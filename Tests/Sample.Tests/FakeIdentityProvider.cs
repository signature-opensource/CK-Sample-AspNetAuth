using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace CK.Sample.Tests;

/// <summary>
/// (A copy of the one of CK.AppIdentity.Cris.AuthCenter.Tests: test helpers are not packaged.)
/// A minimal OpenID Connect provider on a loopback port: discovery, JWKS and token endpoint (authorization code with
/// PKCE, client_secret_post). The "browser" part of the authorization is <see cref="Authorize"/>: the user logs in
/// and the provider "redirects" (it returns the code and the state).
/// </summary>
public sealed class FakeIdentityProvider : IDisposable
{
    public const string ClientId = "the-client";
    public const string ClientSecret = "the-client-secret";
    const string AccessToken = "the-access-token";

    readonly HttpListener _listener;
    readonly RSA _rsa;
    readonly RsaSecurityKey _key;
    readonly ConcurrentDictionary<string, Grant> _grants = new();
    readonly CancellationTokenSource _stop = new();

    sealed record Grant( string Sub, string Nonce, string Challenge, string RedirectUri, string ClientId );

    public FakeIdentityProvider()
    {
        var port = GetFreePort();
        BaseUrl = $"http://localhost:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add( BaseUrl + "/" );
        _listener.Start();
        _rsa = RSA.Create( 2048 );
        _key = new RsaSecurityKey( _rsa ) { KeyId = "key-1" };
        _ = Task.Run( LoopAsync );
    }

    /// <summary>
    /// Gets the base URL: the authority and the issuer.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>
    /// Gets or sets the issuer written in the identity tokens. Defaults to <see cref="BaseUrl"/>.
    /// </summary>
    public string? TokenIssuer { get; set; }

    /// <summary>
    /// Gets or sets the audience written in the identity tokens. Defaults to <see cref="ClientId"/>.
    /// </summary>
    public string? TokenAudience { get; set; }

    /// <summary>
    /// Gets or sets a nonce that replaces the one of the authorization request.
    /// </summary>
    public string? TokenNonce { get; set; }

    /// <summary>
    /// Gets or sets an at_hash that replaces the one of the access token.
    /// </summary>
    public string? TokenAtHash { get; set; }

    /// <summary>
    /// Gets the number of the redeemed codes.
    /// </summary>
    public int RedeemCount { get; private set; }

    /// <summary>
    /// The user logs in at the provider: returns the code and the state that the provider sends to the redirect URI.
    /// </summary>
    /// <param name="authorizationUrl">The authorization URL (from the start of the login).</param>
    /// <param name="sub">The subject of the user.</param>
    /// <returns>The redirect URI, the code and the state.</returns>
    public (string RedirectUri, string Code, string State) Authorize( string authorizationUrl, string sub )
    {
        var u = new Uri( authorizationUrl );
        if( !authorizationUrl.StartsWith( BaseUrl + "/authorize?", StringComparison.Ordinal ) ) throw new ArgumentException( "Not our authorization endpoint." );
        var q = HttpUtility.ParseQueryString( u.Query );
        if( q["response_type"] != "code" || q["code_challenge_method"] != "S256" ) throw new ArgumentException( "Code flow with PKCE (S256) expected." );
        if( !(q["scope"] ?? "").Split( ' ' ).Contains( "openid" ) ) throw new ArgumentException( "openid scope expected." );
        var code = Base64Url.EncodeToString( RandomNumberGenerator.GetBytes( 16 ) );
        _grants[code] = new Grant( sub, q["nonce"]!, q["code_challenge"]!, q["redirect_uri"]!, q["client_id"]! );
        return (q["redirect_uri"]!, code, q["state"]!);
    }

    async Task LoopAsync()
    {
        while( !_stop.IsCancellationRequested )
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }
            try
            {
                await HandleAsync( ctx );
            }
            catch
            {
                ctx.Response.StatusCode = 500;
            }
            finally
            {
                ctx.Response.Close();
            }
        }
    }

    async Task HandleAsync( HttpListenerContext ctx )
    {
        var path = ctx.Request.Url!.AbsolutePath;
        if( path == "/.well-known/openid-configuration" )
        {
            await WriteJsonAsync( ctx, new Dictionary<string, object>
            {
                ["issuer"] = BaseUrl,
                ["authorization_endpoint"] = BaseUrl + "/authorize",
                ["token_endpoint"] = BaseUrl + "/token",
                ["jwks_uri"] = BaseUrl + "/jwks",
                ["response_types_supported"] = new[] { "code" },
                ["subject_types_supported"] = new[] { "public" },
                ["id_token_signing_alg_values_supported"] = new[] { "RS256" }
            } );
        }
        else if( path == "/jwks" )
        {
            var p = _rsa.ExportParameters( false );
            await WriteJsonAsync( ctx, new Dictionary<string, object>
            {
                ["keys"] = new[] { new Dictionary<string, string>
                {
                    ["kty"] = "RSA", ["use"] = "sig", ["alg"] = "RS256", ["kid"] = _key.KeyId,
                    ["n"] = Base64Url.EncodeToString( p.Modulus ), ["e"] = Base64Url.EncodeToString( p.Exponent )
                } }
            } );
        }
        else if( path == "/token" && ctx.Request.HttpMethod == "POST" )
        {
            using var reader = new System.IO.StreamReader( ctx.Request.InputStream );
            var form = HttpUtility.ParseQueryString( await reader.ReadToEndAsync() );
            if( form["grant_type"] != "authorization_code"
                || form["client_id"] != ClientId
                || form["client_secret"] != ClientSecret
                || !_grants.TryRemove( form["code"] ?? "", out var g )
                || g.RedirectUri != form["redirect_uri"]
                || g.ClientId != ClientId
                || Base64Url.EncodeToString( SHA256.HashData( Encoding.ASCII.GetBytes( form["code_verifier"] ?? "" ) ) ) != g.Challenge )
            {
                ctx.Response.StatusCode = 400;
                await WriteJsonAsync( ctx, new Dictionary<string, object> { ["error"] = "invalid_grant" } );
                return;
            }
            ++RedeemCount;
            var now = DateTime.UtcNow;
            var idToken = new JsonWebTokenHandler().CreateToken( new SecurityTokenDescriptor
            {
                Issuer = TokenIssuer ?? BaseUrl,
                Audience = TokenAudience ?? ClientId,
                IssuedAt = now,
                NotBefore = now,
                Expires = now.AddMinutes( 5 ),
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = g.Sub,
                    ["nonce"] = TokenNonce ?? g.Nonce,
                    // The left half of the SHA-256 of the access token.
                    ["at_hash"] = TokenAtHash ?? Base64Url.EncodeToString( SHA256.HashData( Encoding.ASCII.GetBytes( AccessToken ) ).AsSpan( 0, 16 ) )
                },
                SigningCredentials = new SigningCredentials( _key, SecurityAlgorithms.RsaSha256 )
            } );
            await WriteJsonAsync( ctx, new Dictionary<string, object> { ["access_token"] = AccessToken, ["token_type"] = "Bearer", ["id_token"] = idToken } );
        }
        else
        {
            ctx.Response.StatusCode = 404;
        }
    }

    static async Task WriteJsonAsync( HttpListenerContext ctx, object o )
    {
        ctx.Response.ContentType = "application/json";
        var bytes = JsonSerializer.SerializeToUtf8Bytes( o );
        await ctx.Response.OutputStream.WriteAsync( bytes );
    }

    static int GetFreePort()
    {
        var l = new TcpListener( IPAddress.Loopback, 0 );
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
        _rsa.Dispose();
    }
}
