// The server sides of the fronts' features: no type of them is used here, the references are kept for the engine.
[assembly: CK.Setup.PreserveAssemblyReference( typeof( CK.UserProfile.UpdateUserCommandHandler ) )]
[assembly: CK.Setup.PreserveAssemblyReference( typeof( CK.UserProfile.UserPassword.Lost.PasswordLostCommandHandler ) )]
// The holder side of the enlistment (the approval sent down by the Admin).
[assembly: CK.Setup.PreserveAssemblyReference( typeof( CK.AppIdentity.Enlistment.PartyApprovalHandlers ) )]
[assembly: CK.Setup.PreserveAssemblyReference( typeof( CK.AppIdentity.AuthCenter.Enlistment.AuthCenterPeeringAuthority ) )]
