namespace CK.Sample;

/// <summary>
/// The application's leaf of the user profile Poco family: a single materialization carries the extensions of the
/// feature packages (the preferred culture, the preferred workspace and the groups, the temporary password state, the
/// banishment state). The Backend reads it (see Backend.App's UserQueries), the fronts display it.
/// </summary>
public interface IUserProfile : CK.IO.User.PreferredCulture.IUserProfile,
                                CK.IO.UserProfile.Workspace.IUserProfile,
                                CK.IO.User.UserPassword.Reset.IUserProfile,
                                CK.IO.User.UserBanned.IUserProfile
{
}

/// <summary>
/// The application's leaf of the workspace user Poco family: the banishments (<c>Bans</c>) and the e-mail
/// (<c>Email</c>) in a single materialization (see Backend.App's WorkspaceUsersQueries).
/// </summary>
public interface ICombinedWorkspaceUser : CK.IO.UserManagement.UserBanned.IWorkspaceUser,
                                          CK.IO.UserManagement.UserInvitation.IWorkspaceUser
{
}
