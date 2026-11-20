using CK.Core;
using CK.TS.Angular;

namespace CK.Sample.Front;

/// <summary>
/// The home of the private page: who is logged in, and the <see cref="IHelloCommand"/> (that requires the Normal level).
/// It is also shown by the <see cref="PublicHomeComponent"/> to a weak actor (whose hello is refused).
/// </summary>
[NgRoutedComponent<INgPrivatePageComponent>( Route = "" )]
[Package<SamplePackage>]
public sealed class HomeComponent : NgRoutedComponent
{
}
