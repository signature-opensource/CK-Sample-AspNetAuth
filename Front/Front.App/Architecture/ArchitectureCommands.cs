using CK.AppIdentity;
using CK.AppIdentity.Cris;
using CK.AppIdentity.TransportLayer;
using CK.Auth;
using CK.Core;
using CK.Cris;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CK.Sample.Front;

/// <summary>
/// "Who am I, there, later?": the front asks <see cref="IWhoAmIQCommand"/> to the target party <b>in its background</b>,
/// like a deferred work: there is no user's proof, the front only vouches for the user. The AuthCenter then grants at
/// most the Normal level, even when the session of the user is Critical.
/// </summary>
public interface IAskLaterQCommand : ICommand<IExecutionIdentity>, ICommandAuthNormal
{
    /// <summary>
    /// Gets or sets the full name of the party to ask.
    /// </summary>
    string TargetPartyFullName { get; set; }
}

/// <summary>
/// The topology of the front: its local party and its remotes, with their PartyActorId, their connection and what they
/// accept from the front (their advertisement over the Cris channel).
/// </summary>
public interface IGetTopologyQCommand : ICommand<ITopology>, ICommandAuthNormal
{
}

/// <summary>
/// The answer of the <see cref="IGetTopologyQCommand"/>.
/// </summary>
public interface ITopology : IPoco
{
    /// <summary>
    /// Gets or sets the full name of the local party (the front).
    /// </summary>
    string LocalPartyFullName { get; set; }

    /// <summary>
    /// Gets or sets the PartyActorId of the local party (given by its AuthCenter when the channel connected).
    /// </summary>
    int LocalPartyActorId { get; set; }

    /// <summary>
    /// Gets the remotes of the local party.
    /// </summary>
    IList<IRemotePartyInfo> Remotes { get; }
}

/// <summary>
/// A remote party of the front.
/// </summary>
public interface IRemotePartyInfo : IPoco
{
    /// <summary>
    /// Gets or sets the full name of the remote.
    /// </summary>
    string FullName { get; set; }

    /// <summary>
    /// Gets or sets the PartyActorId of the remote (0 when it is not resolved).
    /// </summary>
    int PartyActorId { get; set; }

    /// <summary>
    /// Gets or sets whether this remote is the AuthCenter of the front.
    /// </summary>
    bool IsAuthCenter { get; set; }

    /// <summary>
    /// Gets or sets the availability of the connection to the remote (None, DangerZone, Low, Connected).
    /// </summary>
    string Connection { get; set; }

    /// <summary>
    /// Gets the names of the commands the remote accepts from the front: the routes of the dispatch (empty until it
    /// advertised them).
    /// </summary>
    IList<string> AcceptedCommands { get; }
}

/// <summary>
/// Handles the commands of the architecture page.
/// </summary>
public sealed class ArchitectureHandlers : ISingletonAutoService
{
    /// <summary>
    /// Executes the <see cref="IWhoAmIQCommand"/> in the background of the front (no user's proof) and returns its result.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="command">The command.</param>
    /// <param name="hub">The ambient services of the request (the deferred work runs with them).</param>
    /// <param name="background">The background executor.</param>
    /// <returns>The execution identity seen by the target party.</returns>
    [CommandHandler]
    public async Task<IExecutionIdentity> HandleAsync( IActivityMonitor monitor,
                                                       IAskLaterQCommand command,
                                                       AmbientServiceHub hub,
                                                       CrisBackgroundExecutorService background )
    {
        // The validated actor (the ICommandAuthNormal part they share) is copied, the target party is not in a shared part.
        var whoAmI = command.Create<IWhoAmIQCommand>( c => c.TargetPartyFullName = command.TargetPartyFullName );
        var executed = await background.Submit( monitor, whoAmI, hub.CleanClone() ).ExecutedCommand.ConfigureAwait( false );
        if( executed.Result is ICrisResultError error )
        {
            Throw.CKException( string.Join( " ", error.Errors.Select( e => e.Text ) ) );
        }
        return (IExecutionIdentity)executed.Result!;
    }

    /// <summary>
    /// Describes the topology of the front.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="party">The current local party (the front).</param>
    /// <param name="pocos">The poco directory.</param>
    /// <returns>The topology.</returns>
    [CommandHandler]
    public ITopology Handle( IGetTopologyQCommand command, CurrentLocalParty party, PocoDirectory pocos )
    {
        var local = party.LocalParty;
        var authCenter = local.GetFeature<CK.AppIdentity.Cris.AuthCenter.RemoteAuthCenter>()?.Center.AuthCenter;
        return pocos.Create<ITopology>( t =>
        {
            t.LocalPartyFullName = local.FullName;
            t.LocalPartyActorId = local.PartyActorId;
            foreach( var remote in local.Remotes )
            {
                t.Remotes.Add( pocos.Create<IRemotePartyInfo>( r =>
                {
                    r.FullName = remote.FullName;
                    r.PartyActorId = remote.PartyActorId;
                    r.IsAuthCenter = remote == authCenter;
                    r.Connection = (remote.GetFeature<TransportFeature>()?.ConnectionAvailability ?? ConnectionAvailability.None).ToString();
                    if( remote.GetFeature<CrisChannelFeature>()?.AcceptedCommands is { } accepted )
                    {
                        foreach( var name in accepted.Order( StringComparer.Ordinal ) ) r.AcceptedCommands.Add( name );
                    }
                } ) );
            }
        } );
    }
}
