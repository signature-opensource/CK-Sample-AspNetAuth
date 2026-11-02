<#
.SYNOPSIS
    Builds and starts the CK-Sample-AspNetAuth demo in Development: the Backend, the Front, the Admin, and the browser on the Front.

.DESCRIPTION
    1. Builds Backend.Host, Front.Host and Admin.Host. Their builders generate the code of the hosts, the Angular code
       (ck-gen) and the Backend's database (CKSampleBackend on the local SQL Server).
       The packages of the CK stack are restored from the local CKli feed (<stack root>\.PublicStack\$Local\NuGet) when
       it exists, then from the feeds of nuget.config.
    2. Installs the packages of the Angular applications of the Front and of the Admin (yarn install).
    3. Starts the hosts in the Development environment (DOTNET_ENVIRONMENT): the Backend, then the Front and the Admin,
       and waits until they answer and reach their AuthCenter. In Development, the Front and the Admin start the dev
       servers of their client applications ("ng serve" on 4200 and 4201 for the Front, 4300 for the Admin): the script
       waits until the client applications answer (the dev servers compile first).
    4. Opens the browser on http://localhost:5300 (the Admin is on http://localhost:5302), then waits: press Enter to stop
       the hosts. They are stopped gracefully (their window is closed, like Ctrl+C in it): each host stops the dev
       servers it started.

    The demo logins: Albert / pass (Basic), Operator (Weak), "Continue with Duende" then bob / bob (OpenID Connect).

    Requires the .NET SDK, Node (with Yarn: "corepack enable") and a local SQL Server (integrated security).

.PARAMETER SkipBuild
    Starts the hosts already built (no .NET build nor yarn install).

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
$backendHost = Join-Path $root 'Backend\Backend.Host'
$frontHost = Join-Path $root 'Front\Front.Host'
$frontUrl = 'http://localhost:5300'
$adminHost = Join-Path $root 'Admin\Admin.Host'
$adminUrl = 'http://localhost:5302'
$backendPort = 37300
# The Angular applications (their dev servers: Front/Front.Builder/CKSetup.xml, Admin/Admin.Builder/Program.cs).
$clientApps = @( (Join-Path $frontHost 'Clients\App'), (Join-Path $frontHost 'Clients\MiniApp'), (Join-Path $adminHost 'Clients\App') )
$devServerPorts = @( 4200, 4201, 4300 )

function Write-Step( [string] $text ) { Write-Host "`n==> $text" -ForegroundColor Cyan }

function Test-Port( [int] $port ) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect( '127.0.0.1', $port ); return $true } catch { return $false } finally { $c.Dispose() }
}

function Invoke-Native( [string] $what, [scriptblock] $command ) {
    & $command
    if( $LASTEXITCODE -ne 0 ) { throw "$what failed (exit code $LASTEXITCODE)." }
}

# The schemes of the Backend, asked through a front (the Front or the Admin): proves that it answers and reaches its
# AuthCenter.
function Get-LoginSchemes( [string] $url ) {
    try {
        # "/.cris" reads the commands of the request's type set (the client applications named "App").
        $r = Invoke-RestMethod -Method Post -Uri "$url/.cris?TypeFilterName=TypeScriptApp" -ContentType 'application/json' `
                               -Headers @{ Origin = $url } -Body '["CK.IO.Authentication.IGetLoginSchemesQCommand",{}]' -TimeoutSec 5
        # The result is typed: ["L(CK.IO.Authentication.ILoginSchemeInfo?)", [ { "name": "Basic", ... }, ... ]].
        if( $r.result -is [array] -and $r.result.Count -eq 2 -and $r.result[1] -is [array] -and $r.result[1].Count -gt 0 ) { return $r.result[1] }
    } catch { }
    return $null
}

# Whether a client application answers (through its dev server).
function Test-Page( [string] $url ) {
    try { return (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 30).StatusCode -eq 200 } catch { return $false }
}

# Sends Ctrl+C to the console of a process. It runs in a hidden helper process: the helper attaches to the host's console,
# ignores the signal itself and generates it for the console (a console window can't be closed from another process:
# "taskkill" without "/F" fails on a console application).
$sendCtrlC = @'
param( [int] $ProcessId )
Add-Type -Namespace Win32 -Name Console -MemberDefinition @"
[System.Runtime.InteropServices.DllImport( "kernel32.dll" )] public static extern bool FreeConsole();
[System.Runtime.InteropServices.DllImport( "kernel32.dll" )] public static extern bool AttachConsole( uint dwProcessId );
[System.Runtime.InteropServices.DllImport( "kernel32.dll" )] public static extern bool SetConsoleCtrlHandler( System.IntPtr handler, bool add );
[System.Runtime.InteropServices.DllImport( "kernel32.dll" )] public static extern bool GenerateConsoleCtrlEvent( uint dwCtrlEvent, uint dwProcessGroupId );
"@
[Win32.Console]::FreeConsole() | Out-Null
if( -not [Win32.Console]::AttachConsole( $ProcessId ) ) { exit 1 }
[Win32.Console]::SetConsoleCtrlHandler( [IntPtr]::Zero, $true ) | Out-Null
$ok = [Win32.Console]::GenerateConsoleCtrlEvent( 0, 0 )
Start-Sleep -Milliseconds 500
[Win32.Console]::FreeConsole() | Out-Null
if( $ok ) { exit 0 } else { exit 2 }
'@

# Stops a host gracefully, like Ctrl+C in its window: it stops its web applications and the dev servers it started.
# Its process tree is killed if it doesn't exit in time.
function Stop-Host( [System.Diagnostics.Process] $p ) {
    if( $p.HasExited ) { return }
    $command = "& { $sendCtrlC } -ProcessId $($p.Id)"
    $encoded = [Convert]::ToBase64String( [Text.Encoding]::Unicode.GetBytes( $command ) )
    Start-Process powershell -ArgumentList '-NoProfile', '-EncodedCommand', $encoded -WindowStyle Hidden -Wait
    if( -not $p.WaitForExit( 15000 ) ) {
        Write-Host "The process $($p.Id) didn't stop: its process tree is killed." -ForegroundColor Yellow
        cmd /c "taskkill /PID $($p.Id) /T /F >nul 2>&1"
    }
}

$busy = @( $backendPort, 5300, 5302 ) + $devServerPorts | Where-Object { Test-Port $_ }
if( $busy ) {
    throw "The ports $($busy -join ', ') are in use: the demo (or a dev server) may already be running."
}
foreach( $tool in @( 'dotnet', 'node' ) + $(if( $SkipBuild ) { @() } else { @( 'yarn' ) }) ) {
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
        foreach( $project in @( $backendHost, $frontHost, $adminHost ) ) {
            Invoke-Native "The restore of $(Split-Path -Leaf $project)" { dotnet restore $project --configfile $config -v q }
            Invoke-Native "The build of $(Split-Path -Leaf $project) (SQL Server is required by the Backend's builder)" { dotnet build $project --no-restore -v q -nologo }
        }
    }
    finally {
        Remove-Item $config -ErrorAction SilentlyContinue
    }

    # The dev servers compile the Angular applications: only their packages are installed.
    foreach( $a in $clientApps ) {
        Write-Step "Installing the packages of $a"
        Push-Location $a
        try {
            Invoke-Native 'yarn install' { yarn install }
        }
        finally {
            Pop-Location
        }
    }
}

foreach( $required in @( "$backendHost\bin\Debug\net10.0\Backend.Host.exe", "$frontHost\bin\Debug\net10.0\Front.Host.exe",
                         "$adminHost\bin\Debug\net10.0\Admin.Host.exe" ) + ($clientApps | ForEach-Object { Join-Path $_ '.pnp.cjs' }) ) {
    # The client applications use Yarn Plug'n'Play: "yarn install" writes their ".pnp.cjs".
    if( -not (Test-Path $required) ) { throw "'$required' is missing: run this script without -SkipBuild." }
}

$started = @()
$previousEnvironment = $env:DOTNET_ENVIRONMENT
# The hosts inherit it: the Development environment ("#Dev" is their AppIdentity environment).
$env:DOTNET_ENVIRONMENT = 'Development'
try {
    # Each host runs in its own console window (its logs), from its project folder (its appsettings.json).
    Write-Step 'Starting the Backend'
    $started += Start-Process -FilePath "$backendHost\bin\Debug\net10.0\Backend.Host.exe" -WorkingDirectory $backendHost -PassThru
    $deadline = (Get-Date).AddSeconds( 60 )
    while( -not (Test-Port $backendPort) ) {
        if( $started[0].HasExited ) { throw 'The Backend stopped (see its window or Backend.Host\Logs). Is SQL Server running?' }
        if( (Get-Date) -gt $deadline ) { throw "The Backend does not listen on port $backendPort." }
        Start-Sleep -Milliseconds 500
    }

    Write-Step 'Starting the Front'
    $started += Start-Process -FilePath "$frontHost\bin\Debug\net10.0\Front.Host.exe" -WorkingDirectory $frontHost -PassThru
    $deadline = (Get-Date).AddSeconds( 60 )
    while( -not ($schemes = Get-LoginSchemes $frontUrl) ) {
        if( $started[1].HasExited ) { throw 'The Front stopped (see its window or Front.Host\Logs).' }
        if( (Get-Date) -gt $deadline ) { throw 'The Front does not reach its AuthCenter.' }
        Start-Sleep -Milliseconds 500
    }
    Write-Host "Login schemes: $(($schemes | ForEach-Object { $_.name }) -join ', ')"

    Write-Step 'Starting the Admin'
    $started += Start-Process -FilePath "$adminHost\bin\Debug\net10.0\Admin.Host.exe" -WorkingDirectory $adminHost -PassThru
    $deadline = (Get-Date).AddSeconds( 60 )
    while( -not (Get-LoginSchemes $adminUrl) ) {
        if( $started[2].HasExited ) { throw 'The Admin stopped (see its window or Admin.Host\Logs).' }
        if( (Get-Date) -gt $deadline ) { throw 'The Admin does not reach its AuthCenter.' }
        Start-Sleep -Milliseconds 500
    }

    Write-Step 'Waiting for the client applications (their dev servers compile them)'
    $deadline = (Get-Date).AddSeconds( 180 )
    foreach( $page in @( "$frontUrl/", "$frontUrl/mini-app/", "$adminUrl/" ) ) {
        while( -not (Test-Page $page) ) {
            if( (Get-Date) -gt $deadline ) { throw "The client application $page does not answer (see the windows of the hosts)." }
            Start-Sleep -Seconds 2
        }
        Write-Host "$page answers."
    }

    Write-Step "The demo runs on $frontUrl (the Admin on $adminUrl)"
    Write-Host 'Albert / pass (Basic), Operator (Weak), "Continue with Duende" then bob / bob (OpenID Connect).'
    Write-Host 'On the Admin, Albert / pass administers the users of the "Demo" workspace.'
    if( -not $NoBrowser ) { Start-Process $frontUrl }
    Write-Host "`nPress Enter to stop the demo." -ForegroundColor Yellow
    [Console]::ReadLine() | Out-Null
}
finally {
    $env:DOTNET_ENVIRONMENT = $previousEnvironment
    # The fronts first (they stop their dev servers), the Backend last.
    [array]::Reverse( $started )
    foreach( $p in $started ) { Stop-Host $p }
    if( $started.Count -gt 0 ) {
        $left = $devServerPorts | Where-Object { Test-Port $_ }
        if( $left ) { Write-Host "Something still listens on $($left -join ', ')." -ForegroundColor Yellow }
        Write-Host 'The demo is stopped.'
    }
}
