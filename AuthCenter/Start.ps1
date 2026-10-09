<#
.SYNOPSIS
    Builds and starts the CK-Sample-AspNetAuth demo: the AuthCenter, the Front, and the browser on the application.

.DESCRIPTION
    1. Builds AuthCenter.Host and Front.Host. Their builders generate the code of the hosts, the Angular code (ck-gen) and
       the AuthCenter's database (CKSampleAuthCenter on the local SQL Server).
       The packages of the CK stack are restored from the local CKli feed (<stack root>\.PublicStack\$Local\NuGet) when
       it exists, then from the feeds of nuget.config.
    2. Builds the Angular application (yarn install, yarn build).
    3. Starts AuthCenter.Host, waits for its AppIdentity listener, starts Front.Host and waits until the Front answers
       and reaches its AuthCenter.
    4. Opens the browser on http://localhost:5300, then waits: press Enter to stop both hosts.

    The demo logins: Albert / pass (Basic), Operator (Weak), "Continue with Duende" then bob / bob (OpenID Connect).

    Requires the .NET SDK, Node (with Yarn: "corepack enable") and a local SQL Server (integrated security).

.PARAMETER SkipBuild
    Starts the hosts already built (no .NET nor Angular build).

.PARAMETER NoBrowser
    Does not open the browser.
#>
param(
    [switch] $SkipBuild,
    [switch] $NoBrowser
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$authCenterHost = Join-Path $root 'AuthCenter\AuthCenter.Host'
$frontHost = Join-Path $root 'Front\Front.Host'
$app = Join-Path $frontHost 'Clients\App'
$frontUrl = 'http://localhost:5300'
$authCenterPort = 37300

function Write-Step( [string] $text ) { Write-Host "`n==> $text" -ForegroundColor Cyan }

function Test-Port( [int] $port ) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect( '127.0.0.1', $port ); return $true } catch { return $false } finally { $c.Dispose() }
}

function Invoke-Native( [string] $what, [scriptblock] $command ) {
    & $command
    if( $LASTEXITCODE -ne 0 ) { throw "$what failed (exit code $LASTEXITCODE)." }
}

# The schemes of the AuthCenter, asked through the Front: proves that the Front answers and reaches its AuthCenter.
function Get-LoginSchemes {
    try {
        $r = Invoke-RestMethod -Method Post -Uri "$frontUrl/.cris" -ContentType 'application/json' `
                               -Headers @{ Origin = $frontUrl } -Body '["CK.IO.Authentication.IGetLoginSchemesQCommand",{}]' -TimeoutSec 5
        # The result is typed: ["L(string?)", [ "Basic", ... ]].
        if( $r.result -is [array] -and $r.result.Count -eq 2 -and $r.result[1] -is [array] -and $r.result[1].Count -gt 0 ) { return $r.result[1] }
    } catch { }
    return $null
}

if( (Test-Port $authCenterPort) -or (Test-Port 5300) ) {
    throw "The ports $authCenterPort or 5300 are in use: the demo may already be running."
}
foreach( $tool in @( 'dotnet' ) + $(if( $SkipBuild ) { @() } else { @( 'node', 'yarn' ) }) ) {
    if( -not (Get-Command $tool -ErrorAction SilentlyContinue) ) { throw "'$tool' is not available (see the README)." }
}

if( -not $SkipBuild ) {
    Write-Step 'Building the hosts (the builders generate the code and the database)'
    # A temporary NuGet configuration: the local CKli feed (when it exists) before the feeds of the repository.
    $config = Join-Path ([System.IO.Path]::GetTempPath()) 'CK-Sample-AspNetAuth.nuget.config'
    [xml] $xml = Get-Content (Join-Path $root 'nuget.config')
    $localFeed = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) '.PublicStack\$Local\NuGet'
    if( Test-Path $localFeed ) {
        Write-Host "Local CKli feed: $localFeed"
        $add = $xml.CreateElement( 'add' )
        $add.SetAttribute( 'key', 'CKli-Local' )
        $add.SetAttribute( 'value', $localFeed )
        $sources = $xml.configuration.packageSources
        $sources.InsertAfter( $add, $sources.SelectSingleNode( 'clear' ) ) | Out-Null
        $mapping = $xml.configuration.SelectSingleNode( 'packageSourceMapping' )
        if( $mapping ) {
            $source = $xml.CreateElement( 'packageSource' )
            $source.SetAttribute( 'key', 'CKli-Local' )
            $pattern = $xml.CreateElement( 'package' )
            $pattern.SetAttribute( 'pattern', '*' )
            $source.AppendChild( $pattern ) | Out-Null
            $mapping.AppendChild( $source ) | Out-Null
        }
    }
    $xml.Save( $config )
    try {
        foreach( $project in @( $authCenterHost, $frontHost ) ) {
            Invoke-Native "The restore of $(Split-Path -Leaf $project)" { dotnet restore $project --configfile $config -v q }
            Invoke-Native "The build of $(Split-Path -Leaf $project) (SQL Server is required by the AuthCenter's builder)" { dotnet build $project --no-restore -v q -nologo }
        }
    }
    finally {
        Remove-Item $config -ErrorAction SilentlyContinue
    }

    Write-Step 'Building the Angular application'
    Push-Location $app
    try {
        Invoke-Native 'yarn install' { yarn install }
        Invoke-Native 'yarn build' { yarn build }
    }
    finally {
        Pop-Location
    }
}

foreach( $exe in @( "$authCenterHost\bin\Debug\net10.0\AuthCenter.Host.exe", "$frontHost\bin\Debug\net10.0\Front.Host.exe", "$app\dist\App\browser\index.html" ) ) {
    if( -not (Test-Path $exe) ) { throw "'$exe' is missing: run this script without -SkipBuild." }
}

$started = @()
try {
    # Each host runs in its own console window (its logs), from its project folder (its appsettings.json).
    Write-Step 'Starting the AuthCenter'
    $started += Start-Process -FilePath "$authCenterHost\bin\Debug\net10.0\AuthCenter.Host.exe" -WorkingDirectory $authCenterHost -PassThru
    $deadline = (Get-Date).AddSeconds( 60 )
    while( -not (Test-Port $authCenterPort) ) {
        if( $started[0].HasExited ) { throw 'The AuthCenter stopped (see its window or AuthCenter.Host\Logs). Is SQL Server running?' }
        if( (Get-Date) -gt $deadline ) { throw "The AuthCenter does not listen on port $authCenterPort." }
        Start-Sleep -Milliseconds 500
    }

    Write-Step 'Starting the Front'
    $started += Start-Process -FilePath "$frontHost\bin\Debug\net10.0\Front.Host.exe" -WorkingDirectory $frontHost -PassThru
    $deadline = (Get-Date).AddSeconds( 60 )
    while( -not ($schemes = Get-LoginSchemes) ) {
        if( $started[1].HasExited ) { throw 'The Front stopped (see its window or Front.Host\Logs).' }
        if( (Get-Date) -gt $deadline ) { throw 'The Front does not reach its AuthCenter.' }
        Start-Sleep -Milliseconds 500
    }
    Write-Host "Login schemes: $($schemes -join ', ')"

    Write-Step "The demo runs on $frontUrl"
    Write-Host 'Albert / pass (Basic), Operator (Weak), "Continue with Duende" then bob / bob (OpenID Connect).'
    if( -not $NoBrowser ) { Start-Process $frontUrl }
    Write-Host "`nPress Enter to stop the demo." -ForegroundColor Yellow
    [Console]::ReadLine() | Out-Null
}
finally {
    foreach( $p in $started ) {
        if( -not $p.HasExited ) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    }
    if( $started.Count -gt 0 ) { Write-Host 'The demo is stopped.' }
}
