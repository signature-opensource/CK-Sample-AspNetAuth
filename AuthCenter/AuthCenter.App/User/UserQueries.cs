using CK.Core;
using CK.IO.UserProfile.Workspace;
using CK.SqlServer;
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CK.Sample.AuthCenter.User;

/// <summary>
/// Dapper queries that read a <see cref="IUserProfile"/> with its <see cref="IUserGroup"/> list.
/// The base <c>CK.sUserUserProfileRead</c> stored procedure only projects <c>UserId</c>/<c>UserName</c>, so this
/// service is used by <see cref="GetUserProfileCommandHandler"/> to override the default
/// <c>IGetUserProfileQCommand</c> handler with a result that includes groups + grant levels.
/// <para>
/// Beware: bypassing the procedure also bypasses every <c>transform:sUserUserProfileRead</c> a feature
/// package contributes, so each of those columns must be mirrored here. That is the case of
/// <c>IsTemporaryPassword</c> (CK.DB.User.UserPassword.Reset), read by the Angular navigation guard
/// to redirect a user whose password is temporary to the reset page, and of <c>IsBanned</c>
/// (CK.DB.User.UserBanned), read by the banishment guard.
/// </para>
/// </summary>
public class UserQueries : IAutoService
{
    readonly DB.Actor.UserTable _userTable;
    readonly PocoDirectory _pocoDirectory;

    public UserQueries( DB.Actor.UserTable userTable, PocoDirectory pocoDirectory )
    {
        _userTable = userTable;
        _pocoDirectory = pocoDirectory;
    }

    public async Task<IUserProfile?> GetUserProfileAsync( ISqlCallContext ctx, int userId )
    {
        if( userId <= 0 ) return null;

        var rows = await ctx[_userTable].QueryAsync<FlatUserProfile>(
            """
            select distinct
                   u.UserId
                  ,u.UserName
                  ,u.PreferredWorkspaceId
                  ,u.ExtendedCultureId
                  ,IsTemporaryPassword = isnull( p.IsTemporary, cast( 0 as bit ) )
                   -- An "exists" and not a join: a user can carry several active banishments at once,
                   -- and a join would duplicate the row (this select is already grouped by user).
                  ,IsBanned = case when exists( select 1
                                                from CK.fUserBannedViewAt( sysutcdatetime() ) ban
                                                where ban.UserId = u.UserId )
                                   then cast( 1 as bit ) else cast( 0 as bit ) end
                  ,GroupId    = isnull( g.GroupId, 0 )
                  ,GroupName  = isnull( g.GroupName, '' )
                  ,IsZone     = isnull( g.IsZone, cast( 0 as bit ) )
                  ,ZoneId     = isnull( g.ZoneId, 0 )
                  ,ZoneName   = isnull( z.ZoneName, '' )
                  ,GrantLevel = isnull( CK.fAclGrantLevel( @UserId, acl.AclId ), 0 )
            from CK.tUser u
                left join CK.tUserPassword p     on p.UserId   = u.UserId
                left join CK.tActorProfile ap
                    on ap.ActorId = u.UserId
                    and ap.ActorId <> ap.GroupId
                left join CK.vGroup g            on g.GroupId  = ap.GroupId
                left join CK.vZone z             on z.ZoneId   = g.ZoneId
                left join CK.vAclConfigMemory acl on acl.ActorId = g.GroupId
            where u.UserId = @UserId;
            """,
            new { UserId = userId } );

        return rows.GroupBy( r => r.UserId )
            .Select( u =>
            {
                var user = u.First();
                return _pocoDirectory.Create<IUserProfile>( up =>
                {
                    up.UserId = user.UserId;
                    up.UserName = user.UserName;
                    up.PreferredWorkspaceId = user.PreferredWorkspaceId;
                    up.ExtendedCultureId = user.ExtendedCultureId;
                    up.IsTemporaryPassword = user.IsTemporaryPassword;
                    up.IsBanned = user.IsBanned;
                    foreach( var g in u.Where( r => r.GroupId != 0 ).DistinctBy( r => r.GroupId ) )
                    {
                        up.Groups.Add( _pocoDirectory.Create<IUserGroup>( ug =>
                        {
                            ug.GrantLevel = g.GrantLevel;
                            ug.Group = _pocoDirectory.Create<IGroupInfos>( gi =>
                            {
                                gi.GroupId = g.GroupId;
                                gi.GroupName = g.GroupName;
                                gi.IsZone = g.IsZone;
                                gi.ZoneId = g.ZoneId;
                                gi.ZoneName = g.ZoneName;
                            } );
                        } ) );
                    }
                } );
            } )
            .SingleOrDefault();
    }
}

record FlatUserProfile
{
    public int UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public int PreferredWorkspaceId { get; init; }
    public int ExtendedCultureId { get; init; }
    public bool IsTemporaryPassword { get; init; }
    public bool IsBanned { get; init; }
    public int GroupId { get; init; }
    public string GroupName { get; init; } = string.Empty;
    public bool IsZone { get; init; }
    public int ZoneId { get; init; }
    public string ZoneName { get; init; } = string.Empty;
    public int GrantLevel { get; init; }
}
