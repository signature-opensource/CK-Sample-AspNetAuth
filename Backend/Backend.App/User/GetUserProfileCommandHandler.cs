using CK.Core;
using CK.Cris;
using CK.IO.Actor;
using CK.SqlServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CK.Sample.Backend.User;

/// <summary>
/// Overrides <c>CK.DB.Actor.UserTable.ReadUserProfileAsync</c> (the default handler for
/// <see cref="IGetUserProfileQCommand"/>) so the response carries the more specialized
/// <see cref="CK.IO.UserProfile.Workspace.IUserProfile"/> shape — <c>PreferredWorkspaceId</c>
/// and the <c>Groups</c> list — both required by the Angular user-service <c>isAdmin</c> signal.
/// <para>
/// Declaring <see cref="ICommandHandler{IGetUserProfileQCommand}"/> makes the Cris engine
/// elect this service over any other <c>[CommandHandler]</c> for the same command.
/// </para>
/// </summary>
public class GetUserProfileCommandHandler : IAutoService, ICommandHandler<IGetUserProfileQCommand>
{
    readonly UserQueries _queries;

    public GetUserProfileCommandHandler( UserQueries queries )
    {
        _queries = queries;
    }

    [CommandHandler]
    public Task<IUserProfile?> GetUserProfileAsync( ISqlCallContext ctx, IGetUserProfileQCommand cmd )
        => _queries.GetUserProfileAsync( ctx, cmd.UserId );
}
