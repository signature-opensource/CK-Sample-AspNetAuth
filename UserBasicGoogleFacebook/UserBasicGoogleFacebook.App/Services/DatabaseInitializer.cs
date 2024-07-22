using CK.Core;
using CK.DB.Actor;
using CK.DB.Auth;
using CK.DB.User.UserPassword;
using CK.SqlServer;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UserBasicGoogleFacebook.App
{
    public class DatabaseInitializer : IRealObject
    {
        async Task OnHostStartAsync( ISqlCallContext ctx, UserTable user, UserPasswordTable password )
        {
            await CreateSimpleUserAsync( ctx, user, password, "Spencer", "pwd" );
            await CreateSimpleUserAsync( ctx, user, password, "Clémence", "pwd" );
            await CreateSimpleUserAsync( ctx, user, password, "Romain", "pwd" );
            await CreateSimpleUserAsync( ctx, user, password, "Julien", "pwd" );
            await CreateSimpleUserAsync( ctx, user, password, "Olivier", "pwd" );

            await EnsureSystemUserHasPasswordAsync( ctx, password );

        }

        static async Task CreateSimpleUserAsync( ISqlCallContext ctx, UserTable user, UserPasswordTable password, string userName, string pwd )
        {
            // Create the user if the user already exist userId is equal to -1.
            int userId = await user.CreateUserAsync( ctx, 1, userName );
            // Set the password of the first user if the user has just been created.
            if( userId != -1 )
            {
                password.CreateOrUpdatePasswordUser( ctx, userId, userId, pwd, UCLMode.CreateOnly );
            }
        }

        static async Task EnsureSystemUserHasPasswordAsync( ISqlCallContext ctx, UserPasswordTable password )
        {
            // Generate Guid then try to create the password for the user "System".
            string systemPassword = Guid.NewGuid().ToString();
            UCLResult creationResult = await password.CreateOrUpdatePasswordUserAsync( ctx, 1, 1, systemPassword, UCLMode.CreateOnly );
            // If the operation result is "Created" then we write the Guid in a txt file at the root of the WebHost folder.
            if( creationResult.OperationResult == UCResult.Created )
            {
                string destPath = Path.Combine( Environment.CurrentDirectory, "SystemPassword.txt" );
                await File.WriteAllTextAsync( destPath, systemPassword.ToString() );
            }
        }
    }
}
