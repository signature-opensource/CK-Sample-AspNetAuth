using CK.Core;
using CK.DB.Actor;
using CK.DB.Actor.WeakActor;
using CK.DB.Acl;
using CK.DB.Workspace;
using CK.DB.User.UserOidc;
using CK.DB.User.UserPassword;
using CK.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace CK.Sample.Backend;

/// <summary>
/// The demo data of the sample, created when missing (configuration section "Demo"):
/// <list type="bullet">
///   <item>the user "Albert" with the password "Demo:AlbertPassword" (Basic login), administrator of the workspace
///   "Demo" (the Admin front's user management);</item>
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
    /// The name of the demo workspace, administered by <see cref="UserName"/>.
    /// </summary>
    public const string WorkspaceName = "Demo";

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

        await EnsureWorkspaceAsync( monitor, ctx, services, users, userId );

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

    // The demo workspace: Albert administers it (the Admin front's user management), it is his preferred workspace and
    // he is a member of its zone (the profile's groups tell the fronts which workspace he administers).
    static async Task EnsureWorkspaceAsync( IActivityMonitor monitor, SqlStandardCallContext ctx, IServiceProvider services, UserTable users, int userId )
    {
        int workspaceId;
        using( var cmd = new SqlCommand( "select PreferredWorkspaceId from CK.tUser where UserId=@UserId" ) )
        {
            cmd.Parameters.AddWithValue( "@UserId", userId );
            workspaceId = await ctx[users.Database].ExecuteScalarAsync( cmd ) is int id ? id : 0;
        }
        if( workspaceId <= 0 )
        {
            workspaceId = (await services.GetRequiredService<WorkspaceTable>().CreateWorkspaceAsync( ctx, 1, WorkspaceName )).WorkspaceId;
            int aclId;
            using( var cmd = new SqlCommand( "select AclId from CK.tWorkspace where WorkspaceId=@WorkspaceId" ) )
            {
                cmd.Parameters.AddWithValue( "@WorkspaceId", workspaceId );
                aclId = (int)(await ctx[users.Database].ExecuteScalarAsync( cmd ))!;
            }
            // 127: the administrator level of the workspace.
            await services.GetRequiredService<AclTable>().AclGrantSetAsync( ctx, 1, aclId, userId, "DemoWorkspaceAdmin", 127 );
            await services.GetRequiredService<CK.DB.Workspace.Package>().SetUserPreferredWorkspaceAsync( ctx, 1, userId, workspaceId );
            monitor.Info( $"Demo workspace '{WorkspaceName}' created (#{workspaceId}), administered by '{UserName}'." );
        }
        await services.GetRequiredService<CK.DB.Zone.GroupTable>().AddMemberAsync( ctx, 1, workspaceId, userId, autoAddMemberInZone: true );
    }
}
