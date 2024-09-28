using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.IO;

var builder = WebApplication.CreateSlimBuilder();
var monitor = builder.GetBuilderMonitor();
builder.UseCKMonitoring();

// Ordering of services doesn't matter.
builder.Services.AddControllers();

// Configured cookie policy due to correlation fail when trying to authenticate with oidc.
// => This code has been copied from the previous sample... And I don't understand the why...
//    To be investigated...
builder.Services.Configure<CookiePolicyOptions>( options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Unspecified;
    options.OnAppendCookie = cookieContext => CheckSameSite( cookieContext.Context, cookieContext.CookieOptions );
    options.OnDeleteCookie = cookieContext => CheckSameSite( cookieContext.Context, cookieContext.CookieOptions );

    static void CheckSameSite( HttpContext httpContext, CookieOptions options )
    {
        if( options.SameSite == SameSiteMode.None )
        {
            var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
            // TODO: Use your User Agent library of choice here.
            if( !DisallowsSameSiteNone( userAgent ) )
            {
                options.SameSite = SameSiteMode.Unspecified;
            }
        }

        static bool DisallowsSameSiteNone( string userAgent )
        {
            if( string.IsNullOrEmpty( userAgent ) )
            {
                return false;
            }
            // Cover all iOS based browsers here. This includes:
            // - Safari on iOS 12 for iPhone, iPod Touch, iPad
            // - WkWebview on iOS 12 for iPhone, iPod Touch, iPad
            // - Chrome on iOS 12 for iPhone, iPod Touch, iPad
            // All of which are broken by SameSite=None, because they use the iOS networking stack
            if( userAgent.Contains( "CPU iPhone OS 12" ) || userAgent.Contains( "iPad; CPU OS 12" ) )
            {
                return true;
            }
            // Cover Mac OS X based browsers that use the Mac OS networking stack. This includes:
            // - Safari on Mac OS X.
            // This does not include:
            // - Chrome on Mac OS X
            // Because they do not use the Mac OS networking stack.
            if( userAgent.Contains( "Macintosh; Intel Mac OS X 10_14" ) &&
                userAgent.Contains( "Version/" ) && userAgent.Contains( "Safari" ) )
            {
                return true;
            }
            // Cover Chrome 50-69, because some versions are broken by SameSite=None, 
            // and none in this range require it.
            // Note: this covers some pre-Chromium Edge versions, 
            // but pre-Chromium Edge does not require SameSite=None.
            if( userAgent.Contains( "Chrome/5" ) || userAgent.Contains( "Chrome/6" ) )
            {
                return true;
            }
            return false;
        }
    }
} );

//var authBuilder = new AuthenticationBuilder( builder.Services );
//authBuilder.AddOpenIdConnect( "Oidc.Signature", o =>
//{
//    string authUri = builder.Configuration["Authentication:Oidc.Signature:AuthUri"] ?? "https://login.microsoftonline.com/";
//    string tenantId = builder.Configuration["Authentication:Oidc.Signature:TenantId"]!;
//    o.Authority = $"{authUri.TrimEnd( '/' )}/{tenantId}/v2.0";
//    o.CallbackPath = builder.Configuration["Authentication:Oidc.Signature:CallbackPath"] ?? "/signin-oidc-signature";
//    o.ClientId = builder.Configuration["Authentication:Oidc.Signature:ClientId"];
//    o.ClientSecret = builder.Configuration["Authentication:Oidc.Signature:ClientSecret"];
//    o.AuthenticationMethod = OpenIdConnectRedirectBehavior.RedirectGet;
//    o.ResponseMode = OpenIdConnectResponseMode.FormPost;
//    o.ResponseType = OpenIdConnectResponseType.CodeIdToken;
//    o.TokenValidationParameters = new TokenValidationParameters { ValidIssuer = o.Authority };
//    o.SaveTokens = true;
//    // The OnTicketReceived is the main adapter between the remote provider and the backend:
//    // the information from the Ticket is transfered onto the payload that is the IUserOidc payload.
//    o.Events.OnTicketReceived = c => c.WebFrontAuthOnTicketReceivedAsync<CK.Sample.User.UserOidc.App.IUserOidcInfo>( payload =>
//    {
//        payload.SchemeSuffix = "Signature";
//        payload.Sub = c.Principal.FindFirst( ClaimTypes.NameIdentifier ).Value;
//        payload.DisplayName = c.Principal.FindFirst( "name" ).Value;
//        payload.Username = c.Principal.FindFirst( "preferred_username" ).Value;
//        payload.Email = c.Principal.FindFirst( "verified_primary_email" )?.Value;
//    } );
//} );
//authBuilder.AddOpenIdConnect( "Oidc.Google", options =>
//{
//    options.Authority = builder.Configuration["Authentication:Google:AuthUri"] ?? "https://accounts.google.com/o/oauth2/auth";
//    options.CallbackPath = builder.Configuration["Authentication:Google:CallbackPath"] ?? "/signin-oidc-google";
//    options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
//    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
//    options.TokenValidationParameters = new TokenValidationParameters { ValidIssuer = options.Authority };
//    options.Scope.Add( "email" );
//    options.SaveTokens = true;
//    options.Events.OnRemoteFailure = f => f.WebFrontAuthOnRemoteFailureAsync();
//    options.Events.OnTicketReceived = c => c.WebFrontAuthOnTicketReceivedAsync<CK.Sample.User.UserOidc.App.IUserOidcInfo>( payload =>
//    {
//        payload.SchemeSuffix = "Google";
//        payload.Sub = c.Principal.FindFirst( ClaimTypes.NameIdentifier ).Value;
//        payload.DisplayName = c.Principal.FindFirst( "name" ).Value;
//        payload.Username = c.Principal.FindFirst( "name" ).Value;
//        payload.Email = c.Principal.FindFirst( ClaimTypes.Email ).Value;
//    } );
//} );

// CK.AspNet introduces the AppendApplicationBuilder and helpers that
// handle services and middleware registrations at the same time.
// For middleware ordering, see: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-8.0#middleware-order
// This is hard to remember (at least for me). This SHOULD be automated.
builder.AppendApplicationBuilder( app => app.UseCookiePolicy() );
builder.AppendApplicationBuilder( app => app.UseRouting() );
builder.AddUnsafeAllowAllCors();
builder.AddWebFrontAuth( options => options.ExpireTimeSpan = TimeSpan.FromDays( 1 ) );
builder.AppendApplicationBuilder( app => app.UseEndpoints( endpoints => endpoints.MapControllers() ) );

var map = StObjContextRoot.Load( System.Reflection.Assembly.GetExecutingAssembly(), monitor );
// CK.AspNet exposes this helper.
var app = builder.CKBuild( map );
app.Urls.Add( "http://[::1]:53456" );

app.UseStaticFiles( new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider( Path.Combine( builder.Environment.ContentRootPath, "Clients/AspNetAuthTester/" ) ),
    RequestPath = ""
} );

Process process = new Process();
process.StartInfo.UseShellExecute = true;
process.StartInfo.FileName = $"http://localhost:53456/AspNetAuth-tester.html";
process.Start();

await app.RunAsync().ConfigureAwait( false );

