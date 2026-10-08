using CK.Core;
using CK.IO.Authentication.Weak;
using CK.Ng.Cris.AspNet.Authentication.Basic;
using CK.TypeScript;

namespace CK.Sample.Front;

/// <summary>
/// The TypeScript package of the sample: the commands that the application sends (the Basic login form comes with
/// <see cref="NgCrisAuthenticationBasicPackage"/>).
/// </summary>
[TypeScriptPackage]
[Requires<NgCrisAuthenticationBasicPackage>]
[RegisterTypeScriptType( typeof( IWeakLoginCommand ) )]
[RegisterTypeScriptType( typeof( IHelloCommand ) )]
public class SamplePackage : TypeScriptPackage
{
}
