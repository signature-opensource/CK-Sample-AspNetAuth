<!-- Generated from Auth3.Builder/ckonf.json: do not edit. -->
# The App '$Auth3'

Its run time configuration is `appsettings.json`; this document describes it from its content and its compile time choices.

## The projects

- The App: `../Auth3.Auth3.App`
- The Builder: `../Auth3.Builder`

## The type set

The types of the packages of the App project (discovered from its folder).

## The configurable types by package

| Package | Configurable types |
|---|---|
| CK.AppIdentity | DevRunningStatus |
| CK.AppIdentity.AuthCenter.Local | AuthCenter |
| CK.AppIdentity.AuthCenter.Oidc | Oidc |
| CK.AppIdentity.AuthCenter.Passkey | Passkey |
| CK.AppIdentity.AuthCenter.Password | Password |
| CK.AppIdentity.AuthCenter.Remote | AuthCenterRemote |
| CK.AppIdentity.AuthCenter.Weak | Weak |
| CK.AppIdentity.Cris | CrisChannel |
| CK.AppIdentity.KeyManagement | KeyManagement |
| CK.AppIdentity.TransportLayer | Transport |
| CK.Globalization.StObj | Globalization |
| CK.Mailer | MailSender |
| CK.Mailer.MailKit | MailKit |
| CK.SqlServer.Setup.Model | SqlServer |
| CK.UserManagement.UserInvitation | UserInvitation |
| CK.UserProfile.UserPassword.Lost | PasswordLost |

## The aspects

### AppContext aspect `SqlServer`

Package: `CK.SqlServer.Setup.Model`

| Key | Value |
|---|---|
| DefaultDatabaseConnectionString | *(secret)* |
| Databases | *(contains secrets)* *(default)* |
| GlobalResolution | `false` *(default)* |
| IgnoreMissingDependencyIsError | `false` *(default)* |

## The party '$Auth3'

- Full name: `Sample/Auth3/$Auth3/#Dev`
- Remotes: `$Front`, `$Admin`, `$Worker-1`, `$Worker-2`, `$Worker2`

### Feature `AuthCenter`

Package: `CK.AppIdentity.AuthCenter.Local`

Options: `Oidc`, `Passkey`

### Feature `MailSender`

Package: `CK.Mailer`

Options: `MailKit`
