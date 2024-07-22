using CK.AspNet.Auth;
using CK.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Security.Claims;
using System.Threading.Tasks;


var builder = WebApplication.CreateSlimBuilder();
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

var authBuilder = new AuthenticationBuilder( builder.Services );
authBuilder.AddGoogle( "Google", o =>
    {
        o.ClientId = builder.Configuration["Authentication:Google:ClientId"];
        o.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
        o.Scope.Add( "profile" );
        o.Scope.Add( "email" );

        o.Events.OnRemoteFailure = f => f.WebFrontAuthOnRemoteFailureAsync();

        // Google package filters the original claims to standard ones, but "email_verified" and "pictures" are lost.
        // This is why here, we intercept the early ticket creation and save the fields in the AuthenticationProperties.Parameters.
        // (Parameters are a simple Dictionary<string,object> that is transient, as opposed to the Items that are persisted and follow
        // the whole authentication flow).
        o.Events.OnCreatingTicket = c =>
        {
            //c.Properties.Parameters["picture"] = (string)c.User["picture"];
            c.Properties.Parameters["verified_email"] = c.User.GetProperty( "verified_email" ).ToString();
            return Task.CompletedTask;
        };

        o.Events.OnTicketReceived = c => c.WebFrontAuthOnTicketReceivedAsync<UserBasicGoogleFacebook.App.IUserGoogleInfo>( payload =>
        {
            payload.GoogleAccountId = c.Principal.FindFirst( ClaimTypes.NameIdentifier ).Value;
            payload.EMail = c.Principal.FindFirst( ClaimTypes.Email ).Value;
            payload.FirstName = c.Principal.FindFirst( ClaimTypes.GivenName ).Value;
            payload.LastName = c.Principal.FindFirst( ClaimTypes.Surname ).Value;
            payload.UserName = c.Principal.FindFirst( ClaimTypes.Name ).Value;
            Throw.DebugAssert( c.Properties != null );
            payload.EMailVerified = c.Properties.Parameters.GetValueOrDefault( "verified_email" ) is string s && s.Equals( "True", StringComparison.OrdinalIgnoreCase );
        } );
    } );
authBuilder.AddFacebook( "Facebook", o =>
    {
        o.AppId = builder.Configuration["Authentication:Facebook:ClientId"];
        o.AppSecret = builder.Configuration["Authentication:Facebook:ClientSecret"];
        
        o.Events.OnRemoteFailure = f => f.WebFrontAuthOnRemoteFailureAsync();

        o.Events.OnTicketReceived = c => c.WebFrontAuthOnTicketReceivedAsync<UserBasicGoogleFacebook.App.Model.IUserFacebookInfo>( payload =>
        {
            payload.FacebookAccountId = c.Principal.FindFirst( ClaimTypes.NameIdentifier ).Value;
            payload.UserName = c.Principal.FindFirst( ClaimTypes.Name )?.Value;
            // User can decline the "email" scope.
            payload.EMail = c.Principal.FindFirst( ClaimTypes.Email )?.Value;
            payload.FirstName = c.Principal.FindFirst( ClaimTypes.GivenName ).Value;
            payload.LastName = c.Principal.FindFirst( ClaimTypes.Surname ).Value;
        } );
    } );

// CK.AspNet introduces the AppendApplicationBuilder and helpers that
// handle services and middleware registrations at the same time.
// For middleware ordering, see: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-8.0#middleware-order
// This is hard to remember (at least for me). This SHOULD be automated.
builder.AppendApplicationBuilder( app => app.UseCookiePolicy() );
builder.AppendApplicationBuilder( app => app.UseRouting() );
builder.AddUnsafeAllowAllCors();
builder.AddWebFrontAuth( options => options.ExpireTimeSpan = TimeSpan.FromDays( 1 ) );
builder.AppendApplicationBuilder( app => app.UseEndpoints( endpoints => endpoints.MapControllers() ) );

// CK.AspNet exposes this helper.
var map = CK.Core.StObjContextRoot.Load( System.Reflection.Assembly.GetExecutingAssembly(), builder.GetBuilderMonitor() );
var app = builder.CKBuild( map );

app.Urls.Add( "http://[::1]:53455" );

await app.RunAsync();

