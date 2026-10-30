using CK.Core;
using CK.Ng.Cris.AspNet.Authentication.Basic;
using CK.Ng.Cris.AspNet.Authentication.Weak;
using CK.Ng.PublicPage;
using CK.Ng.UserProfile.UserOidc;
using CK.Ng.UserProfile.UserPasskey;
using CK.Ng.UserProfile.UserPassword;
using CK.Ng.Zorro;
using CK.TypeScript;

namespace CK.Sample.Front;

/// <summary>
/// The TypeScript package of the sample. The application is made of standard packages:
/// <list type="bullet">
///   <item>the login page with the Basic and Weak forms and the OpenID Connect buttons (CK.Ng.Cris.AspNet.Authentication.*);</item>
///   <item>the private page (guarded: Normal level at least) in the back-office layout (CK.Ng.Zorro.BackOffice), whose top
///   bar shows the UserInfoBox (Res/private-page.t);</item>
///   <item>the public page (CK.Ng.PublicPage), where the anonymous and the weak actors land;</item>
///   <item>the profile page (CK.Ng.UserProfile) with its "Sign-in methods" tab (CK.Ng.UserProfile.UserPassword,
///   .UserPasskey, .UserOidc).</item>
/// </list>
/// The sample's own code is its <see cref="IHelloCommand"/>, the <see cref="HomeComponent"/> (the private home), the
/// <see cref="PublicHomeComponent"/> (the public home) and the <see cref="ArchitectureComponent"/> (where the commands
/// execute, with which identity, the topology and a console of refused calls).
/// </summary>
[TypeScriptPackage]
[Requires<NgCrisAuthenticationBasicPackage, NgCrisAuthenticationWeakPackage>]
[Requires<LayoutComponent, PublicPageComponent>]
// The profile page (the user info box's "Profile" item) and its "Sign-in methods" tab: the password, the passkeys and the
// linked accounts. Their commands are handled by the AuthCenter (posted to "/.cris" and dispatched).
[Requires<UserProfilePasswordPackage, UserProfilePasskeyPackage, UserProfileOidcPackage>]
[RegisterTypeScriptType( typeof( IHelloCommand ) )]
// The architecture page: a party command (handled by every party), the deferred question, the topology.
[RegisterTypeScriptType( typeof( IWhoAmIQCommand ) )]
[RegisterTypeScriptType( typeof( IAskLaterQCommand ) )]
[RegisterTypeScriptType( typeof( IGetTopologyQCommand ) )]
public class SamplePackage : TypeScriptPackage
{
}
