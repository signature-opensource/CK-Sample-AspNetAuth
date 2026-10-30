using CK.Core;
using CK.IO.UserProfile.Workspace;
using CK.SqlServer;
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CK.Sample.Backend.WorkspaceUsers;

/// <summary>
/// App-level workspace-user listing that combines the two feature-package projections: the core
/// columns plus the banishments (<c>CK.tUserBanned</c>, contributed by CK.DB.User.UserBanned) AND the
/// primary e-mail (<c>CK.tActorEMail</c>, <c>IsPrimary = 1</c>). Dapper materializes the merged
/// workspace-user Poco through its <see cref="ICombinedWorkspaceUser"/> leaf, so both the <c>Bans</c>
/// and <c>Email</c> properties are set.
/// </summary>
public class WorkspaceUsersQueries : IAutoService
{
    readonly CK.DB.Actor.UserTable _userTable;
    readonly PocoDirectory _pocoDirectory;

    /// <summary>
    /// The left outer joins duplicate the user row once per banishment and once per group — and, the two
    /// combined, once per (banishment, group) pair: the grouping is done in C# by
    /// <see cref="GetWorkspaceUsersAsync"/>. All the banishments are returned, expired ones included:
    /// the caller owns the definition of "currently banned".
    /// <para>
    /// The groups span all the zones, not only the queried workspace: the listing displays the
    /// memberships of a user outside of the current workspace. Rows are ordered by workspace, its own
    /// zone group first: a zone group is its own workspace but <c>CK.vGroup</c> gives it a null
    /// <c>ZoneId</c> (and therefore no <c>ZoneName</c>).
    /// </para>
    /// </summary>
    const string _getWorkspaceUsersSql =
        """
        select u.UserId
              ,u.UserName
              ,Email = isnull( e.EMail, '' )
              ,u.FirstName
              ,u.LastName
              ,IsWorkspaceAdmin = cast( case when CK.fAclGrantLevel( u.UserId, w.AclId ) >= 112 then 1 else 0 end as bit )
              ,u.ExtendedCultureId
              ,b.KeyReason
              ,b.BanStartDate
              ,b.BanEndDate
              ,pg.GroupId
              ,pg.GroupName
              ,pg.IsZone
              ,pg.ZoneId
              ,ZoneName = isnull( pz.ZoneName, '' )
          from CK.vUser u
              inner join CK.tActorProfile ap on ap.ActorId = u.UserId
              inner join CK.tWorkspace w on w.WorkspaceId = @WorkspaceId
              left outer join CK.tActorEMail e on e.ActorId = u.UserId and e.IsPrimary = 1
              left outer join CK.tUserBanned b on b.UserId = u.UserId
              left outer join CK.tActorProfile pap on pap.ActorId = u.UserId and pap.ActorId <> pap.GroupId
              left outer join CK.vGroup pg on pg.GroupId = pap.GroupId and pg.GroupId > 1
              left outer join CK.vZone pz on pz.ZoneId = pg.ZoneId
          where ap.GroupId = @WorkspaceId and u.UserId > 1
          order by u.UserId
                  ,case when pg.IsZone = 1 then pg.GroupId else pg.ZoneId end
                  ,pg.IsZone desc
                  ,pg.GroupName
                  ,b.BanStartDate;
        """;

    /// <summary>
    /// The ban part of a joined row. Members are nullable: a user without any banishment yields a row
    /// whose ban columns are all null (left outer join). Dapper hands out a null BanRow in that case.
    /// </summary>
    sealed class BanRow
    {
        public string? KeyReason { get; set; }
        public DateTime? BanStartDate { get; set; }
        public DateTime? BanEndDate { get; set; }
    }

    public WorkspaceUsersQueries( CK.DB.Actor.UserTable userTable, PocoDirectory pocoDirectory )
    {
        _userTable = userTable;
        _pocoDirectory = pocoDirectory;
    }

    public async Task<IReadOnlyList<CK.IO.UserManagement.IWorkspaceUser>> GetWorkspaceUsersAsync( ISqlCallContext ctx, int workspaceId )
    {
        var byId = new Dictionary<int, ICombinedWorkspaceUser>();
        await ctx[_userTable].QueryAsync<ICombinedWorkspaceUser, BanRow, IGroupInfos, object?>(
            _getWorkspaceUsersSql,
            ( user, ban, group ) =>
            {
                if( !byId.TryGetValue( user.UserId, out var existing ) )
                {
                    byId.Add( user.UserId, existing = user );
                }
                // Both collections are guarded: the banishment and group joins fan out into each other,
                // so each banishment row repeats once per group and each group row once per banishment.
                if( ban?.KeyReason != null
                    && !existing.Bans.Any( x => x.KeyReason == ban.KeyReason && x.BanStartDate == ban.BanStartDate ) )
                {
                    existing.Bans.Add( _pocoDirectory.Create<CK.IO.UserManagement.UserBanned.IUserBan>( b =>
                    {
                        b.KeyReason = ban.KeyReason;
                        b.BanStartDate = ban.BanStartDate!.Value;
                        b.BanEndDate = ban.BanEndDate!.Value;
                    } ) );
                }
                if( group != null && !existing.Groups.Any( g => g.GroupId == group.GroupId ) )
                {
                    existing.Groups.Add( group );
                }
                return null;
            },
            new { WorkspaceId = workspaceId },
            splitOn: "KeyReason,GroupId" );

        return byId.Values.Cast<CK.IO.UserManagement.IWorkspaceUser>().ToList();
    }
}
