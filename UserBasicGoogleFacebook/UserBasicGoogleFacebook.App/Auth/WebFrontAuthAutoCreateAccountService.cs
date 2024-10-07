using CK.AspNet.Auth;
using CK.Auth;
using CK.Core;
using CK.DB.Actor;
using CK.DB.Actor.ActorEMail;
using CK.DB.Auth;
using CK.DB.User.UserFacebook;
using CK.DB.User.UserGoogle;
using CK.DB.User.UserPassword;
using CK.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace UserBasicGoogleFacebook.App.Auth;

public class WebFrontAuthAutoCreateAccountService : IWebFrontAuthAutoCreateAccountService
{
    readonly UserTable _userTable;
    readonly UserGoogleTable _userGoogleTable;
    readonly UserFacebookTable _userFacebookTable;
    readonly UserPasswordTable _userPasswordTable;
    readonly ActorEMailTable _actorEMailTable;
    readonly GroupTable _groupTable;
    readonly IAuthenticationTypeSystem _authenticationTypeSystem;
    readonly IAuthenticationDatabaseService _authenticationDatabaseService;

    public WebFrontAuthAutoCreateAccountService( UserTable userTable,
                                                 UserGoogleTable userGoogleTable,
                                                 UserFacebookTable userFacebookTable,
                                                 ActorEMailTable actorEMailTable,
                                                 GroupTable groupTable,
                                                 IAuthenticationTypeSystem authenticationTypeSystem,
                                                 IAuthenticationDatabaseService authenticationDatabaseService,
                                                 UserPasswordTable userPasswordTable )
    {
        _userTable = userTable;
        _authenticationTypeSystem = authenticationTypeSystem;
        _authenticationDatabaseService = authenticationDatabaseService;
        _actorEMailTable = actorEMailTable;
        _groupTable = groupTable;
        _userGoogleTable = userGoogleTable;
        _userFacebookTable = userFacebookTable;
        _userPasswordTable = userPasswordTable;
    }

    public async Task<UserLoginResult?> CreateAccountAndLoginAsync( IActivityMonitor monitor, IWebFrontAuthAutoCreateAccountContext context )
    {
        ISqlCallContext ctx = context.HttpContext.RequestServices.GetRequiredService<ISqlCallContext>();

        if( context.InitialScheme == "Google" )
        {
            IUserGoogleInfo userGoogleInfo = (IUserGoogleInfo)context.Payload;

            // Create user
            int userId = await CreateUserAsync( ctx, userGoogleInfo.UserName );

            // Associate GoogleAccountId
            await _userGoogleTable.CreateOrUpdateGoogleUserAsync( ctx, 1, userId, userGoogleInfo, UCLMode.CreateOnly );

            // Associate e-mail from Username
            await _actorEMailTable.AddEMailAsync( ctx, 1, userId, userGoogleInfo.EMail ?? userGoogleInfo.UserName, isPrimary: true, validate: true );

            // Read user
            var userAuthInfo = await _authenticationDatabaseService.ReadUserAuthInfoAsync( ctx, 1, userId );
            var userInfo = _authenticationTypeSystem.UserInfo.FromUserAuthInfo( userAuthInfo );

            // Successful login
            return new UserLoginResult( userInfo, 0, null, false );
        }
        else if( context.InitialScheme == "Facebook" )
        {
            Model.IUserFacebookInfo userFacebookInfo = (Model.IUserFacebookInfo)context.Payload;

            // Create user
            int userId = await CreateUserAsync( ctx, userFacebookInfo.UserName );

            // Associate FacebookAccountId
            await _userFacebookTable.CreateOrUpdateFacebookUserAsync( ctx, 1, userId, userFacebookInfo, UCLMode.CreateOnly );

            // Associate e-mail from Username
            await _actorEMailTable.AddEMailAsync( ctx, 1, userId, userFacebookInfo.EMail ?? userFacebookInfo.UserName, true, true );

            // Read user
            var userAuthInfo = await _authenticationDatabaseService.ReadUserAuthInfoAsync( ctx, 1, userId );
            var userInfo = _authenticationTypeSystem.UserInfo.FromUserAuthInfo( userAuthInfo );

            // Successful login
            return new UserLoginResult( userInfo, 0, null, false );
        }
        monitor.Warn( $"{context.InitialScheme}: Account does not exist. Failing login." );
        return new UserLoginResult( null,
                                    1,
                                    $"Local account was not found, and auto-create is disabled for scheme {context.InitialScheme}.",
                                    false );
    }

    public async Task<int> CreateUserAsync( ISqlCallContext ctx, string username )
    {
        // Try to create an user.
        int userId = await _userTable.CreateUserAsync( ctx, 1, username );
        // If an user with the same username already exists, we find an unique one
        // by adding a number at the end.
        if( userId == -1 )
        {
            int count = 0;
            do
            {
                userId = await _userTable.CreateUserAsync( ctx, 1, $"{username}-{++count}" );
            }
            while( userId == -1 );
        }
        return userId;
    }
}
