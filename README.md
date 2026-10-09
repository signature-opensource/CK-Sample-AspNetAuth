# CK-Sample-AspNetAuth

A sample of the authentication of CK applications: Cris commands, an Authentication Center, and an Angular
client. It replaces the WebFrontAuth samples.

## Two hosts

| Host | What | Reached by |
|---|---|---|
| [AuthCenter](AuthCenter) | The Authentication Center: the database (SQL Server), the login providers (Basic, Oidc, Weak), the sessions and the signing keys of the tokens. No HTTP endpoint. | The AppIdentity channel (`tcp:127.0.0.1:37300`). |
| [Front](Front) | The HTTP front: the Angular application and its `/.cris` endpoints on the same origin (`http://localhost:5300`). No database, no login provider: it relays the session commands (logins, refresh, logout...) to its AuthCenter and verifies the tokens with the AuthCenter's keys. | The browser. |

Each host is three projects (the CK pattern): `*.App` (the model: packages, commands, services), `*.Builder` (it runs
the CK engine after its build: the generated code of the host, the database of the AuthCenter, the TypeScript and the
Angular application of the Front) and `*.Host` (the program).

## The logins

| Scheme | Provider | Demo |
|---|---|---|
| `Basic` | CK.DB.User.UserPassword | `Albert` / `pass` (the `Demo:AlbertPassword` setting). Critical level. |
| `Weak` | CK.DB.Actor.WeakActor | `Operator`: passwordless, at the Unsafe level (the application's commands that require Normal refuse it). |
| `Oidc.<Suffix>` | CK.DB.User.UserOidc | `Oidc.Duende` out of the box: "Continue with Duende", then `bob` / `bob` at the Duende demo server (bound to Albert). Other schemes: see below. Only an identity bound to a user can log in: there is no automatic account creation. |

The demo data is created at the start of the AuthCenter when missing (`DemoData`).

## The Angular application

It is made of standard packages: the sample's own code is minimal.

| Route | What | From |
|---|---|---|
| `/auth` | The login page: the Basic form, the Weak form, a button per OpenID Connect scheme. | CK.Ng.Cris.AspNet.Authentication (+ `.Basic`, `.Weak`) |
| `/` (Normal level at least) | The private page in the back-office layout (side bar, top bar with the user info box and the sign out). Its home says hello. | CK.Ng.Cris.AspNet.Authentication, CK.Ng.Zorro.BackOffice |
| `/` (otherwise) | The public page: the anonymous is invited to log in; a weak actor (Unsafe level, not authenticated) sees who it is and its hello is refused. | CK.Ng.PublicPage |
| `/auth/logout` | The sign out. | CK.Ng.Cris.AspNet.Authentication |

The sample's code ([Front.App](Front/Front.App)): the `IHelloCommand` (Normal level) and its handler,
`HomeComponent` (the hello card, routed in the private page), `PublicHomeComponent` (routed in the public page) and
`Res/private-page.t`, a transformer that puts the private page in the back-office layout.

## Running it

Requires SQL Server (the AuthCenter's database is `CKSampleAuthCenter` on `.`), Node and Yarn.

**`AuthCenter/Start.ps1`** does it all: it builds the hosts (restoring the CK packages from the local CKli feed when it
exists) and the Angular application, starts the AuthCenter then the Front (each in its own window), waits until the
Front reaches its AuthCenter, opens the browser and stops both hosts when Enter is pressed (`-SkipBuild` starts what is
already built, `-NoBrowser` does not open the browser). By hand:

1. Build the solution: the builders generate the code of the hosts (and the database).
2. Build the Angular application: in `Front/Front.Host/Clients/App`, `yarn install` then `yarn build`.
3. Start `AuthCenter.Host`, then `Front.Host`, and open `http://localhost:5300`: log in as `Albert` / `pass` (Basic), as `Operator`
   (Weak), or with "Continue with Duende" then `bob` / `bob` (OpenID Connect, logs in as Albert).

The two hosts pin each other's identity at their first connection (`AutoTrustKey: Once`): in production, the keys are
pinned explicitly.

### OpenID Connect

OpenID Connect is a package of its own: the AuthCenter references CK.AppIdentity.Cris.AuthCenter.Oidc (without it, an
"AuthCenter:Oidc" section is a configuration error). The default configuration (`AuthCenter/AuthCenter.Host/appsettings.json`) has the scheme `Oidc.Duende`: the public demo
server of Duende IdentityServer (`https://demo.duendesoftware.com`) with its public test client
(`interactive.confidential` / `secret`, authorization code with PKCE). This secret is published by Duende for testing: it
protects nothing. The demo data binds Albert to its user `bob` (`Demo:OidcSub` is bob's `sub`, "2"), so "Continue with
Duende" then `bob` / `bob` logs in as Albert. It needs the Internet and depends on a server that we do not control:
no test uses it (the tests use a fake provider).
The logo of its button is the sample's (`Front/Front.App/Res/ts-assets`, mapped to `login-providers/duende.png`): the
login page looks for `login-providers/<suffix>.png`, and an application adds the logos of its own schemes this way.

A real provider (with a real secret) goes in the user-secrets of AuthCenter.Host (`dotnet user-secrets` in
`AuthCenter/AuthCenter.Host`). For Google (the `Demo` keys then bind Albert to your account instead of bob):

```json
{
  "CK-AppIdentity:AuthCenter:Oidc:Google:Authority": "https://accounts.google.com",
  "CK-AppIdentity:AuthCenter:Oidc:Google:ClientId": "...",
  "CK-AppIdentity:AuthCenter:Oidc:Google:ClientSecret": "...",
  "CK-AppIdentity:AuthCenter:Oidc:Google:Scopes": "openid email",
  "CK-AppIdentity:AuthCenter:Oidc:Google:CallbackUrls:0": "http://localhost:5300/.auth/oidc/callback",
  "Demo:OidcScheme": "Google",
  "Demo:OidcSub": "<the sub of your Google account>"
}
```

The callback URL is the Front's (`/.auth/oidc/callback`): register it at the provider. Only the AuthCenter knows the
client secret (it redeems the code); the Front relays the start of the login and the callback.

## The tests

[Tests/Sample.Tests](Tests/Sample.Tests) runs the two hosts in one process (their own ports, a fresh AppIdentity store,
an OpenID Connect scheme on a fake provider) and talks to the front like a browser (an HTTP client that keeps the
cookies): Basic login, refresh and logout, a stolen token without its binding cookie, a wrong password, the Weak
login (refused by a Normal command), and an OIDC login (start, provider, callback, refresh). It needs the database
created by the AuthCenter's builder.

### The UI smoke test

[Front/Front.Host/Clients/App/ui-smoke/ui-smoke.mjs](Front/Front.Host/Clients/App/ui-smoke/ui-smoke.mjs) drives the
built Angular application in a headless Edge or Chrome (DevTools protocol, no dependency): the public page, the login
page, a wrong password, the Weak login (its hello is refused), the Basic login (the private page in the back-office
layout), the user info box, a reload and the sign out. It takes a screenshot at each step (and on failure).

- `yarn smoke` (in `Front/Front.Host/Clients/App`) runs it against the running hosts (`--url`, default
  `http://localhost:5300`; `--oidc <Suffix>` also checks the button of an OpenID Connect scheme; the browser is found at
  its usual places, or `--browser` / `CK_BROWSER`).
- `the_Angular_application_passes_the_UI_smoke_test_Async` (Sample.Tests) runs it against the hosts of the tests. It is
  **ignored** when the application is not built (`yarn build`) or without Node or a browser. The screenshots are in
  `Tests/Sample.Tests/UISmoke`. It tests the built `dist`: rebuild the application after a change.
