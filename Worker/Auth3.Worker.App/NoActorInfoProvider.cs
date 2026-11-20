using CK.Core;
using System.Threading.Tasks;

namespace CK.Sample.Worker;

/// <summary>
/// The Worker has no database: it knows no actor. What needs to read an actor here fails closed: the command is refused.
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
