<!-- Generated from Front.Builder/ckonf.json: do not edit. -->
# The App '$Front'

Its run time configuration is `appsettings.json`; this document describes it from its content and its compile time choices.

## The projects

- The App: `../Auth3.Front.App`
- The Builder: `../Front.Builder`

## The type set

The types of the packages of the App project (discovered from its folder).

## The configurable types by package

| Package | Configurable types |
|---|---|
| CK.AppIdentity | DevRunningStatus |
| CK.AppIdentity.AuthCenter.Remote | AuthCenterRemote |
| CK.AppIdentity.Cris | CrisChannel |
| CK.AppIdentity.HttpEndpoint | ForwardedHeaders, HttpEndpoint |
| CK.AppIdentity.KeyManagement | KeyManagement |
| CK.AppIdentity.PartyClient | PartyClient |
| CK.AppIdentity.TransportLayer | Transport |
| CK.TS.Angular | NgApp |
| CK.TS.AspNet.Clients | Hosting, StaticFiles |

## The party '$Front'

- Full name: `Sample/Auth3/$Front/#Dev`
- Remotes: `$Auth3`

### Feature `HttpEndpoint`

Package: `CK.AppIdentity.HttpEndpoint`

### Feature `PartyClient`

Package: `CK.AppIdentity.PartyClient`

## The client 'App' of '$Front'

### Client feature `NgApp`

Package: `CK.TS.Angular`

Build time:

| Key | Value |
|---|---|
| RequestPath | `/` |
| DevServerPort | `4200` |
| TargetProjectPath | *(none)* *(default)* |
| DefaultTypeScriptVersion | *(none)* *(default)* |
| IntegrationMode | *(none)* *(default)* |
| InstallYarn | *(none)* *(default)* |
| GitIgnoreCKGenFolder | *(none)* *(default)* |
| AutoInstallJest | *(none)* *(default)* |
| EnableTSProjectReferences | *(none)* *(default)* |
| DefaultCulture | *(none)* *(default)* |
| ActiveCultures | *(none)* *(default)* |
| TypeFilterName | *(none)* *(default)* |
| Barrels | *(none)* *(default)* |
| Types | *(none)* *(default)* |

### Client feature `Hosting`

Package: `CK.TS.AspNet.Clients`

## The client 'MiniApp' of '$Front'

- Types: `CK.Ng.Cris.AspNet.CrisAspNetPackage, CK.Ng.Cris.AspNet`, `CK.Sample.Front.IFrontInfoQCommand, Auth3.Front.App`, `CK.TS.Angular.NgAppClientFeature, CK.TS.Angular`, `CK.TypeScript.HostingClientFeature, CK.TS.AspNet.Clients`

### Client feature `NgApp`

Package: `CK.TS.Angular`

Build time:

| Key | Value |
|---|---|
| RequestPath | *(none)* *(default)* |
| DevServerPort | `4201` |
| TargetProjectPath | *(none)* *(default)* |
| DefaultTypeScriptVersion | *(none)* *(default)* |
| IntegrationMode | *(none)* *(default)* |
| InstallYarn | *(none)* *(default)* |
| GitIgnoreCKGenFolder | *(none)* *(default)* |
| AutoInstallJest | *(none)* *(default)* |
| EnableTSProjectReferences | *(none)* *(default)* |
| DefaultCulture | *(none)* *(default)* |
| ActiveCultures | *(none)* *(default)* |
| TypeFilterName | *(none)* *(default)* |
| Barrels | *(none)* *(default)* |
| Types | *(none)* *(default)* |

### Client feature `Hosting`

Package: `CK.TS.AspNet.Clients`
