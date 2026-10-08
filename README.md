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
| `Oidc.<Suffix>` | CK.DB.User.UserOidc | Configured on the AuthCenter (see below). Only an identity bound to a user can log in: there is no automatic account creation. |

The demo data is created at the start of the AuthCenter when missing (`DemoData`).

## Running it

Requires SQL Server (the AuthCenter's database is `CKSampleAuthCenter` on `.`), Node and Yarn.

1. Build the solution: the builders generate the code (and the database). The first build of a host may miss its
   generated code (it is created during the build): build it again.
2. Build the Angular application: in `Front/Front.Host/Clients/App`, `yarn install` then `yarn build`.
3. Start `AuthCenter.Host`, then `Front.Host`, and open `http://localhost:5300`.

The two hosts pin each other's identity at their first connection (`AutoTrustKey: Once`): in production, the keys are
pinned explicitly.

### OpenID Connect

In the user-secrets of AuthCenter.Host (`dotnet user-secrets` in `AuthCenter/AuthCenter.Host`), for Google:

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
