using CK.Core;
using System.Threading.Tasks;

namespace CK.Sample.Admin;

/// <summary>
/// The back-office has no database (like the front): it knows no actor. The commands it receives are authenticated by
/// their bearer token (the AuthCenter's); what needs to read an actor here (the restoration of a command executed later,
/// in the background) fails closed: the command is refused.
/// </summary>
public sealed class NoActorInfoProvider : IActorInfoProvider
{
    /// <summary>
    /// Always returns null.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="actorId">The actor identifier.</param>
    /// <returns>Null.</returns>
    public ValueTask<ActorInfo?> GetActorInfoAsync( IActivityMonitor monitor, int actorId ) => ValueTask.FromResult<ActorInfo?>( null );
}
