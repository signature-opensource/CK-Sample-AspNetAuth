# CK-Sample-AspNetAuth

A sample of the authentication of CK applications: Cris commands, an Authentication Center, and an Angular
client. It replaces the WebFrontAuth samples.

## Three hosts

| Host | What | Reached by |
|---|---|---|
| [Backend](Backend) | The backend, the Authentication Center of the fronts (its local party allows the "AuthCenter" feature): the database (SQL Server), the login providers (Basic, Oidc, Passkey, Weak), the sessions and the signing keys of the tokens, and the handlers of the fronts' features (the profile, the user management). No HTTP endpoint. | The AppIdentity channel (`tcp:127.0.0.1:37300`). |
| [Front](Front) | The HTTP front: the Angular application and its `/.cris` endpoints on the same origin (`http://localhost:5300`). No database, no login provider: it is a remote of its AuthCenter. Its endpoints execute the session commands (logins, refresh, logout...) whose operations are dispatched to the Backend, and it verifies the tokens with the Backend's keys. | The browser. |
| [Admin](Admin) | The back-office, the second remote of the Backend (`http://localhost:5302`): the user management of CK-Ng-Admin (the users of a workspace, their banishment, their invitation). Like the Front, no database: its commands are dispatched to the Backend. | The browser. |

Each host is three projects (the CK pattern): `*.App` (the model: packages, commands, services), `*.Builder` (it runs
the CK engine after its build: the generated code of the host, the database of the Backend, the TypeScript and the
Angular application of the Front and of the Admin) and `*.Host` (the program).

[Shared/Sample.Shared](Shared/Sample.Shared) is referenced by every `*.App`: its `IWhoAmIQCommand` is a party command
handled by every party (see [the Architecture page](#the-architecture-page)), and it declares the application's Poco
leaves, `IUserProfile` and `ICombinedWorkspaceUser`: they combine the extensions of the feature packages (the preferred
culture, the workspace and the groups, the temporary password, the banishment, the e-mail), the Backend materializes
them and the fronts read them, so every party knows the same Poco.

A party is named after what it is in the application (the Backend, the Front, the Admin), not after a feature it
implements: the Backend is the AuthCenter of the fronts, and it is also where the database is.

The role of each host is its package (CK.AppIdentity.Cris.AuthCenter.Local or .Remote) and they allow the
"AuthCenter" feature in their "CK-AppIdentity" configuration: the Backend in its `Local` section (it is its own
AuthCenter, its settings are `Local:AuthCenter`) and on the `$Front` and `$Admin` parties (its remotes); the Front and
the Admin in their `Local` section and on the `$Backend` remote (their AuthCenter).

The Backend references CK.DB.AppIdentity, the PartyActor registrar: every party (the Backend and its remotes) gets
its PartyActorId, that identifies it as the caller of the commands it sends.

## The logins

| Scheme | Provider | Demo |
|---|---|---|
| `Basic` | CK.DB.User.UserPassword | `Albert` / `pass` (the `Demo:AlbertPassword` setting). Critical level (a password is a fresh proof). |
| `Passkey` | CK.DB.User.UserPasskey | Logged in as `Albert` (Critical level), add a passkey in the "Sign-in methods" tab of the profile, then use the passkey button of the login page. Critical level (the user is verified by the authenticator). |
| `Weak` | CK.DB.Actor.WeakActor | `Operator`: passwordless, at the Unsafe level (the application's commands that require Normal refuse it). |
| `Oidc.<Suffix>` | CK.DB.User.UserOidc | `Oidc.Duende` out of the box: "Continue with Duende", then `bob` / `bob` at the Duende demo server (its first login creates the user `Bob-Smith`). Other schemes: see below. An unknown identity creates its user only for a scheme with `AllowAccountCreation` (`SampleAccountCreation`): otherwise, only an identity bound to a user can log in. Critical level only when the provider authenticated the user recently (the `auth_time` of its identity token): a login on an older provider session is Normal. |

The demo data is created at the start of the Backend when missing (`DemoData`): Albert (with his password), the
weak actor Operator, and the workspace "Demo" that Albert administers (the Admin's user management).

The Critical level lasts 5 minutes after the authentication (`Local:AuthCenter:CriticalLifetime` of the Backend's
"CK-AppIdentity" configuration, or per scheme with `Local:AuthCenter:CriticalLifetimes:<scheme>`; `00:00:00` for a scheme that must never elevate). When it ends, the passkey
registration button of the profile offers to log in again: the login page (`/auth?critical`) proposes only the
schemes of the user that can elevate it, and a remote provider must authenticate the user again (`prompt=login`).

## The Angular application

It is made of standard packages: the sample's own code is minimal.

| Route | What | From |
|---|---|---|
| `/auth` | The login page: the Basic form, the Weak form, a button per OpenID Connect scheme. | CK.Ng.Cris.AspNet.Authentication (+ `.Basic`, `.Weak`) |
| `/` (Normal level at least) | The private page in the back-office layout (side bar, top bar with the user info box and the sign out). Its home says hello. | CK.Ng.Cris.AspNet.Authentication, CK.Ng.Zorro.BackOffice |
| `/` (otherwise) | The public page: the anonymous is invited to log in; a weak actor (Unsafe level, not authenticated) sees who it is and its hello is refused. | CK.Ng.PublicPage |
| `/architecture` (Normal level at least) | Where the commands execute and with which identity, the topology, a console of refused calls: see below. | The sample |
| `/profile` (Normal level at least, the "Profile" item of the user info box) | The profile: the user name, and the "Sign-in methods" tab: the methods of the user and their last use, the password (change, add, remove), the passkeys (list, rename, delete, add) and the linked accounts (what the providers shared, unlink, link another provider). | CK.Ng.UserProfile (+ `.UserPassword`, `.UserPasskey`, `.UserOidc`) |
| `/auth/logout` | The sign out. | CK.Ng.Cris.AspNet.Authentication |

The sample's code ([Front.App](Front/Front.App)): the `IHelloCommand` (Normal level) and its handler,
`HomeComponent` (the hello card, routed in the private page), `PublicHomeComponent`
(routed in the public page), `ArchitectureComponent` and its commands (`Architecture/`), and `Res/private-page.t`, a
transformer that puts the private page in the back-office layout (and adds the Architecture item to its side bar).

The commands of the profile (`IGetUserProfileQCommand`, `IUpdateUserCommand`, the self-service on the login methods:
`IGetPasskeysQCommand`, `IAddPasswordCommand`, `IUnlinkOidcIdentityCommand`...) are handled by the AuthCenter: the
browser posts them to the Front's `/.cris`, that dispatches them with the user's proof (the token and its binding
secret), and the Backend verifies this proof. The profile's query is the application's
(`Backend.App/User/UserQueries`: the groups and the grant levels, the banishment, the temporary password).

## The Admin application

The back-office of CK-Ng-Admin: the login page (the Basic form), the private page in the back-office layout whose side
bar proposes the user management to an administrator of the current workspace, and the user management
(CK.Ng.Admin.UserManagement, `.UserBanned`, `.UserInvitation`). Its commands are handled by the Backend
(CK.UserManagement and its packages, and the application's listing of the workspace users:
`Backend.App/WorkspaceUsers`). Log in as `Albert` / `pass`: he administers the "Demo" workspace.

The mails (an invitation, a lost password) are written in the Backend's pickup directory (`Mails`, nothing is sent)
and their links open the Admin (`Local:FrontUrl`): it has the registration and the password recovery pages.

## The Architecture page

`/architecture` shows the location transparency of the Cris commands and the identity across the parties:

| Card | What it shows |
|---|---|
| Here and there | `IWhoAmIQCommand` (Shared) is a party command (`ICommandTargetParty`): sent to the Front or to the Backend by the same code, it executes where its target is. At the Backend, the user's proof is verified: the level is kept, Critical included, and the Front is the caller party. |
| Later | `IAskLaterQCommand`: the Front asks the same question in its background (like a deferred work). There is no user's proof: the Front only vouches for the user, and the Backend grants its remotes the Normal level at most. |
| Topology | `IGetTopologyQCommand`: the Front's party and PartyActorId, its remotes with their PartyActorId, their connection and the commands they accept from the Front (their advertisement: the routes of the dispatch). |
| Refused and protected calls | A little request console: predefined raw requests (an `[Internal]` operation on `/.cris/net`, a login on `/.cris`, a command for another actor, a plain text POST...) that can be edited and sent, with or without the bearer token. It dumps the HTTP status and the body of the answer. |

## Running it

Requires SQL Server (the Backend's database is `CKSampleBackend` on `.`), Node and Yarn.

**`Backend/Start.ps1`** does it all: it builds the hosts (restoring the CK packages from the local CKli feed when it
exists) and the Angular applications, starts the Backend then the Front and the Admin (each in its own window),
waits until they reach their AuthCenter, opens the browser on the Front and stops the hosts when Enter is pressed (`-SkipBuild` starts what is
already built, `-NoBrowser` does not open the browser). By hand:

1. Build the solution: the builders generate the code of the hosts (and the database).
2. Build the Angular applications: in `Front/Front.Host/Clients/App` and in `Admin/Admin.Host/Clients/App`, `yarn install`
   then `yarn build`.
3. Start `Backend.Host`, then `Front.Host` and `Admin.Host`, and open `http://localhost:5300`: log in as `Albert` / `pass`
   (Basic), as `Operator` (Weak), or with "Continue with Duende" then `bob` / `bob` (OpenID Connect, logs in as `Bob-Smith`,
   created at the first login). The Admin is `http://localhost:5302`.

The hosts pin each other's identity at their first connection (`AutoTrustKey: Once`): in production, the keys are
pinned explicitly.

### OpenID Connect

OpenID Connect is a package of its own: the Backend references CK.AppIdentity.Cris.AuthCenter.Oidc (without it, an
"Local:AuthCenter:Oidc" section is a configuration error). The default configuration (`Backend/Backend.Host/appsettings.json`) has the scheme `Oidc.Duende`: the public demo
server of Duende IdentityServer (`https://demo.duendesoftware.com`) with its public test client
(`interactive.confidential` / `secret`, authorization code with PKCE). This secret is published by Duende for testing: it
protects nothing. The scheme allows account creation (`AllowAccountCreation`) and reads the UserInfo endpoint
(`GetClaimsFromUserInfoEndpoint`: the demo server puts the profile claims there only, not in the identity token), with the
scopes `openid profile email`. "Continue with Duende" then `bob` / `bob` creates the user `Bob-Smith` at the first login
(`SampleAccountCreation`: the longest of the `preferred_username` and `name` claims, normalized, suffixed on a clash) and
logs in as this user afterwards. A database created before this change still binds Albert to bob (the former demo
binding): remove its `CK.tUserOidc` row (`SchemeSuffix` "Duende", `Sub` "2") to see the creation. It needs the Internet and depends on a server that we do not control:
no test uses it (the tests use a fake provider).
The logo of its button is the sample's (`Front/Front.App/Res/ts-assets`, mapped to `login-providers/duende.png`): the
login page looks for `login-providers/<suffix>.png`, and an application adds the logos of its own schemes this way.

A real provider (with a real secret) goes in the user-secrets of Backend.Host (`dotnet user-secrets` in
`Backend/Backend.Host`). For Google (the `Demo` keys bind Albert to your account):

```json
{
  "CK-AppIdentity:Local:AuthCenter:Oidc:Google:Authority": "https://accounts.google.com",
  "CK-AppIdentity:Local:AuthCenter:Oidc:Google:ClientId": "...",
  "CK-AppIdentity:Local:AuthCenter:Oidc:Google:ClientSecret": "...",
  "CK-AppIdentity:Local:AuthCenter:Oidc:Google:Scopes": "openid email",
  "CK-AppIdentity:Local:AuthCenter:Oidc:Google:CallbackUrls:0": "http://localhost:5300/.auth/oidc/callback",
  "Demo:OidcScheme": "Google",
  "Demo:OidcSub": "<the sub of your Google account>"
}
```

The callback URL is the Front's (`/.auth/oidc/callback`): register it at the provider. Only the Backend knows the
client secret (it redeems the code); the Front forwards the start of the login and the callback to it.

## The tests

[Tests/Sample.Tests](Tests/Sample.Tests) runs the three hosts in one process (their own ports, a fresh AppIdentity store,
an OpenID Connect scheme on a fake provider) and talks to the front like a browser (an HTTP client that keeps the
cookies): Basic login, refresh and logout, a stolen token without its binding cookie, a wrong password, the Weak
login (refused by a Normal command), an OIDC login (start, provider, callback, refresh), the passkey ceremonies and the
self-service on the passkeys, the profile (the profile query, the linked accounts, the password), the Admin's user
management (the users of the demo workspace), and the scenarios of the Architecture page (here and there, later, a Critical command deferred by the Front and
refused by the Backend, the topology, the refused calls of the console). It needs the database created by the
AuthCenter's builder.

### The UI smoke test

[Front/Front.Host/Clients/App/ui-smoke/ui-smoke.mjs](Front/Front.Host/Clients/App/ui-smoke/ui-smoke.mjs) drives the
built Angular application in a headless Edge or Chrome (DevTools protocol, no dependency): the public page, the login
page, a wrong password, the Weak login (its hello is refused), the Basic login (the private page in the back-office
layout), the "Sign-in methods" tab of the profile (the password, the linked account, the registration of a named passkey
and its renaming), the Architecture page (a command dispatched to the Backend, a refused call of its console), the
user info box, a reload, the end of the Critical level (with `--critical-wait <seconds>`: a new login from the passkey
registration button), the sign out, the passkey login and the deletion of the passkey. It takes a screenshot at each step (and on
failure).

- `yarn smoke` (in `Front/Front.Host/Clients/App`) runs it against the running hosts (`--url`, default
  `http://localhost:5300`; `--oidc <Suffix>` also checks the button of an OpenID Connect scheme; the browser is found at
  its usual places, or `--browser` / `CK_BROWSER`).
- `the_Angular_application_passes_the_UI_smoke_test_Async` (Sample.Tests) runs it against the hosts of the tests. It is
  **ignored** when the application is not built (`yarn build`) or without Node or a browser. The screenshots are in
  `Tests/Sample.Tests/UISmoke`. It tests the built `dist`: rebuild the application after a change.
