using CK.Core;
using CK.DB.Actor;
using CK.DB.Actor.WeakActor;
using CK.DB.User.UserOidc;
using CK.DB.User.UserPassword;
using CK.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace CK.Sample.AuthCenter;

/// <summary>
/// The demo data of the sample, created when missing (configuration section "Demo"):
/// <list type="bullet">
///   <item>the user "Albert" with the password "Demo:AlbertPassword" (Basic login);</item>
///   <item>the enabled weak actor "Operator" (Weak login, Unsafe level);</item>
///   <item>when "Demo:OidcScheme" (the scheme suffix, like "Google") and "Demo:OidcSub" are set, Albert is bound to this
///   OpenID Connect identity (there is no automatic account creation or binding).</item>
/// </list>
/// </summary>
public static class DemoData
{
    /// <summary>
    /// The name of the demo user.
    /// </summary>
    public const string UserName = "Albert";

    /// <summary>
    /// The name of the demo weak actor.
    /// </summary>
    public const string WeakActorName = "Operator";

    /// <summary>
    /// Ensures that the demo data exists.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="services">The application services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The awaitable.</returns>
    public static async Task EnsureAsync( IActivityMonitor monitor, IServiceProvider services, IConfiguration configuration )
    {
        var demo = configuration.GetSection( "Demo" );
        using var ctx = new SqlStandardCallContext( monitor );
        var users = services.GetRequiredService<UserTable>();
        var userId = await users.FindByNameAsync( ctx, UserName );
        if( userId == 0 )
        {
            userId = await users.CreateUserAsync( ctx, 1, UserName );
            monitor.Info( $"Demo user '{UserName}' created (#{userId})." );
        }
        var password = demo["AlbertPassword"];
        if( !string.IsNullOrEmpty( password ) )
        {
            await services.GetRequiredService<UserPasswordTable>().CreateOrUpdatePasswordUserAsync( ctx, 1, userId, password );
        }

        var weakActors = services.GetRequiredService<WeakActorTable>();
        using( var cmd = new SqlCommand( "select WeakActorId from CK.tWeakActor where WeakActorName=@Name" ) )
        {
            cmd.Parameters.AddWithValue( "@Name", WeakActorName );
            if( await ctx[weakActors.Database].ExecuteScalarAsync( cmd ) is not int )
            {
                var id = await weakActors.CreateAsync( ctx, 1, WeakActorName );
                await weakActors.EnableAsync( ctx, 1, id );
                monitor.Info( $"Demo weak actor '{WeakActorName}' created (#{id})." );
            }
        }

        var scheme = demo["OidcScheme"];
        var sub = demo["OidcSub"];
        if( !string.IsNullOrEmpty( scheme ) && !string.IsNullOrEmpty( sub ) )
        {
            var oidc = services.GetRequiredService<UserOidcTable>();
            var info = oidc.CreateUserInfo<IUserOidcInfo>();
            info.SchemeSuffix = scheme;
            info.Sub = sub;
            await oidc.CreateOrUpdateOidcUserAsync( ctx, 1, userId, info );
            monitor.Info( $"Demo user '{UserName}' bound to 'Oidc.{scheme}'." );
        }
    }
}
