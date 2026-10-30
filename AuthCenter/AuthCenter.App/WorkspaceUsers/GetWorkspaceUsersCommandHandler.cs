using CK.Core;
using CK.Cris;
using CK.IO.UserManagement;
using CK.SqlServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CK.Sample.AuthCenter.WorkspaceUsers;

/// <summary>
/// App-level handler for <see cref="IGetWorkspaceUsersQCommand"/>. Both the UserBanned
/// (ban-aware) and UserInvitation (e-mail-aware) packages ship their own closed handler for
/// this command; composed together they would be ambiguous. Declaring
/// <see cref="ICommandHandler{IGetWorkspaceUsersQCommand}"/> here makes the Cris engine elect this
/// service over the package handlers, and it returns the combined projection (core columns + bans +
/// e-mail).
/// <para>
/// Same election mechanism as <see cref="User.GetUserProfileCommandHandler"/>.
/// </para>
/// </summary>
[ReplaceAutoService( typeof( CK.UserManagement.UserBanned.BannedWorkspaceUsersHandler ) )]
[ReplaceAutoService( typeof( CK.UserManagement.UserInvitation.UserInvitationWorkspaceUsersHandler ) )]
public class GetWorkspaceUsersCommandHandler : IAutoService, ICommandHandler<IGetWorkspaceUsersQCommand>
{
    readonly WorkspaceUsersQueries _queries;

    public GetWorkspaceUsersCommandHandler( WorkspaceUsersQueries queries )
    {
        _queries = queries;
    }

    [CommandHandler]
    public async Task<List<IWorkspaceUser>> GetWorkspaceUsersAsync( ISqlCallContext ctx, IGetWorkspaceUsersQCommand query )
    {
        var workspaceId = query.CurrentWorkspaceId.GetValueOrDefault();
        using( ctx.Monitor.OpenInfo( $"Handling {nameof( IGetWorkspaceUsersQCommand )} query (combined bans + e-mail). (WorkspaceId: {workspaceId})" ) )
        {
            try
            {
                var users = await _queries.GetWorkspaceUsersAsync( ctx, workspaceId );
                return users.ToList();
            }
            catch( Exception e )
            {
                ctx.Monitor.Error( e );
                return new();
            }
        }
    }
}
