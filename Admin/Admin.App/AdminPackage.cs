using CK.Core;
using CK.IO.DynamicConfiguration;
using CK.Ng.Admin.UserManagement;
using CK.Ng.Admin.UserManagement.UserBanned;
using CK.Ng.Admin.UserManagement.UserInvitation;
using CK.Ng.AppIdentity.Enlistment;
using CK.Ng.Cris.AspNet.Authentication;
using CK.Ng.Cris.AspNet.Authentication.Basic;
using CK.Ng.UserProfile.PreferredCulture;
using CK.Ng.UserProfile.UserBanned;
using CK.TypeScript;

namespace CK.Sample.Admin;

/// <summary>
/// The TypeScript package of the back-office: the login page (the Basic form), the private page in the back-office
/// layout whose side bar proposes the user management to an administrator (Res/private-page.t), the user management
/// (the users of the current workspace, their banishment and their invitation), the profile page and the approval page
/// of the System (app-identity/enlist). Every command is handled by Auth3, where the database is, but the approval
/// of a party: the Admin hosts the approval page, it handles it and sends it down to the holder of the pending approval.
/// <para>
/// The commands of the dynamic configuration (the run properties of an application: Auth3's ParallelRunnerCount) are
/// available to the client application, until a page of the dynamic configuration exists.
/// </para>
/// </summary>
[TypeScriptPackage]
[Requires<NgCrisAuthenticationBasicPackage, NgCrisAuthenticationPackage>]
[Requires<UserManagementTSPackage, UserManagementUserBannedPackage, UserManagementUserInvitationPackage>]
[Requires<UserProfilePreferredCulturePackage, UserProfileUserBannedPackage>]
[Requires<NgAppIdentityEnlistmentPackage>]
[RegisterTypeScriptType( typeof( IGetRunPropertiesQCommand ) )]
[RegisterTypeScriptType( typeof( IConfigurePropertyCommand ) )]
public class AdminPackage : TypeScriptPackage
{
}
