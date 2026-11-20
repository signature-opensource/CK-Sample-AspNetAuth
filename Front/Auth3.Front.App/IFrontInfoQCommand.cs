using CK.Core;
using CK.Cris;
using System;
using System.Diagnostics;

namespace CK.Sample.Front;

/// <summary>
/// A query of the "MiniApp" front: it requires no authentication and returns basic information on the process of this
/// Front host.
/// </summary>
public interface IFrontInfoQCommand : ICommand<string>
{
}

/// <summary>
/// Handles the <see cref="IFrontInfoQCommand"/>.
/// </summary>
public sealed class FrontInfoService : ISingletonAutoService
{
    /// <summary>
    /// Describes the process of this host.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>The process information.</returns>
    [CommandHandler]
    public string GetFrontInfo( IFrontInfoQCommand command )
    {
        using var p = Process.GetCurrentProcess();
        return $"Process {p.Id} on '{Environment.MachineName}', started at {p.StartTime.ToUniversalTime():u}, " +
               $"working set {p.WorkingSet64 / (1024 * 1024)} MB, {Environment.Version} runtime, answered at {DateTime.UtcNow:u}.";
    }
}
