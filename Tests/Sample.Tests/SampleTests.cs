using CK.AppIdentity;
using CK.AppIdentity.KeyManagement;
using CK.AppIdentity.TransportLayer;
using CK.Core;
using CK.Cris;
using CK.Sample.Admin;
using CK.Sample.Auth3;
using CK.Sample.Front;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using static CK.Monitoring.MultiLogReader;
using static CK.Testing.MonitorTestHelper;

namespace CK.Sample.Tests;

/// <summary>
/// The sample, end to end: its three hosts run in this process (with their own ports and an OpenID Connect scheme on the
/// <see cref="FakeIdentityProvider"/>), and a "browser" (an HttpClient that keeps the cookies) talks to the front (or to
/// the Admin). The fronts relay the session commands to Auth3 over the AppIdentity channel.
/// <para>
/// This crosses CK-AppIdentity-Transport (Auth3, the relay) and CK-Cris-TypeScript (the HTTP front) on a real
/// database: their own tests use fakes on the other side.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class SampleTests
{
    // "localhost", not 127.0.0.1: the passkeys are bound to a domain name (the RP ID), never to an IP address.
    const string FrontUrl = "http://localhost:5310";
    const string Auth3Address = "tcp:127.0.0.1:37310";
    const string Callback = FrontUrl + "/.auth/oidc/callback";
    const string AdminUrl = "http://localhost:5312";

    FakeIdentityProvider _idp = null!;
    IHost _auth3 = null!;
    IHost _front = null!;
    IHost _admin = null!;

    [OneTimeSetUp]
    public async Task StartTheHostsAsync()
    {
        // A fresh AppIdentity store for each run (the identity keys, the pinned remotes, the sessions), not the one of the
        // manual runs of the sample ("#Dev/Store"): setting the DefaultStoreRootPath disables the dev store.
        var store = TestHelper.TestProjectFolder.AppendPart( "TestStore" );
        if( Directory.Exists( store ) ) Directory.Delete( store, recursive: true );
        ApplicationIdentityServiceConfiguration.DefaultStoreRootPath = store;
        _idp = new FakeIdentityProvider();
        _auth3 = BuildAuth3();
        await _auth3.StartAsync();
        _front = CKomposableAppHost.Build( [], (monitor, b, map) => b.Configuration.AddInMemoryCollection( new Dictionary<string, string?>
        {
            // The web application of the "Main" endpoint of the front's party ("HttpEndpoint" feature).
            ["CK-AppIdentity:Local:HttpEndpoint:Urls"] = FrontUrl,
            ["CK-AppIdentity:Parties:0:Address"] = Auth3Address,
            // The UI smoke test runs on the built client applications (never their dev server).
            ["Clients:UseDist"] = "true"
        } ), TestHelper.SolutionFolder.Combine( "Front/Auth3.Front.Host" ), Assembly.Load( "Auth3.Front.Host" ) );
        await _front.StartAsync();
        _admin = CKomposableAppHost.Build( [], ( monitor, b, map ) => b.Configuration.AddInMemoryCollection( new Dictionary<string, string?>
        {
            ["CK-AppIdentity:Local:HttpEndpoint:Urls"] = AdminUrl,
            ["CK-AppIdentity:Parties:0:Address"] = Auth3Address,
            ["Clients:UseDist"] = "true"
        } ), TestHelper.SolutionFolder.Combine( "Admin/Auth3.Admin.Host" ), Assembly.Load( "Auth3.Admin.Host" ) );
        await _admin.StartAsync();
        await WaitForTheFrontsAsync();
    }

    // The Front and the Admin announce their public URL once their endpoint is started: Auth3 derives the
    // passkey origins, the OIDC callbacks and the links of the mails from them.
    async Task WaitForTheFrontsAsync()
    {
        var auth3 = _auth3.Services.GetRequiredService<ApplicationIdentityService>();
        // The Workers (started by the enlistment tests only) have no web endpoint.
        var fronts = auth3.Remotes.Where( r => !r.PartyName.StartsWith( "$Worker" ) ).ToList();
        for( int i = 0; i < 300 && fronts.Any( r => r.Description.PublicUrl == null ); ++i ) await Task.Delay( 100 );
        fronts.Select( r => r.Description.GetClientApplicationUrl() ).ShouldBe( [FrontUrl, AdminUrl], ignoreOrder: true );
    }

    IHost BuildAuth3()
    {
        return CKomposableAppHost.Build( [], (monitor, b, map) => b.Configuration.AddInMemoryCollection( new Dictionary<string, string?>
        {
            ["CK-AppIdentity:Defaults:Transport:ListeningAddress:tcp"] = Auth3Address["tcp:".Length..],
            ["CK-AppIdentity:Local:AuthCenter:Oidc:Schemes:Test:Authority"] = _idp.BaseUrl,
            ["CK-AppIdentity:Local:AuthCenter:Oidc:Schemes:Test:ClientId"] = FakeIdentityProvider.ClientId,
            ["CK-AppIdentity:Local:AuthCenter:Oidc:Schemes:Test:ClientSecret"] = FakeIdentityProvider.ClientSecret,
            // An unknown identity of the "Test" scheme creates its user (SampleAccountCreation).
            ["CK-AppIdentity:Local:AuthCenter:Oidc:Schemes:Test:AllowAccountCreation"] = "true",
            // Albert is bound to the "albert-sub" identity of the "Test" scheme.
            ["Demo:OidcScheme"] = "Test",
            ["Demo:OidcSub"] = "albert-sub",
            // The passkey origins and the OIDC callbacks are the ones of the Front and the Admin: the public URL they announce
            // (the one of their endpoint). The RP ID "localhost" is the configured one.
            // A short Critical level for the Basic logins: the UI smoke test sees its end (--critical-wait).
            ["CK-AppIdentity:Local:AuthCenter:CriticalLifetimes:Basic"] = "00:00:15"
        } ), TestHelper.SolutionFolder.Combine( "Auth3/Auth3.Auth3.Host" ), Assembly.Load( "Auth3.Auth3.Host" ) );
    }

    [OneTimeTearDown]
    public async Task StopTheHostsAsync()
    {
        if( _admin != null )
        {
            await _admin.StopAsync();
            _admin.Dispose();
        }
        if( _front != null )
        {
            await _front.StopAsync();
            _front.Dispose();
        }
        _auth3?.Dispose();
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

        // Posts a command (["Type",{...}] in the type set of the "App" client applications) and returns the "result" of the answer.
        public async Task<JsonElement> SendAsync( string path, string command )
        {
            using var m = new HttpRequestMessage( HttpMethod.Post, path + "?TypeFilterName=TypeScriptApp" ) { Content = new StringContent( command, Encoding.UTF8, "application/json" ) };
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
    public void the_connection_string_of_Auth3_is_burnt_by_the_build()
    {
        // The central ckonf.json locks it to the Compile level ("$LOCK" of the SqlServer aspect): it is not in the host's
        // appsettings.json, it is in the generated code (the running Auth3 uses its database: the other tests).
        var connectionString = "SqlServer:DefaultDatabaseConnectionString";
        File.ReadAllText( TestHelper.SolutionFolder.Combine( "Auth3/Auth3.Auth3.Host/appsettings.json" ) ).ShouldNotContain( "DefaultDatabaseConnectionString" );
        // A configured value is ignored (with a warning): the build wins.
        using var host = CKomposableAppHost.Build( [], ( monitor, b, map ) => b.Configuration.AddInMemoryCollection( new Dictionary<string, string?>
        {
            [connectionString] = "Server=nowhere;Database=Other"
        } ), TestHelper.SolutionFolder.Combine( "Auth3/Auth3.Auth3.Host" ), Assembly.Load( "Auth3.Auth3.Host" ) );
        host.Services.GetRequiredService<IConfiguration>()[connectionString].ShouldBe( "Server=.;Database=CKSampleAuth3;Integrated Security=True;TrustServerCertificate=true" );
    }

    [Test]
    public async Task the_ParallelRunnerCount_of_Auth3_is_changed_from_the_Admin_and_kept_Async()
    {
        const string path = "CrisExecutionHost:ParallelRunnerCount";
        using var browser = new Browser( AdminUrl );
        await browser.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        string auth3Name = _auth3.Services.GetRequiredService<ApplicationIdentityService>().FullName;
        var target = JsonEncodedText.Encode( auth3Name );
        // The configured value (the central ckonf.json: 2). The runners adjust in the background: the count follows.
        (await WaitForAsync( () => _auth3.Services.GetRequiredService<CrisExecutionHost>().ParallelRunnerCount == 2 ? "2" : null ))
            .ShouldNotBeNull( "Auth3 starts with the configured value (a previous run left the recorded value at 2)." );

        // The Admin lists the run properties of Auth3...
        var properties = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.DynamicConfiguration.IGetRunPropertiesQCommand",{"actorId":{{browser.ActorId}},"targetPartyFullName":"{{target}}"}]""" ) );
        properties.ValueKind.ShouldBe( JsonValueKind.Array, properties.ToString() );
        var p = properties.EnumerateArray().Select( Value ).Single( x => x.GetProperty( "path" ).GetString() == path );
        p.GetProperty( "value" ).GetString().ShouldBe( "2" );
        // ...and changes one: the current value is the optimistic concurrency.
        var changed = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.DynamicConfiguration.IConfigurePropertyCommand",{"actorId":{{browser.ActorId}},"targetPartyFullName":"{{target}}","propertyPath":"{{path}}","value":"4","currentValue":"2"}]""" ) );
        IsSuccess( changed ).ShouldBeTrue( changed.ToString() );
        (await WaitForAsync( () => _auth3.Services.GetRequiredService<CrisExecutionHost>().ParallelRunnerCount == 4 ? "4" : null )).ShouldNotBeNull();
        var conflict = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.DynamicConfiguration.IConfigurePropertyCommand",{"actorId":{{browser.ActorId}},"targetPartyFullName":"{{target}}","propertyPath":"{{path}}","value":"5","currentValue":"2"}]""" ) );
        IsSuccess( conflict ).ShouldBeFalse( conflict.ToString() );
        Value( conflict ).GetProperty( "userMessages" )[0][1].GetString().ShouldContain( "is '4', not '2'" );

        // Kept when Auth3 restarts (its configured value didn't change).
        await _auth3.StopAsync();
        _auth3.Dispose();
        _auth3 = BuildAuth3();
        await _auth3.StartAsync();
        (await WaitForAsync( () => _auth3.Services.GetRequiredService<CrisExecutionHost>().ParallelRunnerCount == 4 ? "4" : null ))
            .ShouldNotBeNull( "The change is kept in the store of Auth3." );

        // Back to 2 (the next runs start with it): the Admin reconnects to the restarted Auth3 first.
        (await WaitForAsync( () =>
        {
            var r = Value( browser.SendAsync( "/.cris", $$"""["CK.IO.DynamicConfiguration.IConfigurePropertyCommand",{"actorId":{{browser.ActorId}},"targetPartyFullName":"{{target}}","propertyPath":"{{path}}","value":"2","currentValue":"4"}]""" ).Result );
            return IsSuccess( r ) ? "reset" : null;
        } )).ShouldNotBeNull( "The Admin reaches the restarted Auth3." );
        (await WaitForAsync( () => _auth3.Services.GetRequiredService<CrisExecutionHost>().ParallelRunnerCount == 2 ? "2" : null )).ShouldNotBeNull();
    }

    [Test]
    public async Task the_user_manages_its_login_methods_at_Auth3_through_the_front_Async()
    {
        // The front has no database: these commands are posted to "/.cris" and dispatched to Auth3 (that handles
        // them) with the browser's proof (its token and its binding): Auth3 verifies it.
        using var anonymous = new Browser();
        Value( await anonymous.SendAsync( "/.cris", """["CK.IO.User.UserPasskey.IGetPasskeysQCommand",{"actorId":1}]""" ) ).ValueKind.ShouldNotBe( JsonValueKind.Array, "Refused: not a list." );

        using var b = new Browser();
        await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        var albertId = b.ActorId;
        // A passkey of Albert, stored directly in Auth3 (a unique name: the database outlives the test runs).
        var keyName = $"Sample key {Guid.NewGuid():N}";
        var store = _auth3.Services.GetRequiredService<IPasskeyStore>();
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
    public async Task the_profile_page_reads_the_profile_and_the_sign_in_methods_at_Auth3_Async()
    {
        using var b = new Browser();
        await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        var albertId = b.ActorId;

        // The profile (the application's leaf, read by Auth3): Albert administers the demo workspace.
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
    public async Task the_Admin_front_administers_the_users_at_Auth3_Async()
    {
        // The second remote of the AuthCenter: its sessions are its own (its origin, its cookies).
        using var admin = new Browser( AdminUrl );
        await admin.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        admin.Token.ShouldNotBeNull();
        var albertId = admin.ActorId;
        var profile = Value( await admin.SendAsync( "/.cris", $$"""["CK.IO.Actor.IGetUserProfileQCommand",{"actorId":{{albertId}},"userId":{{albertId}}}]""" ) );
        var workspaceId = profile.GetProperty( "preferredWorkspaceId" ).GetInt32();

        // Albert administers the demo workspace: Auth3 lists its users (the application's query, with the
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
    public async Task a_basic_login_through_the_front_opens_a_session_on_Auth3_Async()
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

        // Logout: the session ends on Auth3, the cookies are cleared.
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
    public async Task an_oidc_login_through_the_front_and_Auth3_Async()
    {
        using var b = new Browser();
        // The schemes come from Auth3.
        Text( await b.SendAsync( "/.cris", """["CK.IO.Authentication.IGetLoginSchemesQCommand",{}]""" ) ).ShouldContain( "Oidc.Test" );

        // Start: the front relays it, Auth3 returns the provider's URL; the front sets the remote login cookie.
        var start = await b.SessionAsync( """["CK.IO.Authentication.IStartLoginCommand",{"scheme":"Oidc.Test","inlineMode":false,"rememberMe":false}]""" );
        var url = start[1].GetProperty( "url" ).GetString().ShouldNotBeNull();
        b.Cookies.GetAllCookies().ShouldContain( c => c.Name == "ck-auth-login" && c.Path == "/.auth/oidc/callback" );

        // The user logs in at the provider, that redirects the browser to the front's callback.
        var (redirect, code, state) = _idp.Authorize( url, "albert-sub" );
        redirect.ShouldBe( Callback );
        using var callback = await b.GetAsync( $"/.auth/oidc/callback?state={HttpUtility.UrlEncode( state )}&code={HttpUtility.UrlEncode( code )}" );
        callback.StatusCode.ShouldBe( HttpStatusCode.OK );
        (await callback.Content.ReadAsStringAsync()).ShouldContain( "\"success\":true" );
        _idp.RedeemCount.ShouldBeGreaterThan( 0, "Auth3 redeemed the code." );
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
    /// The UI smoke test (Auth3.Front.Host/Clients/App/ui-smoke/ui-smoke.mjs) on the built Angular application, in a headless
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
        var app = TestHelper.SolutionFolder.Combine( "Front/Auth3.Front.Host/Clients/App" );
        var index = app.Combine( "dist/App/browser/index.html" );
        if( !File.Exists( index ) )
        {
            Assert.Ignore( "The Angular application is not built (yarn build in Front/Auth3.Front.Host/Clients/App)." );
        }
        // The test serves the dist as it is: it may be older than the ck-gen (rewritten at each run, its dates can't tell).
        var built = $"The Angular application was built at {File.GetLastWriteTime( index ):yyyy-MM-dd HH:mm:ss}: an older build than the last front change fails here (yarn build in Front/Auth3.Front.Host/Clients/App).";
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
            var text = built + Environment.NewLine + await output + await error;
            TestHelper.Monitor.Info( $"UI smoke test (screenshots in '{screenshots}'):{Environment.NewLine}{text}" );
            if( node.ExitCode == 2 ) Assert.Ignore( "No browser for the UI smoke test (set CK_BROWSER)." );
            node.ExitCode.ShouldBe( 0, text );
        }
    }

    // The names of the front and of its AuthCenter, from the topology.
    static async Task<(string Front, string AuthCenter, JsonElement Topology)> TopologyAsync( Browser b )
    {
        var t = Value( await b.SendAsync( "/.cris", """["CK.Sample.Front.IGetTopologyQCommand",{"actorId":""" + b.ActorId + "}]" ) );
        var center = t.GetProperty( "remotes" ).EnumerateArray().Select( Value ).Single( r => r.GetProperty( "isAuthCenter" ).GetBoolean() );
        return (t.GetProperty( "localPartyFullName" ).GetString()!, center.GetProperty( "fullName" ).GetString()!, t);
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
        var (front, authCenter, _) = await TopologyAsync( b );

        // Here: a direct request, the front is its own caller party.
        var here = await WhoAmIAsync( b, front );
        here.GetProperty( "partyFullName" ).GetString().ShouldBe( front );
        here.GetProperty( "actorName" ).GetString().ShouldBe( "Albert" );
        here.GetProperty( "level" ).GetString().ShouldBe( "Critical" );
        here.GetProperty( "callerPartyFullName" ).GetString().ShouldBe( front );

        // There: dispatched to Auth3 with the user's proof: the level is the proved one, the front is the caller.
        var there = await WhoAmIAsync( b, authCenter );
        there.GetProperty( "partyFullName" ).GetString().ShouldBe( authCenter );
        there.GetProperty( "actorName" ).GetString().ShouldBe( "Albert" );
        there.GetProperty( "level" ).GetString().ShouldBe( "Critical" );
        there.GetProperty( "callerPartyFullName" ).GetString().ShouldBe( front );
    }

    [Test]
    public async Task asked_later_the_front_only_vouches_and_Auth3_caps_the_level_Async()
    {
        using var b = new Browser();
        await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        var (front, authCenter, _) = await TopologyAsync( b );

        // In the front's background, at the front: the same party, the identity is kept.
        (await AskLaterAsync( b, front )).GetProperty( "level" ).GetString().ShouldBe( "Critical" );
        // In the front's background, at the AuthCenter: no proof, the front vouches: Normal at most.
        var later = await AskLaterAsync( b, authCenter );
        later.GetProperty( "partyFullName" ).GetString().ShouldBe( authCenter );
        later.GetProperty( "actorName" ).GetString().ShouldBe( "Albert" );
        later.GetProperty( "level" ).GetString().ShouldBe( "Normal" );
        later.GetProperty( "callerPartyFullName" ).GetString().ShouldBe( front );
    }

    [Test]
    public async Task a_Critical_command_deferred_by_the_front_is_refused_by_Auth3_Async()
    {
        using var b = new Browser();
        await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        var albertId = b.ActorId;
        // The front submits a password change for Albert to its background executor: no proof, Auth3 refuses.
        var pocos = _front.Services.GetRequiredService<PocoDirectory>();
        var command = pocos.Create<CK.IO.User.UserPassword.ISetPasswordCommand>( c =>
        {
            c.ActorId = albertId;
            c.UserId = albertId;
            c.Password = "deferred";
        } );
        // In "#Dev", the front validates the command again before executing it (its background has no user: the actor
        // mismatches). This is a development check: it is skipped, the refusal under test is Auth3's.
        var executed = await _front.Services.GetRequiredService<CrisBackgroundExecutorService>()
                                            .Submit( TestHelper.Monitor, command, null, incomingValidationCheck: false )
                                            .ExecutedCommand.WaitAsync( TimeSpan.FromSeconds( 30 ) );
        executed.Result.ShouldBeAssignableTo<ICrisResultError>()!.Errors.Select( e => e.ResName ).ShouldContain( "Authentication.CallerPartyRefused" );
        // The password is unchanged.
        using var again = new Browser();
        await again.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        again.Token.ShouldNotBeNull();
    }

    [Test]
    public async Task the_topology_shows_the_parties_and_the_routes_Async()
    {
        using var b = new Browser();
        await b.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        var (front, _, topology) = await TopologyAsync( b );
        var center = topology.GetProperty( "remotes" ).EnumerateArray().Select( Value ).Single( r => r.GetProperty( "isAuthCenter" ).GetBoolean() );
        center.GetProperty( "fullName" ).GetString().ShouldNotBe( front );
        center.GetProperty( "connection" ).GetString().ShouldBe( "Connected" );
        var accepted = center.GetProperty( "acceptedCommands" ).EnumerateArray().Select( c => c.GetString() ).ToList();
        // Auth3 accepts the party command, the self-service, and its internal operations (the front is inside the System).
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
            Content = new StringContent( """["CK.IO.Authentication.IOpenSessionOperationCommand",{"scheme":"Basic","nextRefreshSecretHash":"eA==","nextBindingSecretHash":"eQ=="}]""", Encoding.UTF8, "application/json" )
        };
        m.Headers.Add( "Origin", FrontUrl );
        using var http = new HttpClient { BaseAddress = new Uri( FrontUrl ) };
        using var r = await http.SendAsync( m );
        (await r.Content.ReadAsStringAsync()).ShouldContain( "is not accepted from HTTP" );
        // A login on "/.cris" must be sent to "/.cris/auth".
        Text( await b.SendAsync( "/.cris", """["CK.IO.Authentication.Weak.IWeakLoginCommand",{"weakActorName":"Operator"}]""" ) ).ShouldContain( "/.cris/auth" );
    }

    static async Task<T?> WaitForAsync<T>( Func<T?> get, int seconds = 60 ) where T : class
    {
        for( int i = 0; i < seconds * 10; ++i )
        {
            var v = get();
            if( v != null ) return v;
            await Task.Delay( 100 );
        }
        return null;
    }

    [Test]
    public async Task an_operator_enlists_the_Worker_on_the_approval_page_of_the_Admin_Async()
    {
        // Auth3 never trusts the Worker's key automatically ("AutoTrustKey": "Never"): the Worker waits for an approval.
        // The Worker is a fleet of two: this is its second instance ("$Worker-2").
        using var worker = CKomposableAppHost.Build( ["--Instance=2"], ( monitor, b, map ) => b.Configuration.AddInMemoryCollection( new Dictionary<string, string?>
        {
            ["CK-AppIdentity:Parties:0:Address"] = Auth3Address
        } ), TestHelper.SolutionFolder.Combine( "Worker/Auth3.Worker.Host" ), Assembly.Load( "Auth3.Worker.Host" ) );
        await worker.StartAsync();
        try
        {
            var workerParty = worker.Services.GetRequiredService<ApplicationIdentityService>();
            workerParty.PartyName.ShouldBe( "$Worker-2" );
            var auth3 = _auth3.Services.GetRequiredService<ApplicationIdentityService>();
            var admin = _admin.Services.GetRequiredService<ApplicationIdentityService>();

            // The Worker is told where to enlist (the approval page of the System, on the Admin, for itself) and shows the
            // code that the operator reads.
            var issue = await WaitForAsync( () => workerParty.GetRequiredFeature<TransportManagerFeature>().GetPeeringIssues()
                                                             .FirstOrDefault( i => i.EnlistUrl != null && i.ApprovalCode != null ) );
            issue.ShouldNotBeNull( "The Worker learns its enlistment URL and its approval code." );
            issue.Kind.ShouldBe( PeeringIssueKind.RequiresRemoteApproval );
            issue.EnlistUrl.ShouldBe( $"{AdminUrl}/app-identity/enlist?party={Uri.EscapeDataString( workerParty.FullName )}" );
            var code = issue.ApprovalCode!;

            // Auth3 holds the pending approval: the approval page asks it (Auth3 is the hub of the System).
            var item = await WaitForAsync( () => TransportFeatureDriver.GetHeldPendingApprovals( auth3 ).FirstOrDefault( a => a.PartyFullName == workerParty.FullName.Path ) );
            item.ShouldNotBeNull( "Auth3 holds the pending approval." );

            // The operator signs in on the Admin (a password: the Critical level) and enters the code.
            using var browser = new Browser( AdminUrl );
            await browser.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
            // The approval page lists the pending approvals (the operator chooses the Worker there rather than following a link).
            async Task<List<(string Party, string Holder)>> PendingAsync()
            {
                var r = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Enlistment.IGetPendingApprovalsQCommand",{"actorId":{{browser.ActorId}}}]""" ) );
                r.ValueKind.ShouldBe( JsonValueKind.Array, r.ToString() );
                return r.EnumerateArray().Select( Value ).Select( p => (p.GetProperty( "partyFullName" ).GetString()!, p.GetProperty( "holderFullName" ).GetString()!) ).ToList();
            }
            string auth3FullName = auth3.FullName;
            (await PendingAsync()).ShouldContain( (workerParty.FullName.Path, auth3FullName) );

            async Task<(string Holder, int Status)> ApproveAsync( string c )
            {
                var r = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Enlistment.IApprovePartyCommand",{"actorId":{{browser.ActorId}},"partyFullName":"{{JsonEncodedText.Encode( workerParty.FullName )}}","codes":["{{c}}"]}]""" ) );
                r.ValueKind.ShouldBe( JsonValueKind.Array, r.ToString() );
                var o = Value( r.EnumerateArray().Single() );
                return (o.GetProperty( "holderFullName" ).GetString()!, o.GetProperty( "status" ).GetInt32());
            }
            var wrong = code == "1234-5678" ? "8765-4321" : "1234-5678";
            string auth3Name = auth3.FullName;
            var refused = await ApproveAsync( wrong );
            refused.Holder.ShouldBe( auth3Name );
            refused.Status.ShouldBe( (int)CK.IO.AppIdentity.Enlistment.PartyApprovalStatus.CodeMismatch );
            var approved = await ApproveAsync( code );
            approved.Holder.ShouldBe( auth3Name );
            approved.Status.ShouldBe( (int)CK.IO.AppIdentity.Enlistment.PartyApprovalStatus.Approved );

            // Auth3 trusts the Worker's key: the Worker connects, and the pending approval disappears.
            (await WaitForAsync( () => workerParty.Remotes.Single().GetRequiredFeature<TransportFeature>().ConnectionAvailability == ConnectionAvailability.Connected ? "connected" : null ))
                .ShouldNotBeNull( "The approved Worker connects to Auth3." );
            (await WaitForAsync( () => TransportFeatureDriver.GetHeldPendingApprovals( auth3 ).Count == 0 ? "gone" : null )).ShouldNotBeNull();
            (await PendingAsync()).ShouldBeEmpty( "The Worker is enlisted: nothing waits anymore." );
        }
        finally
        {
            await worker.StopAsync();
        }
    }

    [Test]
    public async Task a_dynamic_Worker_is_enlisted_kept_by_Auth3_at_restart_and_removed_Async()
    {
        // The Worker fleet is Opened ("MaxInstanceCount": 4): "$Worker-3" is not configured, Auth3 accepts it as a candidate.
        using var worker = CKomposableAppHost.Build( ["--Instance=3"], ( monitor, b, map ) => b.Configuration.AddInMemoryCollection( new Dictionary<string, string?>
        {
            ["CK-AppIdentity:Parties:0:Address"] = Auth3Address
        } ), TestHelper.SolutionFolder.Combine( "Worker/Auth3.Worker.Host" ), Assembly.Load( "Auth3.Worker.Host" ) );
        await worker.StartAsync();
        try
        {
            var workerParty = worker.Services.GetRequiredService<ApplicationIdentityService>();
            workerParty.PartyName.ShouldBe( "$Worker-3" );
            string worker3 = workerParty.FullName;
            var issue = await WaitForAsync( () => workerParty.GetRequiredFeature<TransportManagerFeature>().GetPeeringIssues()
                                                             .FirstOrDefault( i => i.EnlistUrl != null && i.ApprovalCode != null ) );
            issue.ShouldNotBeNull( "The candidate learns its enlistment URL and its approval code." );
            var auth3 = _auth3.Services.GetRequiredService<ApplicationIdentityService>();
            var candidate = auth3.Remotes.Single( r => r.FullName.Path == worker3 );
            candidate.Configuration.IsDynamicInstance.ShouldBeTrue();
            string auth3Name = auth3.FullName;

            using var browser = new Browser( AdminUrl );
            await browser.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
            // The approval page says that it is a new instance of the template.
            JsonElement pending = default;
            (await WaitForAsync( () =>
            {
                var r = Value( browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Enlistment.IGetPendingApprovalsQCommand",{"actorId":{{browser.ActorId}}}]""" ).Result );
                pending = r.EnumerateArray().Select( Value ).FirstOrDefault( p => p.GetProperty( "partyFullName" ).GetString() == worker3 );
                return pending.ValueKind == JsonValueKind.Object ? "found" : null;
            } )).ShouldNotBeNull( "The candidate waits on the approval page." );
            pending.GetProperty( "templatePartyName" ).GetString().ShouldBe( "$Worker-{Name}" );
            var a = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Enlistment.IApprovePartyCommand",{"actorId":{{browser.ActorId}},"partyFullName":"{{JsonEncodedText.Encode( worker3 )}}","codes":["{{issue.ApprovalCode}}"]}]""" ) );
            Value( a.EnumerateArray().Single() ).GetProperty( "status" ).GetInt32().ShouldBe( (int)CK.IO.AppIdentity.Enlistment.PartyApprovalStatus.Approved );
            (await WaitForAsync( () => workerParty.Remotes.Single().GetRequiredFeature<TransportFeature>().ConnectionAvailability == ConnectionAvailability.Connected ? "connected" : null ))
                .ShouldNotBeNull( "The approved instance connects to Auth3." );

            // Auth3 restarts: the approved instance is still its remote (its identity is pinned in the store).
            await _auth3.StopAsync();
            _auth3.Dispose();
            _auth3 = BuildAuth3();
            await _auth3.StartAsync();
            auth3 = _auth3.Services.GetRequiredService<ApplicationIdentityService>();
            var w3 = auth3.Remotes.Single( r => r.FullName.Path == worker3 );
            w3.IsDynamic.ShouldBeTrue();
            (await WaitForAsync( () => w3.GetRequiredFeature<TransportFeature>().ConnectionAvailability == ConnectionAvailability.Connected ? "connected" : null ))
                .ShouldNotBeNull( "The instance connects to the restarted Auth3: no approval." );
            await WaitForTheFrontsAsync();

            // The approval page lists it, and removes it.
            JsonElement instance = default;
            (await WaitForAsync( () =>
            {
                var r = Value( browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Enlistment.IGetPartyInstancesQCommand",{"actorId":{{browser.ActorId}}}]""" ).Result );
                instance = r.ValueKind == JsonValueKind.Array
                            ? r.EnumerateArray().Select( Value ).FirstOrDefault( p => p.GetProperty( "partyFullName" ).GetString() == worker3 )
                            : default;
                return instance.ValueKind == JsonValueKind.Object ? "found" : null;
            } )).ShouldNotBeNull( "The approved instance reaches the approval page." );
            instance.GetProperty( "holderFullName" ).GetString().ShouldBe( auth3Name );
            instance.GetProperty( "templatePartyName" ).GetString().ShouldBe( "$Worker-{Name}" );
            var folder = w3.SharedFileStore.FolderPath;
            var removed = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Enlistment.IRemovePartyInstanceCommand",{"actorId":{{browser.ActorId}},"partyFullName":"{{JsonEncodedText.Encode( worker3 )}}"}]""" ) );
            removed.GetInt32().ShouldBe( 1, removed.ToString() );
            auth3.Remotes.ShouldNotContain( r => r.FullName.Path == worker3 && !r.IsDestroyed );
            Directory.EnumerateFiles( folder, IFileStore.TrustFilePattern ).ShouldBeEmpty( "Its pinned identity is forgotten." );
            Directory.Exists( folder.AppendPart( "-Local" ) ).ShouldBeTrue( "The Worker's own folder (the same store) is kept." );
        }
        finally
        {
            await worker.StopAsync();
        }
    }

    [Test]
    public async Task an_expected_Worker_starts_with_its_ticket_and_is_trusted_without_a_code_Async()
    {
        var auth3 = _auth3.Services.GetRequiredService<ApplicationIdentityService>();
        // "$Worker-4": the last dynamic instance of the Opened fleet ("MaxInstanceCount": 4), expected by the operator.
        string worker4 = auth3.FullName.Path.Replace( "$Auth3", "$Worker-4" );

        // The operator expects it on the approval page of the Admin: the ticket is opened there, registered on Auth3.
        using var browser = new Browser( AdminUrl );
        await browser.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        async Task<(string Id, string Ticket)> OpenAsync()
        {
            var t = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Enlistment.IOpenEnlistmentTicketCommand",{"actorId":{{browser.ActorId}},"partyFullName":"{{JsonEncodedText.Encode( worker4 )}}","lifetimeHours":1}]""" ) );
            t.ValueKind.ShouldBe( JsonValueKind.Object, t.ToString() );
            return (t.GetProperty( "id" ).GetString()!, t.GetProperty( "ticket" ).GetString()!);
        }
        async Task<List<JsonElement>> TicketsAsync()
        {
            var r = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Enlistment.IGetEnlistmentTicketsQCommand",{"actorId":{{browser.ActorId}}}]""" ) );
            r.ValueKind.ShouldBe( JsonValueKind.Array, r.ToString() );
            return r.EnumerateArray().Select( Value ).ToList();
        }
        // A ticket opened by mistake is revoked.
        var mistake = await OpenAsync();
        Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Enlistment.IRevokeEnlistmentTicketCommand",{"actorId":{{browser.ActorId}},"ticketId":"{{mistake.Id}}"}]""" ) )
            .GetBoolean().ShouldBeTrue();
        (await TicketsAsync()).ShouldNotContain( t => t.GetProperty( "id" ).GetString() == mistake.Id );

        var (id, ticket) = await OpenAsync();
        ticket.ShouldStartWith( "ckt1." );
        var open = (await TicketsAsync()).Single( t => t.GetProperty( "id" ).GetString() == id );
        open.GetProperty( "partyFullName" ).GetString().ShouldBe( worker4 );
        (!open.TryGetProperty( "used", out var notUsed ) || notUsed.ValueKind == JsonValueKind.Null).ShouldBeTrue( "The ticket is open." );

        // The Worker starts with its ticket: an optional secret (what the "#Production" deployment does with
        // { "$SECRET?": "Enlist:Worker-{Name}" }), given by the operator at its first start.
        using var worker = CKomposableAppHost.Build( ["--Instance=4"], ( monitor, b, map ) => b.Configuration.AddInMemoryCollection( new Dictionary<string, string?>
        {
            ["CK-AppIdentity:Parties:0:Address"] = Auth3Address,
            ["CK-AppIdentity:Local:KeyManagement:EnlistTicket:$SECRET?"] = "Enlist:Worker-{Name}",
            ["Secrets:Enlist:Worker-4"] = ticket
        } ), TestHelper.SolutionFolder.Combine( "Worker/Auth3.Worker.Host" ), Assembly.Load( "Auth3.Worker.Host" ) );
        await worker.StartAsync();
        try
        {
            var workerParty = worker.Services.GetRequiredService<ApplicationIdentityService>();
            workerParty.FullName.Path.ShouldBe( worker4 );
            // Auth3 trusts its key at once: no approval code, no pending approval.
            (await WaitForAsync( () => workerParty.Remotes.Single().GetRequiredFeature<TransportFeature>().ConnectionAvailability == ConnectionAvailability.Connected ? "connected" : null ))
                .ShouldNotBeNull( "The expected Worker connects to Auth3 with its ticket." );
            workerParty.GetRequiredFeature<TransportManagerFeature>().GetPeeringIssues().ShouldNotContain( i => i.ApprovalCode != null );
            var used = (await TicketsAsync()).Single( t => t.GetProperty( "id" ).GetString() == id );
            used.GetProperty( "used" ).ValueKind.ShouldBe( JsonValueKind.String, "The ticket is used." );
            used.GetProperty( "usedByFingerprint" ).GetString().ShouldNotBeNullOrEmpty();

            // It is an approved instance of the fleet: the operator removes it like the other ones.
            var removed = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Enlistment.IRemovePartyInstanceCommand",{"actorId":{{browser.ActorId}},"partyFullName":"{{JsonEncodedText.Encode( worker4 )}}"}]""" ) );
            removed.GetInt32().ShouldBe( 1, removed.ToString() );
        }
        finally
        {
            await worker.StopAsync();
        }
    }

    [Test]
    public async Task Worker2_anchors_the_AuthCenter_and_receives_an_authenticated_enlistment_URL_Async()
    {
        var auth3 = _auth3.Services.GetRequiredService<ApplicationIdentityService>();
        var admin = _admin.Services.GetRequiredService<ApplicationIdentityService>();
        string auth3Name = auth3.FullName;
        // Worker2 anchors Auth3 (its AuthCenter): "#Dev" in its appsettings.json, its checkpoint here (the dev store is
        // disabled by the tests).
        using var worker2 = BuildWorker2( auth3 );
        await worker2.StartAsync();
        try
        {
            var worker2Party = worker2.Services.GetRequiredService<ApplicationIdentityService>();

            // Worker2 trusts Auth3 from its anchor (no trust on first use), Auth3 doesn't trust Worker2. The
            // approval page reaches Worker2 in a statement of the AuthCenter (Auth3) that Worker2 verifies with its
            // anchor: the enlistment URL is authenticated.
            var issue = await WaitForAsync( () => worker2Party.GetRequiredFeature<TransportManagerFeature>().GetPeeringIssues()
                                                              .FirstOrDefault( i => i.EnlistUrl != null && i.ApprovalCode != null ) );
            issue.ShouldNotBeNull( "Worker2 learns its enlistment URL and its approval code." );
            issue.Kind.ShouldBe( PeeringIssueKind.RequiresRemoteApproval );
            issue.IsEnlistUrlAuthenticated.ShouldBeTrue( "The approval page is stated by the AuthCenter that Worker2 anchors." );
            issue.EnlistUrl.ShouldBe( $"{AdminUrl}/app-identity/enlist?party={Uri.EscapeDataString( worker2Party.FullName )}" );
            var code = issue.ApprovalCode!;
            (await WaitForAsync( () => TransportFeatureDriver.GetHeldPendingApprovals( auth3 ).FirstOrDefault( a => a.PartyFullName.Contains( "Worker2" ) ) ))
                .ShouldNotBeNull( "Auth3 holds the pending approval of Worker2." );

            // The operator approves Worker2 on the page: Auth3 pins it.
            using var browser = new Browser( AdminUrl );
            await browser.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
            var r = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Enlistment.IApprovePartyCommand",{"actorId":{{browser.ActorId}},"partyFullName":"{{JsonEncodedText.Encode( worker2Party.FullName )}}","codes":["{{code}}"]}]""" ) );
            r.ValueKind.ShouldBe( JsonValueKind.Array, r.ToString() );
            var o = Value( r.EnumerateArray().Single() );
            o.GetProperty( "holderFullName" ).GetString().ShouldBe( auth3Name );
            o.GetProperty( "status" ).GetInt32().ShouldBe( (int)CK.IO.AppIdentity.Enlistment.PartyApprovalStatus.Approved );
            (await WaitForAsync( () => worker2Party.Remotes.Single().GetRequiredFeature<TransportFeature>().ConnectionAvailability == ConnectionAvailability.Connected ? "connected" : null ))
                .ShouldNotBeNull( "The approved Worker2 connects to Auth3." );
        }
        finally
        {
            await worker2.StopAsync();
        }
    }

    // Worker2 anchors Auth3 (its AuthCenter): "#Dev" in its appsettings.json, its checkpoint here (the dev store is disabled by
    // the tests). Its "Parties" are its tenant template (0) and its link to Auth3 (1): the links are appended by the projection.
    IHost BuildWorker2( ApplicationIdentityService auth3 )
    {
        return CKomposableAppHost.Build( [], ( monitor, b, map ) => b.Configuration.AddInMemoryCollection( new Dictionary<string, string?>
        {
            ["CK-AppIdentity:Parties:1:Address"] = Auth3Address,
            [$"CK-AppIdentity:Local:KeyManagement:TrustAnchors:{auth3.FullName}"] = auth3.GetRequiredFeature<ILocalKeys>().Checkpoint.ToString()
        } ), TestHelper.SolutionFolder.Combine( "Worker2/Auth3.Worker2.Host" ), Assembly.Load( "Auth3.Worker2.Host" ) );
    }

    // Starts Worker2 and, when Auth3 doesn't trust it yet, approves it on the approval page (an operator signed in at the
    // Critical level). Returns once Worker2 is connected to Auth3.
    async Task<IHost> StartEnlistedWorker2Async( Browser browser )
    {
        var auth3 = _auth3.Services.GetRequiredService<ApplicationIdentityService>();
        var worker2 = BuildWorker2( auth3 );
        await worker2.StartAsync();
        var worker2Party = worker2.Services.GetRequiredService<ApplicationIdentityService>();
        var toAuth3 = worker2Party.Remotes.Single().GetRequiredFeature<TransportFeature>();
        var connected = await WaitForAsync( () => toAuth3.ConnectionAvailability == ConnectionAvailability.Connected
                                                    ? "connected"
                                                    : worker2Party.GetRequiredFeature<TransportManagerFeature>().GetPeeringIssues()
                                                                  .FirstOrDefault( i => i.ApprovalCode != null )?.ApprovalCode );
        connected.ShouldNotBeNull( "Worker2 connects to Auth3, or waits for its approval." );
        if( connected != "connected" )
        {
            var r = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Enlistment.IApprovePartyCommand",{"actorId":{{browser.ActorId}},"partyFullName":"{{JsonEncodedText.Encode( worker2Party.FullName )}}","codes":["{{connected}}"]}]""" ) );
            r.ValueKind.ShouldBe( JsonValueKind.Array, r.ToString() );
            (await WaitForAsync( () => toAuth3.ConnectionAvailability == ConnectionAvailability.Connected ? "connected" : null ))
                .ShouldNotBeNull( "The approved Worker2 connects to Auth3." );
        }
        return worker2;
    }

    [Test]
    public async Task a_tenant_of_Worker2_is_created_from_the_Admin_kept_moved_and_removed_Async()
    {
        using var browser = new Browser( AdminUrl );
        await browser.SessionAsync( """["CK.IO.Authentication.Basic.IBasicLoginCommand",{"userName":"Albert","password":"pass","rememberMe":false}]""" );
        var worker2 = await StartEnlistedWorker2Async( browser );
        try
        {
            var worker2Party = worker2.Services.GetRequiredService<ApplicationIdentityService>();
            string worker2Name = worker2Party.FullName;
            var template = worker2Party.TenantTemplates.ShouldHaveSingleItem();
            template.DomainPrefix.ShouldBe( "Sample/Auth3/Customers" );
            // A previous run may have left Acme: the test starts without it.
            if( worker2Party.TenantDomains.Any( t => t.Configuration.Instance?.Name == "Acme" ) || worker2Party.UnloadedTenants.Any( u => u.Name == "Acme" ) )
            {
                (await worker2Party.RemoveTenantInstanceAsync( TestHelper.Monitor, template.GetFullName( "Acme" ) )).ShouldBeTrue();
            }

            // The Admin (the tenants page) finds Worker2 through its hub: a remote of Auth3 that accepts the tenant commands.
            (await WaitForAsync( () =>
            {
                var hosts = Value( browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Tenants.IGetTenantHostsQCommand",{"actorId":{{browser.ActorId}}}]""" ).Result );
                return hosts.ValueKind == JsonValueKind.Array && hosts.EnumerateArray().Any( h => h.GetString() == worker2Name ) ? "found" : null;
            } )).ShouldNotBeNull( "Worker2 is a remote of Auth3, the hub of the Admin, and accepts the tenant commands." );

            // Created from the Admin: the command goes Admin > Auth3 > Worker2 (through the hub).
            var created = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Tenants.ICreateTenantInstanceCommand",{"actorId":{{browser.ActorId}},"targetPartyFullName":"{{JsonEncodedText.Encode( worker2Name )}}","templateName":"Sample/Auth3/Customers","name":"Acme"}]""" ) );
            created.ValueKind.ShouldBe( JsonValueKind.String, created.ToString() );
            var acmeName = created.GetString()!;
            acmeName.ShouldBe( "Sample/Auth3/Customers/Acme/$Acme/#Dev" );
            var acme = worker2Party.TenantDomains.Single( t => t.FullName == acmeName );
            acme.Configuration.Instance!.Index.ShouldBe( 1 );
            var key = acme.GetRequiredFeature<ILocalKeys>().CurrentIdentity.PublicKeyRawData.ToArray();
            var local = acme.LocalFileStore.FolderPath;

            // Worker2 restarts: Acme is still there, with the same key.
            await worker2.StopAsync();
            worker2.Dispose();
            worker2 = await StartEnlistedWorker2Async( browser );
            worker2Party = worker2.Services.GetRequiredService<ApplicationIdentityService>();
            acme = worker2Party.TenantDomains.Single( t => t.FullName == acmeName );
            acme.GetRequiredFeature<ILocalKeys>().CurrentIdentity.PublicKeyRawData.ToArray().ShouldBe( key );

            // Moved, then removed from the Admin.
            var moved = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Tenants.ISetTenantIndexCommand",{"actorId":{{browser.ActorId}},"targetPartyFullName":"{{JsonEncodedText.Encode( worker2Name )}}","tenantFullName":"{{JsonEncodedText.Encode( acmeName )}}","index":3}]""" ) );
            moved.ValueKind.ShouldBe( JsonValueKind.True, moved.ToString() );
            worker2Party.TenantDomains.Single( t => t.FullName == acmeName ).Configuration.Instance!.Index.ShouldBe( 3 );
            var removed = Value( await browser.SendAsync( "/.cris", $$"""["CK.IO.AppIdentity.Tenants.IRemoveTenantInstanceCommand",{"actorId":{{browser.ActorId}},"targetPartyFullName":"{{JsonEncodedText.Encode( worker2Name )}}","tenantFullName":"{{JsonEncodedText.Encode( acmeName )}}"}]""" ) );
            removed.ValueKind.ShouldBe( JsonValueKind.True, removed.ToString() );
            worker2Party.TenantDomains.ShouldNotContain( t => t.FullName == acmeName );
            File.Exists( local.AppendPart( "Instance.json" ) ).ShouldBeFalse( "Its record is in the trash bin." );
            Directory.EnumerateFiles( local.AppendPart( "CoreKeys" ) ).ShouldBeEmpty( "Its keys are destroyed." );
        }
        finally
        {
            await worker2.StopAsync();
            worker2.Dispose();
            // Auth3 forgets Worker2 (the operator clears its pin): the enlistment of Worker2 is the subject of another test.
            var auth3 = _auth3.Services.GetRequiredService<ApplicationIdentityService>();
            auth3.Remotes.Single( r => r.PartyName == "$Worker2" ).GetRequiredFeature<IRemoteKeys>().SetTrustedIdentity( TestHelper.Monitor, null );
        }
    }
}
