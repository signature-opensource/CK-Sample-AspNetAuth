using CK.Auth;
using CK.Core;
using CK.Cris;

namespace CK.Sample.Front;

/// <summary>
/// A command of the application that requires an authenticated actor (at least the <see cref="AuthLevel.Normal"/> level):
/// a weak actor (Unsafe level) is refused.
/// </summary>
public interface IHelloCommand : ICommand<string>, ICommandAuthNormal
{
}

/// <summary>
/// Handles the <see cref="IHelloCommand"/> on the front: the authentication of the request comes from its bearer token
/// (verified with the AuthCenter's keys, bound to the browser by the binding cookie).
/// </summary>
public sealed class HelloService : ISingletonAutoService
{
    /// <summary>
    /// Says hello to the actor.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="info">The authentication of the request.</param>
    /// <returns>The greeting.</returns>
    [CommandHandler]
    public string Hello( IHelloCommand command, AuthenticationInfo info )
        => info.IsImpersonated
            ? $"Hello {info.Actor.ActorName} (impersonated by {info.ActualActor.ActorName}), at the {info.Actor.Level} level."
            : $"Hello {info.Actor.ActorName}, at the {info.Actor.Level} level.";
}
