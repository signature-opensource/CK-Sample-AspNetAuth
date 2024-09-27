using CK.AspNet.Auth;
using CK.Auth;
using CK.Core;
using CK.DB.Actor;
using CK.DB.Auth;
using CK.DB.User.UserOidc;
using CK.SqlServer;
using CK.DB.Actor.ActorEMail;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System;

namespace CK.Sample.User.UserOidc.App.Auth
{
    public class WebFrontAuthAutoCreateAccountService : IWebFrontAuthAutoCreateAccountService
    {
        readonly UserTable _userTable;
        readonly IAuthenticationTypeSystem _authenticationTypeSystem;
        readonly IAuthenticationDatabaseService _authenticationDatabaseService;
        readonly UserOidcTable _userOidcTable;
        readonly ActorEMailTable _actorEMailTable;
        readonly GroupTable _groupTable;

        public WebFrontAuthAutoCreateAccountService( UserTable userTable,
                                                     IAuthenticationTypeSystem authenticationTypeSystem,
                                                     IAuthenticationDatabaseService authenticationDatabaseService,
                                                     UserOidcTable userOidcTable,
                                                     ActorEMailTable actorEMailTable,
                                                     GroupTable groupTable )
        {
            _userTable = userTable;
            _authenticationTypeSystem = authenticationTypeSystem;
            _authenticationDatabaseService = authenticationDatabaseService;
            _userOidcTable = userOidcTable;
            _actorEMailTable = actorEMailTable;
            _groupTable = groupTable;
        }

        public async Task<UserLoginResult?> CreateAccountAndLoginAsync( IActivityMonitor monitor, IWebFrontAuthAutoCreateAccountContext context )
        {
            ISqlCallContext ctx = context.HttpContext.RequestServices.GetRequiredService<ISqlCallContext>();
            if( context.InitialScheme == "Oidc.Signature" )
            {
                IUserOidcInfo userOidcInfo = (IUserOidcInfo)context.Payload;

                if( userOidcInfo.Username.EndsWith( "signature.one", StringComparison.Ordinal ) )
                {
                    // Create user
                    int userId = await _userTable.CreateUserAsync( ctx, 1, userOidcInfo.Username );

                    // Add the user to signature code group ( by design 4 is Signature Code group id )
                    await _groupTable.AddUserAsync( ctx, 1, 4, userId );

                    // Associate OpenID Sub
                    await _userOidcTable.CreateOrUpdateOidcUserAsync( ctx, 1, userId, userOidcInfo, UCLMode.CreateOnly );

                    // Associate e-mail from Username
                    await _actorEMailTable.AddEMailAsync( ctx, 1, userId, userOidcInfo.Email ?? userOidcInfo.Username, true, true );

                    // Read user
                    var userAuthInfo = await _authenticationDatabaseService.ReadUserAuthInfoAsync( ctx, 1, userId );
                    var userInfo = _authenticationTypeSystem.UserInfo.FromUserAuthInfo( userAuthInfo );

                    // Successful login
                    return new UserLoginResult( userInfo, 0, null, false );
                }
                monitor.Warn( $"""
                               Auto creation of account is for scheme '{context.InitialScheme}' is limited to users with '@signature.one' mail.
                               User '{userOidcInfo.Username}' must be explicitly registered.
                               """ );
            }
            else if( context.InitialScheme == "Oidc.Google" )
            {
                IUserOidcInfo userOidcInfo = (IUserOidcInfo)context.Payload;
                // Create user
                int userId = await _userTable.CreateUserAsync( ctx, 1, userOidcInfo.Username );

                // Add the user to signature code group ( by design 4 is Signature Code group id )
                await _groupTable.AddUserAsync( ctx, 1, 4, userId );

                // Associate OpenID Sub
                await _userOidcTable.CreateOrUpdateOidcUserAsync( ctx, 1, userId, userOidcInfo, UCLMode.CreateOnly );

                // Associate e-mail from Username
                await _actorEMailTable.AddEMailAsync( ctx, 1, userId, userOidcInfo.Email ?? userOidcInfo.Username, true, true );

                // Read user
                var userAuthInfo = await _authenticationDatabaseService.ReadUserAuthInfoAsync( ctx, 1, userId );
                var userInfo = _authenticationTypeSystem.UserInfo.FromUserAuthInfo( userAuthInfo );

                // Successful login
                return new UserLoginResult( userInfo, 0, null, false );
            }
            monitor.Warn( $"{context.InitialScheme}: Account does not exist. Login failed." );
            return new UserLoginResult( null,
                                        1,
                                        $"Local account was not found, and auto-create is disabled for scheme {context.InitialScheme}.",
                                        false );
        }
    }
}
