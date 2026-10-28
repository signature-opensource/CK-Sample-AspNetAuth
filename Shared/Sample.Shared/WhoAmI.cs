using CK.AppIdentity;
using CK.Auth;
using CK.Core;
using CK.Cris;

namespace CK.Sample;

/// <summary>
/// Who executes a command, and for whom: the answer of the <see cref="IWhoAmIQCommand"/>.
/// </summary>
public interface IExecutionIdentity : IPoco
{
    /// <summary>
    /// Gets or sets the full name of the party that executed the command.
    /// </summary>
    string PartyFullName { get; set; }

    /// <summary>
    /// Gets or sets the PartyActorId of the party that executed the command (0 when it is not resolved).
    /// </summary>
    int PartyActorId { get; set; }

    /// <summary>
    /// Gets or sets the name of the actor for whom the command has been executed.
    /// </summary>
    string ActorName { get; set; }

    /// <summary>
    /// Gets or sets the authentication level of the actor at the executing party: the proved level, or the one granted to
    /// the caller party (never above Normal without the user's proof).
    /// </summary>
    string Level { get; set; }

    /// <summary>
    /// Gets or sets the name of the actual actor when the actor is impersonated.
    /// </summary>
    string? ActualActorName { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="AuthenticationInfo.CallerPartyId"/>: the party through which the identity entered the
    /// executing party (the party itself for a direct request, the remote that vouched for the user otherwise).
    /// </summary>
    int CallerPartyId { get; set; }
}

/// <summary>
/// "Who am I, there?": a party command (<see cref="ICommandTargetParty"/>) handled by every party of the sample. It
/// executes where its <see cref="ICommandTargetParty.TargetPartyFullName"/> is: the front itself, or the AuthCenter (the
/// front dispatches it to its remote). The code that sends it doesn't change.
/// </summary>
public interface IWhoAmIQCommand : ICommand<IExecutionIdentity>, ICommandAuthNormal, ICommandTargetParty
{
}

/// <summary>
/// Handles <see cref="IWhoAmIQCommand"/>: the same code in every party.
/// </summary>
public sealed class WhoAmIHandler : ISingletonAutoService
{
    /// <summary>
    /// Answers who executes the command, for whom, at which level and through which caller party.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="info">The authentication of the execution.</param>
    /// <param name="party">The party that executes the command.</param>
    /// <param name="pocos">The poco directory.</param>
    /// <returns>The execution identity.</returns>
    [CommandHandler]
    public IExecutionIdentity Handle( IWhoAmIQCommand command, AuthenticationInfo info, CurrentLocalParty party, PocoDirectory pocos )
    {
        return pocos.Create<IExecutionIdentity>( r =>
        {
            r.PartyFullName = party.LocalParty.FullName;
            r.PartyActorId = party.LocalParty.PartyActorId;
            r.ActorName = info.Actor.ActorName;
            r.Level = info.Actor.Level.ToString();
            r.ActualActorName = info.IsImpersonated ? info.ActualActor.ActorName : null;
            r.CallerPartyId = info.CallerPartyId;
        } );
    }
}
