# CK-Sample-AspNetAuth

A sample of the authentication of CK applications: Cris commands, an Authentication Center, and an Angular
client. It replaces the WebFrontAuth samples.

## Five hosts

| Host | What | Reached by |
|---|---|---|
| [Backend](Backend) | The backend, the Authentication Center of the fronts (its local party allows "AuthCenter"): the database (SQL Server), the login providers (Basic, Oidc, Passkey, Weak), the sessions and the signing keys of the tokens, and the handlers of the fronts' features (the profile, the user management). A console application (the .NET generic host): no ASP.NET Core, no HTTP endpoint. | The AppIdentity channel (`tcp:127.0.0.1:37300`). |
| [Front](Front) | The HTTP front: the Angular application and its `/.cris` endpoints on the same origin (`http://localhost:5300`). No database, no login provider: it is a remote of its AuthCenter. Its endpoints execute the session commands (logins, refresh, logout...) whose operations are dispatched to the Backend, and it verifies the tokens with the Backend's keys. A .NET generic host: its web server is the "HttpEndpoint" feature of its party (CK.AppIdentity.HttpEndpoint), the `Main` endpoint of its `Local:HttpEndpoint` configuration. | The browser. |
| [Admin](Admin) | The back-office, the second remote of the Backend (`http://localhost:5302`): the user management of CK-Ng-Admin (the users of a workspace, their banishment, their invitation) and the approval page of the System (see [the enlistment](#the-enlistment)). Like the Front, no database (its commands are dispatched to the Backend), and a .NET generic host whose web server is its "HttpEndpoint" feature. | The browser. |
| [Worker](Worker) | A party that must be enlisted: the Backend never trusts its key automatically, an operator approves it on the Admin (see [the enlistment](#the-enlistment)). No database, no web server: a .NET generic host. | Nobody: it connects to the Backend. |
| [Worker2](Worker2) | The same, with a trust anchor of the Backend (its AuthCenter) instead of a trust on first use: it receives an authenticated enlistment URL (see [the enlistment](#the-enlistment)). | Nobody: it connects to the Backend. |

Each host is three projects (the CK pattern): `*.App` (the model: packages, commands, services), `*.Builder` (it runs
the CK engine after its build: the generated code of the host, the database of the Backend, the TypeScript and the
Angular application of the Front and of the Admin) and `*.Host` (the program). The five hosts are the same: their
`Program.cs` is `await CK.Core.CKomposableAppHost.RunAsync( args );` and their project references
[CK.AppHost](https://github.com/signature-opensource/CK-AspNet/blob/stable/CK.AppHost/README.md) (the bootstrap and the
conventions of a host project), their `*.App` and their `*.Builder`. What a host does is in its packages, its `*.App` and
its `appsettings.json`.

[Shared/Sample.Shared](Shared/Sample.Shared) is referenced by every `*.App`: its `IWhoAmIQCommand` is a party command
handled by every party (see [the Architecture page](#the-architecture-page)), and it declares the application's Poco
leaves, `IUserProfile` and `ICombinedWorkspaceUser`: they combine the extensions of the feature packages (the preferred
culture, the workspace and the groups, the temporary password, the banishment, the e-mail), the Backend materializes
them and the fronts read them, so every party knows the same Poco.

A party is named after what it is in the application (the Backend, the Front, the Admin), not after a feature it
implements: the Backend is the AuthCenter of the fronts, and it is also where the database is.

The AuthCenter is an asymmetric feature: each host references its side (CK.AppIdentity.AuthCenter.Local or .Remote),
and its "CK-AppIdentity" configuration gives the role of its party: `"AuthCenter"` in the `Local` section of the party
that is the AuthCenter, `"AuthCenterRemote"` on a remote. The Backend allows `"AuthCenter"` in its `Local` section (it is
the AuthCenter, its settings are `Local:AuthCenter`) and `"AuthCenterRemote"` on the `$Front` and `$Admin` parties (they
use it); the Front and the Admin don't allow `"AuthCenter"` in their `Local` section and allow `"AuthCenterRemote"` on the
`$Backend` party (it is their AuthCenter).

A party is identified by its full name across the System: it is the caller party of the commands it sends
(`AuthenticationInfo.CallerPartyFullName`). The Backend maps it to an actor where its database needs one (the issuing
party of an invitation, CK.DB.AppIdentity).

## The logins

| Scheme | Provider | Demo |
|---|---|---|
| `Basic` | CK.DB.User.UserPassword | `Albert` / `pass` (the `Demo:AlbertPassword` setting). Critical level (a password is a fresh proof). |
| `Passkey` | CK.DB.User.UserPasskey | Logged in as `Albert` (Critical level), add a passkey in the "Sign-in methods" tab of the profile, then use the passkey button of the login page. Critical level (the user is verified by the authenticator). |
| `Weak` | CK.DB.Actor.WeakActor | `Operator`: passwordless, at the Unsafe level (the application's commands that require Normal refuse it). |
| `Oidc.<Suffix>` | CK.DB.User.UserOidc | `Oidc.Duende` out of the box: "Continue with Duende", then `bob` / `bob` at the Duende demo server (its first login creates the user `Bob-Smith`). Other schemes: see below. An unknown identity creates its user only for a scheme with `AllowAccountCreation` (`SampleAccountCreation`): otherwise, only an identity bound to a user can log in. Critical level only when the provider authenticated the user recently (the `auth_time` of its identity token): a login on an older provider session is Normal. |

The demo data is created at the start of the Backend when missing (`DemoData`, a real object of `Backend.App`: its
`OnHostStartAsync`): Albert (with his password), the
weak actor Operator, and the workspace "Demo" that Albert administers (the Admin's user management).

The Critical level lasts 5 minutes after the authentication (`Local:AuthCenter:CriticalLifetime` of the Backend's
"CK-AppIdentity" configuration, or per scheme with `Local:AuthCenter:CriticalLifetimes:<scheme>`; `00:00:00` for a scheme that must never elevate). When it ends, the passkey
registration button of the profile (and every section of the "Sign-in methods" tab) offers to log in again: the login
page (`/auth?critical`), in a popup (the profile stays on its tab), proposes only the schemes of the user that can elevate
it, and a remote provider must authenticate the user again (`prompt=login`).

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
and their links open the client application of the party through which the request was made: the Admin, where the
registration and the password recovery pages are. The Backend knows the URL of each party: the Front and the Admin
announce their public URL (the one of their endpoint) and their client applications when they connect.

## The Architecture page

`/architecture` shows the location transparency of the Cris commands and the identity across the parties:

| Card | What it shows |
|---|---|
| Here and there | `IWhoAmIQCommand` (Shared) is a party command (`ICommandTargetParty`): sent to the Front or to the Backend by the same code, it executes where its target is. At the Backend, the user's proof is verified: the level is kept, Critical included, and the Front is the caller party. |
| Later | `IAskLaterQCommand`: the Front asks the same question in its background (like a deferred work). There is no user's proof: the Front only vouches for the user, and the Backend grants its remotes the Normal level at most. |
| Topology | `IGetTopologyQCommand`: the Front's party, its remotes, their connection and the commands they accept from the Front (their advertisement: the routes of the dispatch). |
| Refused and protected calls | A little request console: predefined raw requests (an `[Internal]` operation on `/.cris/net`, a login on `/.cris`, a command for another actor, a plain text POST...) that can be edited and sent, with or without the bearer token. It dumps the HTTP status and the body of the answer. |

## The enlistment

A party that a holder doesn't trust waits to be enlisted: an operator approves it on the approval page of the System,
with the code that the waiting party shows (see
[CK.AppIdentity.Enlistment](https://github.com/signature-opensource/CK-AppIdentity-Transport/blob/stable/CK.AppIdentity.Enlistment/README.md)).

- **The Worker waits.** The Backend declares `$Worker` with `"AutoTrustKey": "Never"`: it refuses the Worker's key until an
  operator approves it. The Worker trusts the Backend at its first contact (`"Once"`): the Backend is where it is
  configured to go.
- **Worker2 anchors the AuthCenter.** It designates the Backend as its AuthCenter (`"Local:AuthCenterParty"`) and anchors
  it (`"Local:TrustAnchors"`: `"#Dev"`, the checkpoint of the Backend's key event log read from its running status in
  `#Dev/Store`): no trust on first use, the Backend is verified from its anchor. The
  Backend refuses Worker2 until an operator approves it, like the Worker. The difference is the enlistment URL: the
  AuthCenter states the approval page of the System (CK.AppIdentity.AuthCenter.Enlistment), Worker2 verifies the
  statement with its anchor, so its URL is authenticated (`PeeringIssue.IsEnlistUrlAuthenticated`): a fake page can't be
  handed to it. The Worker reads the same URL, not authenticated.
- **The Admin has the approval page.** Its client application references
  [CK.Ng.AppIdentity.Enlistment](https://github.com/signature-opensource/CK-Ng-Admin/blob/stable/CK.Ng.AppIdentity.Enlistment/README.md):
  the Admin hosts the "Enlistment" route of the System, and the pending approval of the Backend travels the route up to
  it. The Admin handles the approval itself (CK.AppIdentity.Enlistment: the operator at the Critical level) and sends it
  down to the Backend. The approval is countersigned by the AuthCenter (the Backend) in the operator's request
  (CK.AppIdentity.AuthCenter.Enlistment), and the Backend requires the countersignature.
- **The demo:** `#Dev/Start.ps1` prints what the Workers wait for ("'Sample/Auth3/$Backend/#Dev' must accept this party:
  open http://localhost:5302/app-identity/enlist?party=..., the approval code is ..."). Open the link, sign in as
  `Albert` / `pass` and enter the code: the page shows "Approved" for the Backend, and the Worker connects (the same
  for Worker2). Rather than
  following a link, an operator can open the approval page itself (`http://localhost:5302/app-identity/enlist`): once
  signed in, it lists the parties that wait for an approval.
- **No right is checked:** any user at the Critical level on the Admin can approve a party. An application restricts it
  with a validator of `IApprovePartyCommand`.
- **To replay it,** delete `#Dev/Store`: the Backend forgets the Workers' keys (and every identity is reset).

`SampleTests` runs it end to end: the Worker learns its URL and its code, the operator approves it on the Admin; Worker2
learns an authenticated URL and is approved the same way.

## Running it

Requires SQL Server (the Backend's database is `CKSampleBackend` on `.`), Node and Yarn.

Build the solution once (`dotnet build`): the builders generate the code of the hosts, the Angular code (and install
the packages of the Angular applications) and the database. The build of the hosts also fills the `#Dev` folder (see
[CK.AppIdentity.Hosting](https://github.com/signature-opensource/CK-AppIdentity/tree/stable/CK.AppIdentity.Hosting#the-dev-folder-of-a-development-solution)):
their manifests (`#Dev/Parties`) and the launcher **`#Dev/Start.ps1`**. It runs the demo in Development:
- it builds the hosts;
- it starts the Backend, the Front, the Admin, the Worker and Worker2, each in its own window, with `DOTNET_ENVIRONMENT=Development`;
- it waits until they are ready (their outgoing remotes are connected, or wait for an approval) and their client
  applications are served (their dev servers compile them): each host writes its running status in `#Dev/Store`;
- it prints their URLs and what an operator must do (the approval of the Workers), and opens the browser on the Front;
- when Enter is pressed, it stops the hosts like Ctrl+C in their window: each one stops the dev servers it started.

`-SkipBuild` starts what is already built, `-NoBrowser` does not open the browser. By hand:

1. Build the solution.
2. Out of Development, build the Angular applications: `yarn build` in `Front/Front.Host/Clients/App`,
   `Front/Front.Host/Clients/MiniApp` and `Admin/Admin.Host/Clients/App` (in Development, the dev servers compile them).
3. Start `Backend.Host`, then `Front.Host` and `Admin.Host`, and open `http://localhost:5300`: log in as `Albert` / `pass`
   (Basic), as `Operator` (Weak), or with "Continue with Duende" then `bob` / `bob` (OpenID Connect, logs in as `Bob-Smith`,
   created at the first login). The Admin is `http://localhost:5302`. `Worker.Host` and `Worker2.Host` log their enlistment URL
   and their approval code (see [the enlistment](#the-enlistment)).

### The client applications

The Angular applications are the client applications of their host (`<TypeScript Name="..." Kind="...">` in
`Front/Front.Builder/CKSetup.xml` and in `Admin/Admin.Builder/Program.cs`), served by CK.TS.AspNet.Clients (its
`EnableClients`, applied by the "HttpEndpoint" feature of the host):

| Host | Client application | Request path | Dev server port |
|---|---|---|---|
| Front | `Clients/App` | `/` | 4200 |
| Front | `Clients/MiniApp` (no authentication, sends the `IFrontInfoQCommand`) | `/mini-app/` | 4201 |
| Admin | `Clients/App` | `/` | 4300 |

Out of Development, a host serves the built files (`dist`): build them first (`yarn build`). In Development
(`#Dev/Start.ps1`, the launch profiles of the hosts), a host starts the dev server of its client applications (`ng serve` on
their port, stopped with the host; one already running is reused) and forwards to it: the application reloads on a
change, on the origin of the host (the authentication requires it). The host configuration `Clients:UseDist` (all the
client applications) or `Clients:{Name}:UseDist` (one of them) set to `true` serves their built files in Development too.

The hosts pin each other's identity at their first connection (`AutoTrustKey: Once`): in production, the keys are
pinned explicitly.

The AppIdentity store of the hosts (their identity keys, the pinned remotes, the sessions) is in the `#Dev` folder of the
solution (`#Dev/Store`): in Development, a host that doesn't configure its `StoreRootPath` uses it (a private store).
`#Dev` holds the development data of the solution, created by the build of the hosts: its content is ignored by git.
Deleting `#Dev/Store` resets the identities of the hosts.

### OpenID Connect

OpenID Connect is a package of its own: the Backend references CK.AppIdentity.AuthCenter.Oidc (without it, an
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
  "Demo:OidcScheme": "Google",
  "Demo:OidcSub": "<the sub of your Google account>"
}
```

The callback URL is the Front's (`/.auth/oidc/callback`): register it at the provider. The Backend configures no
callback URL: it accepts the ones on the public URL that the Front announces. Only the Backend knows the
client secret (it redeems the code); the Front forwards the start of the login and the callback to it.

### Behind a reverse proxy

The demo runs without a proxy: the browser talks to the Front and the Admin directly. In a deployment, a reverse proxy
usually terminates TLS in front of them: the browser is on `https://front.example.com`, the party receives `http`
requests from the proxy. Two keys of the party's configuration handle it, for example in `Front/Front.Host/appsettings.json`:

```jsonc
"Local": {
  "AllowFeatures": "HttpEndpoint",
  // The URL that the party gives: the links of the mails, the passkey origins and the OIDC callbacks that the Backend
  // accepts are derived from it.
  "PublicUrl": "https://front.example.com",
  "HttpEndpoint": {
    "Main": {
      "Urls": "http://0.0.0.0:5300",
      // The client's scheme, host and address, from the headers of the proxy (its address: nothing is trusted by default).
      "ForwardedHeaders": { "KnownProxies": "10.0.0.12" }
    }
  }
}
```

Without the forwarded headers, the party sees the proxy's request (`http://internal-host`): a remote login (OIDC) starts
with the internal callback URL, that the Backend refuses (it is not the public URL of the Front), the session cookies are
not `Secure` when the internal host is a loopback, and a browser without fetch metadata is refused by the same origin
check. See [the options of an endpoint](https://github.com/signature-opensource/CK-AspNet/blob/stable/CK.AppIdentity.HttpEndpoint/README.md#the-forwarded-headers)
(`KnownNetworks`, `Headers`, `ForwardLimit`). The Admin is configured the same way, with its own public URL.

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
registration button, in a popup), the sign out, the passkey login and the deletion of the passkey. It takes a screenshot at each step (and on
failure).

- `yarn smoke` (in `Front/Front.Host/Clients/App`) runs it against the running hosts (`--url`, default
  `http://localhost:5300`; `--oidc <Suffix>` also checks the button of an OpenID Connect scheme; the browser is found at
  its usual places, or `--browser` / `CK_BROWSER`).
- `the_Angular_application_passes_the_UI_smoke_test_Async` (Sample.Tests) runs it against the hosts of the tests. It is
  **ignored** when the application is not built (`yarn build`) or without Node or a browser. The screenshots are in
  `Tests/Sample.Tests/UISmoke`. It tests the built `dist`: rebuild the application after a change.
