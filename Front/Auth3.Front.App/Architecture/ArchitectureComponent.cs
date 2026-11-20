using CK.Core;
using CK.TS.Angular;

namespace CK.Sample.Front;

/// <summary>
/// The architecture page (in the private page): it shows where the commands execute and with which identity
/// (<see cref="IWhoAmIQCommand"/>, <see cref="IAskLaterQCommand"/>), the topology of the front (<see cref="IGetTopologyQCommand"/>)
/// and a little request console that sends refused or protected calls and dumps their answers.
/// </summary>
[NgRoutedComponent<INgPrivatePageComponent>( Route = "architecture" )]
[Package<SamplePackage>]
public sealed class ArchitectureComponent : NgRoutedComponent
{
}
