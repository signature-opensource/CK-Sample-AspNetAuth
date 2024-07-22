# CK-Sample-AspNetAuth
This project is a sample meant to be an exemple on how to implement the package CK.DB.User.UserOidc and other providers.

## Installation
Please install these before running the project :
- SQL Server (2017 or +)
- Node
- Npm

## Setup
- Clone the repository
- Create an [AAD oidc app](https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/CreateApplicationBlade/quickStartType//isMSAApp/), to properly setup your app do the following steps :
  - Set a redirect URI that match your C# client
  - In the Authentication section:
    - The tokens you would like to be issued by the authorization endpoint has to be ID tokens
  - In the Certificates & secrets section:
    - Create a new secret
  - In token configuration section add those optional claims:
    - email
    - family_name
    - given_name
    - preferred_username
    - upn
    - verified_primary_email
  - In the API permissions section make sure you have those persmissions for the Microsoft Graph API:
    - email
    - openid
    - profile
    - User.Read
- Build the solution
- Start the C# project
- Go to ../Clients/WFATester the run the following commands to start the client ```yarn install``` then ```yarn start```

## WFA tester standalone use
In order to only use the WFA tester here is what you can do :
- First you'll need to globally install the package [http-server](https://www.npmjs.com/package/http-server) ```npm i -g http-server```
- Then go in the WFATester folder and build the project by doing ```yarn run build```
- Move to your new dist folder and make ```http-server . -p 9000```
