using CK.Core;
using CK.IO.User.UserPasskey;
using CK.IO.User.UserPassword;
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
/// The sample's own code is its <see cref="IHelloCommand"/>, the <see cref="HomeComponent"/> (the private home), the
/// <see cref="PublicHomeComponent"/> (the public home) and the <see cref="ArchitectureComponent"/> (where the commands
/// execute, with which identity, the topology and a console of refused calls).
/// </summary>
[TypeScriptPackage]
[Requires<NgCrisAuthenticationBasicPackage, NgCrisAuthenticationWeakPackage>]
[Requires<LayoutComponent, PublicPageComponent>]
[RegisterTypeScriptType( typeof( IHelloCommand ) )]
// The architecture page: a party command (handled by every party), the deferred question, the topology.
[RegisterTypeScriptType( typeof( IWhoAmIQCommand ) )]
[RegisterTypeScriptType( typeof( IAskLaterQCommand ) )]
[RegisterTypeScriptType( typeof( IGetTopologyQCommand ) )]
// The user's self-service on its login methods: handled by the AuthCenter (posted to "/.cris" and dispatched).
[RegisterTypeScriptType( typeof( IGetPasskeysQCommand ) )]
[RegisterTypeScriptType( typeof( IRenamePasskeyCommand ) )]
[RegisterTypeScriptType( typeof( IDeletePasskeyCommand ) )]
[RegisterTypeScriptType( typeof( ISetPasswordCommand ) )]
public class SamplePackage : TypeScriptPackage
{
}
