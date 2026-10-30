using CK.Core;
using CK.Ng.Admin.UserManagement;
using CK.Ng.Admin.UserManagement.UserBanned;
using CK.Ng.Admin.UserManagement.UserInvitation;
using CK.Ng.Cris.AspNet.Authentication;
using CK.Ng.Cris.AspNet.Authentication.Basic;
using CK.Ng.UserProfile.PreferredCulture;
using CK.Ng.UserProfile.UserBanned;
using CK.TypeScript;

namespace CK.Sample.Admin;

/// <summary>
/// The TypeScript package of the back-office: the login page (the Basic form), the private page in the back-office
/// layout whose side bar proposes the user management to an administrator (Res/private-page.t), the user management
/// (the users of the current workspace, their banishment and their invitation) and the profile page. Every command is
/// handled by the Backend, where the database is.
/// </summary>
[TypeScriptPackage]
[Requires<NgCrisAuthenticationBasicPackage, NgCrisAuthenticationPackage>]
[Requires<UserManagementTSPackage, UserManagementUserBannedPackage, UserManagementUserInvitationPackage>]
[Requires<UserProfilePreferredCulturePackage, UserProfileUserBannedPackage>]
public class AdminPackage : TypeScriptPackage
{
}
