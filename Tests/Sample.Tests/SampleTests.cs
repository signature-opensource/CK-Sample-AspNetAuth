using CK.AppIdentity;
using CK.Core;
using CK.Sample.AuthCenter;
using CK.Sample.Front;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Threading;
using static CK.Testing.MonitorTestHelper;

namespace CK.Sample.Tests;

/// <summary>
/// The sample, end to end: its two hosts run in this process (with their own ports and an OpenID Connect scheme on the
/// <see cref="FakeIdentityProvider"/>), and a "browser" (an HttpClient that keeps the cookies) talks to the front. The
/// front relays the session commands to the AuthCenter over the AppIdentity channel.
/// <para>
/// This crosses CK-AppIdentity-Transport (the AuthCenter, the relay) and CK-Cris-TypeScript (the HTTP front) on a real
/// database: their own tests use fakes on the other side.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class SampleTests
{
    // "localhost", not 127.0.0.1: the passkeys are bound to a domain name (the RP ID), never to an IP address.
    const string FrontUrl = "http://localhost:5310";
    const string AuthCenterAddress = "tcp:127.0.0.1:37310";
    const string Callback = FrontUrl + "/.auth/oidc/callback";

    FakeIdentityProvider _idp = null!;
    WebApplication _authCenter = null!;
    WebApplication _front = null!;

    [OneTimeSetUp]
    public async Task StartTheHostsAsync()
    {
        // A fresh AppIdentity store for each run (the identity keys, the pinned remotes, the sessions): the default one
        // is in the user's profile, shared with the manual runs of the sample.
        var store = TestHelper.TestProjectFolder.AppendPart( "TestStore" );
        if( Directory.Exists( store ) ) Directory.Delete( store, recursive: true );
        ApplicationIdentityServiceConfiguration.DefaultStoreRootPath = store;
        _idp = new FakeIdentityProvider();
        _authCenter = await AuthCenterHost.BuildAsync( [], c => c.AddInMemoryCollection( new Dictionary<string, string?>
        {
            ["Urls"] = "http://127.0.0.1:5311",
            ["CK-AppIdentity:ListeningAddress:0"] = AuthCenterAddress,
            ["CK-AppIdentity:AuthCenter:Oidc:Test:Authority"] = _idp.BaseUrl,
            ["CK-AppIdentity:AuthCenter:Oidc:Test:ClientId"] = FakeIdentityProvider.ClientId,
            ["CK-AppIdentity:AuthCenter:Oidc:Test:ClientSecret"] = FakeIdentityProvider.ClientSecret,
            ["CK-AppIdentity:AuthCenter:Oidc:Test:CallbackUrls:0"] = Callback,
            // An unknown identity of the "Test" scheme creates its user (SampleAccountCreation).
            ["CK-AppIdentity:AuthCenter:Oidc:Test:AllowAccountCreation"] = "true",
            // Albert is bound to the "albert-sub" identity of the "Test" scheme.
            ["Demo:OidcScheme"] = "Test",
            ["Demo:OidcSub"] = "albert-sub",
            // The passkey ceremonies are served by the test front (the RP ID "localhost" is the configured one).
            ["CK-AppIdentity:AuthCenter:Passkey:Origins:0"] = FrontUrl
        } ), TestHelper.SolutionFolder.Combine( "AuthCenter/AuthCenter.Host" ) );
        await _authCenter.StartAsync();
        _front = FrontHost.Build( [], c => c.AddInMemoryCollection( new Dictionary<string, string?>
        {
            ["Urls"] = FrontUrl,
            ["CK-AppIdentity:Parties:0:Address"] = AuthCenterAddress
        } ), TestHelper.SolutionFolder.Combine( "Front/Front.Host" ) );
        await _front.StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopTheHostsAsync()
    {
        if( _front != null ) await _front.DisposeAsync();
        if( _authCenter != null ) await _authCenter.DisposeAsync();
        _idp?.Dispose();
    }

    /// <summary>
    /// A browser of the front's origin: it keeps the cookies, sends the Origin header and the bearer token.
    /// </summary>
    sealed class Browser : IDisposable
    {
        readonly HttpClient _http;

        public Browser()
        {
            Cookies = new CookieContainer();
            _http = new HttpClient( new HttpClientHandler { CookieContainer = Cookies, AllowAutoRedirect = false } ) { BaseAddress = new Uri( FrontUrl ) };
            _http.DefaultRequestHeaders.Add( "Origin", FrontUrl );
        }

        public CookieContainer Cookies { get; }

        public string? Token { get; set; }

        public int ActorId { get; set; }

        // Posts a command (["Type",{...}] in the TypeScript set) and returns the "result" of the answer.
        public async Task<JsonElement> SendAsync( string path, string command )
        {
            using var m = new HttpRequestMessage( HttpMethod.Post, path ) { Content = new StringContent( command, Encoding.UTF8, "application/json" ) };
            if( Token != null ) m.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue( "Bearer", Token );
            using var r = await _http.SendAsync( m );
            r.StatusCode.ShouldBe( HttpStatusCode.OK );
            return JsonDocument.Parse( await r.Content.ReadAsStringAsync() ).RootElement.GetProperty( "result" ).Clone();
        }

        // A session command on "/.cris/auth": a successful session result becomes the current session.
        public async Task<JsonElement> SessionAsync( string command )
        {
            var r = await SendAsync( "/.cris/auth", command );
            if( r.ValueKind == JsonValueKind.Array && r[1].ValueKind == JsonValueKind.Object
                && r[1].TryGetProperty( "token", out var token ) && token.ValueKind == JsonValueKind.String )
            {
                Token = token.GetString();
                ActorId = r[1].GetProperty( "info" ).GetProperty( "unsafeActor" ).GetProperty( "actorId" ).GetInt32();
            }
            return r;
        }

        public Task<JsonElement> HelloAsync() => SendAsync( "/.cris", $$"""["CK.Sample.Front.IHelloCommand",{"actorId":{{ActorId}}}]""" );

        public Task<HttpResponseMessage> GetAsync( string path ) => _http.GetAsync( path );

        public void Dispose() => _http.Dispose();
    }

    static string Text( JsonElement result ) => result.ValueKind == JsonValueKind.Array ? result[1].ToString() : result.ToString();

    // A typed value ["Type",value] or the value itself.
    static JsonElement Value( JsonElement result ) => result.ValueKind == JsonValueKind.Array && result.GetArrayLength() == 2 && result[0].ValueKind == JsonValueKind.String ? result[1] : result;

    static bool IsSuccess( JsonElement result ) => Value( result ) is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty( "success", out var s ) && s.GetBoolean();

    // The passkeys listed by IGetPasskeysQCommand: (passkeyId, name).
    static List<(int Id, string Name)> Passkeys( JsonElement result )
    {
        var list = Value( result );
        list.ValueKind.ShouldBe( JsonValueKind.Array, result.ToString() );
        return list.EnumerateArray().Select( Value ).Select( p => (p.GetProperty( "passkeyId" ).GetInt32(), p.GetProperty( "name" ).GetString()!) ).ToList();
    }

    [Test]
    public async Task the_user_manages_its_login_methods_at_the_AuthCenter_through_the_front_Async()
    {
        // The front has no database: these ICommandAuthCenter commands are posted to "/.cris/auth-center" and relayed with
        // the browser's proof (its token and its binding) to the AuthCenter, that establishes the identity again.
        using var anonymous = new Browser();
        Value( await anonymous.SendAsync( "/.cris/auth-center", """["CK.IO.User.UserPasskey.IGetPasskeysQCommand",{"actorId":1}]""" ) ).ValueKind.ShouldNotBe( JsonValueKind.Array, "Refused: not a list." );

        using var b = new Browser();
        await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        var albertId = b.ActorId;
        // A passkey of Albert, stored directly in the AuthCenter.
        var store = _authCenter.Services.GetRequiredService<IPasskeyStore>();
        var handle = await store.GetOrCreateUserHandleAsync( TestHelper.Monitor, albertId );
        (await store.CreateAsync( TestHelper.Monitor, new PasskeyCredential( RandomNumberGenerator.GetBytes( 32 ), albertId, handle, RandomNumberGenerator.GetBytes( 77 ), 0, true, false, Guid.Empty, [], "Sample key" ) )).ShouldBeTrue();

        // The ICommandAuthCenter commands are refused on "/.cris" (their route is declared).
        Text( await b.SendAsync( "/.cris", $$"""["CK.IO.User.UserPasskey.IGetPasskeysQCommand",{"actorId":{{albertId}}}]""" ) ).ShouldContain( "/.cris/auth-center" );

        var listed = Passkeys( await b.SendAsync( "/.cris/auth-center", $$"""["CK.IO.User.UserPasskey.IGetPasskeysQCommand",{"actorId":{{albertId}}}]""" ) );
        var passkeyId = listed.Single( p => p.Name == "Sample key" ).Id;
        IsSuccess( await b.SendAsync( "/.cris/auth-center", $$"""["CK.IO.User.UserPasskey.IRenamePasskeyCommand",{"actorId":{{albertId}},"passkeyId":{{passkeyId}},"name":"Renamed key"}]""" ) ).ShouldBeTrue();
        Passkeys( await b.SendAsync( "/.cris/auth-center", $$"""["CK.IO.User.UserPasskey.IGetPasskeysQCommand",{"actorId":{{albertId}}}]""" ) )
            .ShouldContain( (passkeyId, "Renamed key") );
        // Albert has the Basic scheme: this passkey is not his last login method. The delete requires the Critical level of the login.
        IsSuccess( await b.SendAsync( "/.cris/auth-center", $$"""["CK.IO.User.UserPasskey.IDeletePasskeyCommand",{"actorId":{{albertId}},"actualActorId":{{albertId}},"passkeyId":{{passkeyId}}}]""" ) ).ShouldBeTrue();
        Passkeys( await b.SendAsync( "/.cris/auth-center", $$"""["CK.IO.User.UserPasskey.IGetPasskeysQCommand",{"actorId":{{albertId}}}]""" ) )
            .ShouldNotContain( p => p.Id == passkeyId );

        // Albert changes his password, then logs in with it (and restores it).
        IsSuccess( await b.SendAsync( "/.cris/auth-center", $$"""["CK.IO.User.UserPassword.ISetPasswordCommand",{"actorId":{{albertId}},"userId":{{albertId}},"password":"new-pass"}]""" ) ).ShouldBeTrue();
        using( var again = new Browser() )
        {
            var login = await again.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"new-pass","rememberMe":false}]""" );
            again.Token.ShouldNotBeNull( Text( login ) );
        }
        IsSuccess( await b.SendAsync( "/.cris/auth-center", $$"""["CK.IO.User.UserPassword.ISetPasswordCommand",{"actorId":{{albertId}},"userId":{{albertId}},"password":"pass"}]""" ) ).ShouldBeTrue();

        // After the logout, the same token is refused (the relay's session check is tested in CK-AppIdentity-Transport).
        var token = b.Token;
        await b.SendAsync( "/.cris/auth", """["CK.IO.Authentication.ILogoutSessionCommand",{}]""" );
        b.Token = token;
        Value( await b.SendAsync( "/.cris/auth-center", $$"""["CK.IO.User.UserPasskey.IGetPasskeysQCommand",{"actorId":{{albertId}}}]""" ) ).ValueKind.ShouldNotBe( JsonValueKind.Array, "Refused: not a list." );
    }

    // A string result is typed: ["string","..."] (an error is a CrisResultError object).
    static bool IsString( JsonElement result ) => (result.ValueKind == JsonValueKind.Array ? result[1] : result).ValueKind == JsonValueKind.String;

    [Test]
    public async Task a_basic_login_through_the_front_opens_a_session_on_the_AuthCenter_Async()
    {
        using var b = new Browser();
        var login = await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        login[0].GetString().ShouldBe( "CK.IO.Authentication.ISessionResult" );
        b.Token.ShouldNotBeNull();
        var cookies = b.Cookies.GetAllCookies();
        cookies.ShouldContain( c => c.Name == "ck-auth-binding" && c.Path == "/.cris" && c.HttpOnly );
        cookies.ShouldContain( c => c.Name == "ck-auth-refresh" && c.Path == "/.cris/auth" && c.HttpOnly );

        Text( await b.HelloAsync() ).ShouldBe( "Hello Albert, at the Critical level." );

        // The refresh slides the session (at most Normal: Critical requires a login).
        await b.SessionAsync( """["CK.IO.Authentication.IRefreshSessionCommand",{}]""" );
        Text( await b.HelloAsync() ).ShouldBe( "Hello Albert, at the Normal level." );

        // Logout: the session ends on the AuthCenter, the cookies are cleared.
        await b.SendAsync( "/.cris/auth", """["CK.IO.Authentication.ILogoutSessionCommand",{}]""" );
        b.Cookies.GetAllCookies().ShouldNotContain( c => c.Name == "ck-auth-binding" && !c.Expired );
        Text( await b.HelloAsync() ).ShouldContain( "match the current authentication" );
    }

    [Test]
    public async Task a_stolen_token_is_useless_without_the_binding_cookie_Async()
    {
        using var victim = new Browser();
        await victim.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        using var thief = new Browser { Token = victim.Token, ActorId = victim.ActorId };
        Text( await thief.HelloAsync() ).ShouldContain( "match the current authentication" );
        Text( await victim.HelloAsync() ).ShouldStartWith( "Hello Albert" );
    }

    [Test]
    public async Task a_wrong_password_fails_Async()
    {
        using var b = new Browser();
        var r = await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"not-the-password","rememberMe":false}]""" );
        r[1].GetProperty( "success" ).GetBoolean().ShouldBeFalse();
        b.Token.ShouldBeNull();
    }

    [Test]
    public async Task a_weak_actor_logs_in_at_the_Unsafe_level_and_cannot_send_a_Normal_command_Async()
    {
        using var b = new Browser();
        var r = await b.SessionAsync( """["CK.IO.Authentication.Weak.IWeakLoginCommand",{"weakActorName":"Operator"}]""" );
        r[1].GetProperty( "info" ).GetProperty( "unsafeActor" ).GetProperty( "level" ).GetInt32().ShouldBe( 1, "Unsafe." );
        b.Token.ShouldNotBeNull();
        Text( await b.HelloAsync() ).ShouldNotStartWith( "Hello" );
    }

    [Test]
    public async Task the_passkey_ceremonies_are_served_through_the_front_Async()
    {
        using var b = new Browser();
        Text( await b.SendAsync( "/.cris", """["CK.IO.Authentication.IGetLoginSchemesQCommand",{}]""" ) ).ShouldContain( "Passkey" );

        // The login start: usernameless (no allowed credentials), user verification required, bound to "localhost".
        var start = await b.SessionAsync( """["CK.IO.Authentication.Passkey.IStartPasskeyLoginCommand",{}]""" );
        IsString( start ).ShouldBeTrue( Text( start ) );
        using( var options = JsonDocument.Parse( Text( start ) ) )
        {
            options.RootElement.GetProperty( "rpId" ).GetString().ShouldBe( "localhost" );
            options.RootElement.GetProperty( "userVerification" ).GetString().ShouldBe( "required" );
            options.RootElement.GetProperty( "challenge" ).GetString().ShouldNotBeNullOrEmpty();
            if( options.RootElement.TryGetProperty( "allowCredentials", out var allowed ) ) allowed.GetArrayLength().ShouldBe( 0 );
        }

        // Adding a passkey requires a recent login (Critical): the anonymous is refused.
        var registration = await b.SessionAsync( """["CK.IO.Authentication.Passkey.IStartPasskeyRegistrationCommand",{"actorId":0}]""" );
        IsString( registration ).ShouldBeFalse( $"The anonymous cannot register a passkey ({Text( registration )})." );
    }

    [Test]
    public async Task an_oidc_login_through_the_front_and_the_AuthCenter_Async()
    {
        using var b = new Browser();
        // The schemes come from the AuthCenter.
        Text( await b.SendAsync( "/.cris", """["CK.IO.Authentication.IGetLoginSchemesQCommand",{}]""" ) ).ShouldContain( "Oidc.Test" );

        // Start: the front relays it, the AuthCenter returns the provider's URL; the front sets the remote login cookie.
        var start = await b.SessionAsync( """["CK.IO.Authentication.IStartLoginCommand",{"scheme":"Oidc.Test","inlineMode":false,"rememberMe":false}]""" );
        var url = start[1].GetProperty( "url" ).GetString().ShouldNotBeNull();
        b.Cookies.GetAllCookies().ShouldContain( c => c.Name == "ck-auth-login" && c.Path == "/.auth/oidc/callback" );

        // The user logs in at the provider, that redirects the browser to the front's callback.
        var (redirect, code, state) = _idp.Authorize( url, "albert-sub" );
        redirect.ShouldBe( Callback );
        using var callback = await b.GetAsync( $"/.auth/oidc/callback?state={HttpUtility.UrlEncode( state )}&code={HttpUtility.UrlEncode( code )}" );
        callback.StatusCode.ShouldBe( HttpStatusCode.OK );
        (await callback.Content.ReadAsStringAsync()).ShouldContain( "\"success\":true" );
        _idp.RedeemCount.ShouldBeGreaterThan( 0, "The AuthCenter redeemed the code." );
        _idp.LastAuthMethod.ShouldBe( "client_secret_basic", "The default client authentication." );

        // The page has no token: the client refreshes with the session cookies set by the callback.
        await b.SessionAsync( """["CK.IO.Authentication.IRefreshSessionCommand",{}]""" );
        Text( await b.HelloAsync() ).ShouldBe( "Hello Albert, at the Normal level." );
    }

    [Test]
    public async Task an_unknown_oidc_identity_creates_its_user_named_from_the_claims_Async()
    {
        // Like bob at the Duende demo server: a "name", no "preferred_username". The user name is normalized and, on a
        // clash, suffixed. A run after the first one finds the identities already bound: same users, same names.
        _idp.IdTokenClaims["name"] = "Bob Smith";
        try
        {
            using var bob = new Browser();
            await OidcLoginAsync( bob, "bob-sub" );
            Text( await bob.HelloAsync() ).ShouldBe( "Hello Bob-Smith, at the Normal level." );

            using var otherBob = new Browser();
            await OidcLoginAsync( otherBob, "other-bob-sub" );
            Text( await otherBob.HelloAsync() ).ShouldBe( "Hello Bob-Smith-2, at the Normal level." );
            otherBob.ActorId.ShouldNotBe( bob.ActorId );
        }
        finally
        {
            _idp.IdTokenClaims.Clear();
        }
    }

    async Task OidcLoginAsync( Browser b, string sub )
    {
        var start = await b.SessionAsync( """["CK.IO.Authentication.IStartLoginCommand",{"scheme":"Oidc.Test","inlineMode":false,"rememberMe":false}]""" );
        var url = start[1].GetProperty( "url" ).GetString().ShouldNotBeNull();
        var (_, code, state) = _idp.Authorize( url, sub );
        using var callback = await b.GetAsync( $"/.auth/oidc/callback?state={HttpUtility.UrlEncode( state )}&code={HttpUtility.UrlEncode( code )}" );
        (await callback.Content.ReadAsStringAsync()).ShouldContain( "\"success\":true" );
        await b.SessionAsync( """["CK.IO.Authentication.IRefreshSessionCommand",{}]""" );
    }

    /// <summary>
    /// The UI smoke test (Front.Host/Clients/App/ui-smoke/ui-smoke.mjs) on the built Angular application, in a headless
    /// browser: the public page, the login page (with the "Test" OpenID Connect button), a wrong password, the Weak login
    /// (its hello is refused), the Basic login (the private page in the back-office layout), the user info box, a reload
    /// and the sign out. Ignored when the application is not built (<c>yarn build</c>), or without Node or a browser.
    /// </summary>
    [Test]
    [CancelAfter( 180_000 )]
    public async Task the_Angular_application_passes_the_UI_smoke_test_Async( CancellationToken cancellation )
    {
        var app = TestHelper.SolutionFolder.Combine( "Front/Front.Host/Clients/App" );
        if( !File.Exists( app.Combine( "dist/App/browser/index.html" ) ) )
        {
            Assert.Ignore( "The Angular application is not built (yarn build in Front/Front.Host/Clients/App)." );
        }
        var screenshots = TestHelper.TestProjectFolder.AppendPart( "UISmoke" );
        var start = new ProcessStartInfo( "node" )
        {
            WorkingDirectory = app,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach( var a in new[] { "ui-smoke/ui-smoke.mjs", "--url", FrontUrl, "--oidc", "Test", "--out", screenshots.Path } )
        {
            start.ArgumentList.Add( a );
        }
        Process? node;
        try
        {
            node = Process.Start( start );
        }
        catch( System.ComponentModel.Win32Exception )
        {
            node = null;
        }
        if( node == null ) Assert.Ignore( "Node is not available." );
        using( node )
        {
            var output = node.StandardOutput.ReadToEndAsync( cancellation );
            var error = node.StandardError.ReadToEndAsync( cancellation );
            await node.WaitForExitAsync( cancellation );
            var text = await output + await error;
            TestHelper.Monitor.Info( $"UI smoke test (screenshots in '{screenshots}'):{Environment.NewLine}{text}" );
            if( node.ExitCode == 2 ) Assert.Ignore( "No browser for the UI smoke test (set CK_BROWSER)." );
            node.ExitCode.ShouldBe( 0, text );
        }
    }
}
