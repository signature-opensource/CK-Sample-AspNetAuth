using CK.Core;
using CK.Ng.Cris.AspNet.Authentication.Basic;
using CK.Ng.Cris.AspNet.Authentication.Weak;
using CK.Ng.PublicPage;
using CK.Ng.Zorro;
using CK.TypeScript;

namespace CK.Sample.Front;

/// <summary>
/// The TypeScript package of the sample. The application is made of standard packages:
/// <list type="bullet">
///   <item>the login page with the Basic and Weak forms and the OpenID Connect buttons (CK.Ng.Cris.AspNet.Authentication.*);</item>
///   <item>the private page (guarded: Normal level at least) in the back-office layout (CK.Ng.Zorro.BackOffice), whose top
///   bar shows the UserInfoBox (Res/private-page.t);</item>
///   <item>the public page (CK.Ng.PublicPage), where the anonymous and the weak actors land.</item>
/// </list>
/// The sample's own code is its <see cref="IHelloCommand"/>, the <see cref="HomeComponent"/> (the private home) and the
/// <see cref="PublicHomeComponent"/> (the public home).
/// </summary>
[TypeScriptPackage]
[Requires<NgCrisAuthenticationBasicPackage, NgCrisAuthenticationWeakPackage>]
[Requires<LayoutComponent, PublicPageComponent>]
[RegisterTypeScriptType( typeof( IHelloCommand ) )]
public class SamplePackage : TypeScriptPackage
{
}
