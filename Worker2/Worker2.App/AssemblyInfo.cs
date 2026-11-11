// Worker2 has no code of its own: the references are kept for the engine.
[assembly: CK.Setup.PreserveAssemblyReference( typeof( Microsoft.Extensions.Hosting.CKAppIdentityHostApplicationBuilderExtensions ) )]
[assembly: CK.Setup.PreserveAssemblyReference( typeof( CK.Auth.AuthenticationReceptionHandler ) )]
[assembly: CK.Setup.PreserveAssemblyReference( typeof( CK.Sample.WhoAmIHandler ) )]
[assembly: CK.Setup.PreserveAssemblyReference( typeof( CK.AppIdentity.AuthCenter.RemoteAuthCenterFeatureDriver ) )]
[assembly: CK.Setup.PreserveAssemblyReference( typeof( CK.AppIdentity.Enlistment.PartyApprovalHandlers ) )]
[assembly: CK.Setup.PreserveAssemblyReference( typeof( CK.AppIdentity.AuthCenter.Enlistment.AuthCenterPeeringAuthority ) )]
