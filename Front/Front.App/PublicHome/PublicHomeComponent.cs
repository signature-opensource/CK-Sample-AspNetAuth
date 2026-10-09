using CK.Core;
using CK.Ng.PublicPage;
using CK.TS.Angular;

namespace CK.Sample.Front;

/// <summary>
/// The home of the public page: the private page refuses the anonymous and the weak actors (Unsafe level), they land
/// here. The anonymous is invited to log in, a weak actor sees the <see cref="HomeComponent"/> (its hello is refused).
/// </summary>
[NgRoutedComponent<PublicPageComponent>( Route = "" )]
[Package<SamplePackage>]
[Requires<HomeComponent>]
public sealed class PublicHomeComponent : NgRoutedComponent
{
}
