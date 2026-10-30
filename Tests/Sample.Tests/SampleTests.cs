using CK.AppIdentity;
using CK.Core;
using CK.Cris;
using CK.Sample.Admin;
using CK.Sample.Backend;
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
/// The sample, end to end: its three hosts run in this process (with their own ports and an OpenID Connect scheme on the
/// <see cref="FakeIdentityProvider"/>), and a "browser" (an HttpClient that keeps the cookies) talks to the front (or to
/// the Admin). The fronts relay the session commands to the Backend over the AppIdentity channel.
/// <para>
/// This crosses CK-AppIdentity-Transport (the Backend, the relay) and CK-Cris-TypeScript (the HTTP front) on a real
/// database: their own tests use fakes on the other side.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class SampleTests
{
    // "localhost", not 127.0.0.1: the passkeys are bound to a domain name (the RP ID), never to an IP address.
    const string FrontUrl = "http://localhost:5310";
    const string BackendAddress = "tcp:127.0.0.1:37310";
    const string Callback = FrontUrl + "/.auth/oidc/callback";
    const string AdminUrl = "http://localhost:5312";

    FakeIdentityProvider _idp = null!;
    WebApplication _backend = null!;
    WebApplication _front = null!;
    WebApplication _admin = null!;

    [OneTimeSetUp]
    public async Task StartTheHostsAsync()
    {
        // A fresh AppIdentity store for each run (the identity keys, the pinned remotes, the sessions): the default one
        // is in the user's profile, shared with the manual runs of the sample.
        var store = TestHelper.TestProjectFolder.AppendPart( "TestStore" );
        if( Directory.Exists( store ) ) Directory.Delete( store, recursive: true );
        ApplicationIdentityServiceConfiguration.DefaultStoreRootPath = store;
        _idp = new FakeIdentityProvider();
        _backend = await BackendHost.BuildAsync( [], c => c.AddInMemoryCollection( new Dictionary<string, string?>
        {
            ["Urls"] = "http://127.0.0.1:5311",
            ["CK-AppIdentity:Defaults:ListeningAddress:0"] = BackendAddress,
            ["CK-AppIdentity:Local:AuthCenter:Oidc:Test:Authority"] = _idp.BaseUrl,
            ["CK-AppIdentity:Local:AuthCenter:Oidc:Test:ClientId"] = FakeIdentityProvider.ClientId,
            ["CK-AppIdentity:Local:AuthCenter:Oidc:Test:ClientSecret"] = FakeIdentityProvider.ClientSecret,
            ["CK-AppIdentity:Local:AuthCenter:Oidc:Test:CallbackUrls:0"] = Callback,
            // An unknown identity of the "Test" scheme creates its user (SampleAccountCreation).
            ["CK-AppIdentity:Local:AuthCenter:Oidc:Test:AllowAccountCreation"] = "true",
            // Albert is bound to the "albert-sub" identity of the "Test" scheme.
            ["Demo:OidcScheme"] = "Test",
            ["Demo:OidcSub"] = "albert-sub",
            // The passkey ceremonies are served by the test front (the RP ID "localhost" is the configured one).
            ["CK-AppIdentity:Local:AuthCenter:Passkey:Origins:0"] = FrontUrl,
            // A short Critical level for the Basic logins: the UI smoke test sees its end (--critical-wait).
            ["CK-AppIdentity:Local:AuthCenter:CriticalLifetimes:Basic"] = "00:00:15"
        } ), TestHelper.SolutionFolder.Combine( "Backend/Backend.Host" ) );
        await _backend.StartAsync();
        _front = FrontHost.Build( [], c => c.AddInMemoryCollection( new Dictionary<string, string?>
        {
            ["Urls"] = FrontUrl,
            ["CK-AppIdentity:Parties:0:Address"] = BackendAddress
        } ), TestHelper.SolutionFolder.Combine( "Front/Front.Host" ) );
        await _front.StartAsync();
        _admin = AdminHost.Build( [], c => c.AddInMemoryCollection( new Dictionary<string, string?>
        {
            ["Urls"] = AdminUrl,
            ["CK-AppIdentity:Parties:0:Address"] = BackendAddress
        } ), TestHelper.SolutionFolder.Combine( "Admin/Admin.Host" ) );
        await _admin.StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopTheHostsAsync()
    {
        if( _admin != null ) await _admin.DisposeAsync();
        if( _front != null ) await _front.DisposeAsync();
        if( _backend != null ) await _backend.DisposeAsync();
        _idp?.Dispose();
    }

    /// <summary>
    /// A browser of the front's origin (or of the Admin's one): it keeps the cookies, sends the Origin header and the
    /// bearer token.
    /// </summary>
    sealed class Browser : IDisposable
    {
        readonly HttpClient _http;

        public Browser( string origin = FrontUrl )
        {
            Cookies = new CookieContainer();
            _http = new HttpClient( new HttpClientHandler { CookieContainer = Cookies, AllowAutoRedirect = false } ) { BaseAddress = new Uri( origin ) };
            _http.DefaultRequestHeaders.Add( "Origin", origin );
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
    public async Task the_user_manages_its_login_methods_at_the_Backend_through_the_front_Async()
    {
        // The front has no database: these commands are posted to "/.cris" and dispatched to the Backend (that handles
        // them) with the browser's proof (its token and its binding): the Backend verifies it.
        using var anonymous = new Browser();
        Value( await anonymous.SendAsync( "/.cris", """["CK.IO.User.UserPasskey.IGetPasskeysQCommand",{"actorId":1}]""" ) ).ValueKind.ShouldNotBe( JsonValueKind.Array, "Refused: not a list." );

        using var b = new Browser();
        await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        var albertId = b.ActorId;
        // A passkey of Albert, stored directly in the Backend (a unique name: the database outlives the test runs).
        var keyName = $"Sample key {Guid.NewGuid():N}";
        var store = _backend.Services.GetRequiredService<IPasskeyStore>();
        var handle = await store.GetOrCreateUserHandleAsync( TestHelper.Monitor, albertId );
        (await store.CreateAsync( TestHelper.Monitor, new PasskeyCredential( RandomNumberGenerator.GetBytes( 32 ), albertId, handle, RandomNumberGenerator.GetBytes( 77 ), 0, true, false, Guid.Empty, [], keyName ) )).ShouldBeTrue();

        var listed = Passkeys( await b.SendAsync( "/.cris", $$"""["CK.IO.User.UserPasskey.IGetPasskeysQCommand",{"actorId":{{albertId}}}]""" ) );
        var passkeyId = listed.Single( p => p.Name == keyName ).Id;
        IsSuccess( await b.SendAsync( "/.cris", $$"""["CK.IO.User.UserPasskey.IRenamePasskeyCommand",{"actorId":{{albertId}},"passkeyId":{{passkeyId}},"name":"Renamed key"}]""" ) ).ShouldBeTrue();
        Passkeys( await b.SendAsync( "/.cris", $$"""["CK.IO.User.UserPasskey.IGetPasskeysQCommand",{"actorId":{{albertId}}}]""" ) )
            .ShouldContain( (passkeyId, "Renamed key") );
        // Albert has the Basic scheme: this passkey is not his last login method. The delete requires the Critical level of the login.
        IsSuccess( await b.SendAsync( "/.cris", $$"""["CK.IO.User.UserPasskey.IDeletePasskeyCommand",{"actorId":{{albertId}},"actualActorId":{{albertId}},"passkeyId":{{passkeyId}}}]""" ) ).ShouldBeTrue();
        Passkeys( await b.SendAsync( "/.cris", $$"""["CK.IO.User.UserPasskey.IGetPasskeysQCommand",{"actorId":{{albertId}}}]""" ) )
            .ShouldNotContain( p => p.Id == passkeyId );

        // Albert changes his password, then logs in with it (and restores it).
        IsSuccess( await b.SendAsync( "/.cris", $$"""["CK.IO.User.UserPassword.ISetPasswordCommand",{"actorId":{{albertId}},"userId":{{albertId}},"password":"new-pass"}]""" ) ).ShouldBeTrue();
        using( var again = new Browser() )
        {
            var login = await again.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"new-pass","rememberMe":false}]""" );
            again.Token.ShouldNotBeNull( Text( login ) );
        }
        IsSuccess( await b.SendAsync( "/.cris", $$"""["CK.IO.User.UserPassword.ISetPasswordCommand",{"actorId":{{albertId}},"userId":{{albertId}},"password":"pass"}]""" ) ).ShouldBeTrue();

        // After the logout, the same token is refused (the binding cookie is gone: the request is anonymous at the front).
        var token = b.Token;
        await b.SendAsync( "/.cris/auth", """["CK.IO.Authentication.ILogoutSessionCommand",{}]""" );
        b.Token = token;
        Value( await b.SendAsync( "/.cris", $$"""["CK.IO.User.UserPasskey.IGetPasskeysQCommand",{"actorId":{{albertId}}}]""" ) ).ValueKind.ShouldNotBe( JsonValueKind.Array, "Refused: not a list." );
    }

    [Test]
    public async Task the_profile_page_reads_the_profile_and_the_sign_in_methods_at_the_Backend_Async()
    {
        using var b = new Browser();
        await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        var albertId = b.ActorId;

        // The profile (the application's leaf, read by the Backend): Albert administers the demo workspace.
        var profile = Value( await b.SendAsync( "/.cris", $$"""["CK.IO.Actor.IGetUserProfileQCommand",{"actorId":{{albertId}},"userId":{{albertId}}}]""" ) );
        profile.GetProperty( "userName" ).GetString().ShouldBe( "Albert", profile.ToString() );
        profile.GetProperty( "preferredWorkspaceId" ).GetInt32().ShouldBeGreaterThan( 0 );
        Value( profile.GetProperty( "groups" ) ).EnumerateArray().Select( Value )
            .ShouldContain( g => g.GetProperty( "grantLevel" ).GetInt32() >= 112, "Albert administers the demo workspace." );

        // The linked accounts: Albert is bound to the "albert-sub" identity of the "Test" scheme.
        var identities = Value( await b.SendAsync( "/.cris", $$"""["CK.IO.User.UserOidc.IGetOidcIdentitiesQCommand",{"actorId":{{albertId}}}]""" ) );
        identities.EnumerateArray().Select( Value ).ShouldContain( i => i.GetProperty( "schemeSuffix" ).GetString() == "Test", identities.ToString() );

        // Albert has a password: adding one is refused (the profile proposes to change it).
        var add = Value( await b.SendAsync( "/.cris", $$"""["CK.IO.User.UserPassword.IAddPasswordCommand",{"actorId":{{albertId}},"actualActorId":{{albertId}},"password":"another"}]""" ) );
        add.GetProperty( "success" ).GetBoolean().ShouldBeFalse( add.ToString() );
    }

    [Test]
    public async Task the_Admin_front_administers_the_users_at_the_Backend_Async()
    {
        // The second remote of the AuthCenter: its sessions are its own (its origin, its cookies).
        using var admin = new Browser( AdminUrl );
        await admin.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        admin.Token.ShouldNotBeNull();
        var albertId = admin.ActorId;
        var profile = Value( await admin.SendAsync( "/.cris", $$"""["CK.IO.Actor.IGetUserProfileQCommand",{"actorId":{{albertId}},"userId":{{albertId}}}]""" ) );
        var workspaceId = profile.GetProperty( "preferredWorkspaceId" ).GetInt32();

        // Albert administers the demo workspace: the Backend lists its users (the application's query, with the
        // banishments and the e-mails).
        var users = Value( await admin.SendAsync( "/.cris", $$"""["CK.IO.UserManagement.IGetWorkspaceUsersQCommand",{"actorId":{{albertId}},"currentWorkspaceId":{{workspaceId}},"currentCultureName":"en"}]""" ) );
        users.ValueKind.ShouldBe( JsonValueKind.Array, users.ToString() );
        users.EnumerateArray().Select( Value ).ShouldContain( u => u.GetProperty( "userName" ).GetString() == "Albert", users.ToString() );

        // An anonymous caller is refused.
        using var anonymous = new Browser( AdminUrl );
        Value( await anonymous.SendAsync( "/.cris", $$"""["CK.IO.UserManagement.IGetWorkspaceUsersQCommand",{"actorId":{{albertId}},"currentWorkspaceId":{{workspaceId}},"currentCultureName":"en"}]""" ) )
            .ValueKind.ShouldNotBe( JsonValueKind.Array, "Refused: anonymous." );
    }

    // A string result is typed: ["string","..."] (an error is a CrisResultError object).
    static bool IsString( JsonElement result ) => (result.ValueKind == JsonValueKind.Array ? result[1] : result).ValueKind == JsonValueKind.String;

    [Test]
    public async Task a_basic_login_through_the_front_opens_a_session_on_the_Backend_Async()
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

        // Logout: the session ends on the Backend, the cookies are cleared.
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
    public async Task an_oidc_login_through_the_front_and_the_Backend_Async()
    {
        using var b = new Browser();
        // The schemes come from the Backend.
        Text( await b.SendAsync( "/.cris", """["CK.IO.Authentication.IGetLoginSchemesQCommand",{}]""" ) ).ShouldContain( "Oidc.Test" );

        // Start: the front relays it, the Backend returns the provider's URL; the front sets the remote login cookie.
        var start = await b.SessionAsync( """["CK.IO.Authentication.IStartLoginCommand",{"scheme":"Oidc.Test","inlineMode":false,"rememberMe":false}]""" );
        var url = start[1].GetProperty( "url" ).GetString().ShouldNotBeNull();
        b.Cookies.GetAllCookies().ShouldContain( c => c.Name == "ck-auth-login" && c.Path == "/.auth/oidc/callback" );

        // The user logs in at the provider, that redirects the browser to the front's callback.
        var (redirect, code, state) = _idp.Authorize( url, "albert-sub" );
        redirect.ShouldBe( Callback );
        using var callback = await b.GetAsync( $"/.auth/oidc/callback?state={HttpUtility.UrlEncode( state )}&code={HttpUtility.UrlEncode( code )}" );
        callback.StatusCode.ShouldBe( HttpStatusCode.OK );
        (await callback.Content.ReadAsStringAsync()).ShouldContain( "\"success\":true" );
        _idp.RedeemCount.ShouldBeGreaterThan( 0, "The Backend redeemed the code." );
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
    /// (its hello is refused), the Basic login (the private page in the back-office layout), the "Sign-in methods" tab of
    /// the profile (a named passkey registered and renamed), the Architecture page (a command dispatched to the
    /// AuthCenter, a refused call of its console), the user info box, a reload, the end of the Critical level (a new login
    /// from the passkey registration button), the sign out, the passkey login and the deletion of the passkey. Ignored when
    /// the application is not built (<c>yarn build</c>), or without Node or a browser.
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
        foreach( var a in new[] { "ui-smoke/ui-smoke.mjs", "--url", FrontUrl, "--oidc", "Test", "--critical-wait", "30", "--out", screenshots.Path } )
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

    // The names of the front and of its AuthCenter, from the topology.
    static async Task<(string Front, int FrontId, string AuthCenter, JsonElement Topology)> TopologyAsync( Browser b )
    {
        var t = Value( await b.SendAsync( "/.cris", """["CK.Sample.Front.IGetTopologyQCommand",{"actorId":""" + b.ActorId + "}]" ) );
        var center = t.GetProperty( "remotes" ).EnumerateArray().Select( Value ).Single( r => r.GetProperty( "isAuthCenter" ).GetBoolean() );
        return (t.GetProperty( "localPartyFullName" ).GetString()!, t.GetProperty( "localPartyActorId" ).GetInt32(), center.GetProperty( "fullName" ).GetString()!, t);
    }

    static async Task<JsonElement> WhoAmIAsync( Browser b, string target )
        => Value( await b.SendAsync( "/.cris", $$"""["CK.Sample.IWhoAmIQCommand",{"actorId":{{b.ActorId}},"targetPartyFullName":"{{target}}"}]""" ) );

    static async Task<JsonElement> AskLaterAsync( Browser b, string target )
        => Value( await b.SendAsync( "/.cris", $$"""["CK.Sample.Front.IAskLaterQCommand",{"actorId":{{b.ActorId}},"targetPartyFullName":"{{target}}"}]""" ) );

    [Test]
    public async Task the_same_command_executes_here_and_there_with_the_proved_identity_Async()
    {
        using var b = new Browser();
        await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        var (front, frontId, authCenter, _) = await TopologyAsync( b );
        frontId.ShouldBeGreaterThan( 0, "The front got its PartyActorId from its AuthCenter." );

        // Here: a direct request, the front is its own caller party.
        var here = await WhoAmIAsync( b, front );
        here.GetProperty( "partyFullName" ).GetString().ShouldBe( front );
        here.GetProperty( "actorName" ).GetString().ShouldBe( "Albert" );
        here.GetProperty( "level" ).GetString().ShouldBe( "Critical" );
        here.GetProperty( "callerPartyId" ).GetInt32().ShouldBe( frontId );

        // There: dispatched to the Backend with the user's proof: the level is the proved one, the front is the caller.
        var there = await WhoAmIAsync( b, authCenter );
        there.GetProperty( "partyFullName" ).GetString().ShouldBe( authCenter );
        there.GetProperty( "actorName" ).GetString().ShouldBe( "Albert" );
        there.GetProperty( "level" ).GetString().ShouldBe( "Critical" );
        there.GetProperty( "callerPartyId" ).GetInt32().ShouldBe( frontId );
    }

    [Test]
    public async Task asked_later_the_front_only_vouches_and_the_Backend_caps_the_level_Async()
    {
        using var b = new Browser();
        await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        var (front, frontId, authCenter, _) = await TopologyAsync( b );

        // In the front's background, at the front: the same party, the identity is kept.
        (await AskLaterAsync( b, front )).GetProperty( "level" ).GetString().ShouldBe( "Critical" );
        // In the front's background, at the AuthCenter: no proof, the front vouches: Normal at most.
        var later = await AskLaterAsync( b, authCenter );
        later.GetProperty( "partyFullName" ).GetString().ShouldBe( authCenter );
        later.GetProperty( "actorName" ).GetString().ShouldBe( "Albert" );
        later.GetProperty( "level" ).GetString().ShouldBe( "Normal" );
        later.GetProperty( "callerPartyId" ).GetInt32().ShouldBe( frontId );
    }

    [Test]
    public async Task a_Critical_command_deferred_by_the_front_is_refused_by_the_Backend_Async()
    {
        using var b = new Browser();
        await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        var albertId = b.ActorId;
        // The front submits a password change for Albert to its background executor: no proof, the Backend refuses.
        var pocos = _front.Services.GetRequiredService<PocoDirectory>();
        var command = pocos.Create<CK.IO.User.UserPassword.ISetPasswordCommand>( c =>
        {
            c.ActorId = albertId;
            c.UserId = albertId;
            c.Password = "deferred";
        } );
        var executed = await _front.Services.GetRequiredService<CrisBackgroundExecutorService>()
                                            .Submit( TestHelper.Monitor, command, null ).ExecutedCommand.WaitAsync( TimeSpan.FromSeconds( 30 ) );
        executed.Result.ShouldBeAssignableTo<ICrisResultError>()!.Errors.Select( e => e.ResName ).ShouldContain( "Authentication.CallerPartyRefused" );
        // The password is unchanged.
        using var again = new Browser();
        await again.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        again.Token.ShouldNotBeNull();
    }

    [Test]
    public async Task the_topology_shows_the_parties_their_ids_and_the_routes_Async()
    {
        using var b = new Browser();
        await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        var (_, frontId, _, topology) = await TopologyAsync( b );
        var center = topology.GetProperty( "remotes" ).EnumerateArray().Select( Value ).Single( r => r.GetProperty( "isAuthCenter" ).GetBoolean() );
        center.GetProperty( "partyActorId" ).GetInt32().ShouldBeGreaterThan( 0 );
        center.GetProperty( "partyActorId" ).GetInt32().ShouldNotBe( frontId );
        center.GetProperty( "connection" ).GetString().ShouldBe( "Connected" );
        var accepted = center.GetProperty( "acceptedCommands" ).EnumerateArray().Select( c => c.GetString() ).ToList();
        // The Backend accepts the party command, the self-service, and its internal operations (the front is inside the System).
        accepted.ShouldContain( "CK.Sample.IWhoAmIQCommand" );
        accepted.ShouldContain( "CK.IO.User.UserPasskey.IGetPasskeysQCommand" );
        accepted.ShouldContain( "CK.IO.Authentication.IOpenSessionOperationCommand" );
    }

    [Test]
    public async Task the_protected_calls_of_the_console_are_refused_Async()
    {
        using var b = new Browser();
        // An [Internal] operation on "/.cris/net" (all the exchangeable types): refused before its validation.
        using var m = new HttpRequestMessage( HttpMethod.Post, "/.cris/net" )
        {
            Content = new StringContent( """["CK.IO.Authentication.IOpenSessionOperationCommand",{"scheme":"Basic","nextRefreshSecretHash":"x","nextBindingSecretHash":"y"}]""", Encoding.UTF8, "application/json" )
        };
        m.Headers.Add( "Origin", FrontUrl );
        using var http = new HttpClient { BaseAddress = new Uri( FrontUrl ) };
        using var r = await http.SendAsync( m );
        (await r.Content.ReadAsStringAsync()).ShouldContain( "is not accepted from HTTP" );
        // A login on "/.cris" must be sent to "/.cris/auth".
        Text( await b.SendAsync( "/.cris", """["CK.IO.Authentication.Weak.IWeakLoginCommand",{"weakActorName":"Operator"}]""" ) ).ShouldContain( "/.cris/auth" );
    }
}
