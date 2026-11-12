namespace CK.Sample;

/// <summary>
/// The application's leaf of the user profile Poco family: a single materialization carries the extensions of the
/// feature packages (the preferred culture, the preferred workspace and the groups, the temporary password state, the
/// banishment state, whether the user is a platform administrator: the isAdmin signal of CK.Ng.Admin). Auth3 reads it (see Auth3.App's UserQueries), the fronts display it.
/// </summary>
public interface IUserProfile : CK.IO.User.PreferredCulture.IUserProfile,
                                CK.IO.UserProfile.Workspace.IUserProfile,
                                CK.IO.User.UserPassword.Reset.IUserProfile,
                                CK.IO.User.UserBanned.IUserProfile,
                                CK.IO.Admin.IUserProfile
{
}

/// <summary>
/// The application's leaf of the workspace user Poco family: the banishments (<c>Bans</c>) and the e-mail
/// (<c>Email</c>) in a single materialization (see Auth3.App's WorkspaceUsersQueries).
/// </summary>
public interface ICombinedWorkspaceUser : CK.IO.UserManagement.UserBanned.IWorkspaceUser,
                                          CK.IO.UserManagement.UserInvitation.IWorkspaceUser
{
}
