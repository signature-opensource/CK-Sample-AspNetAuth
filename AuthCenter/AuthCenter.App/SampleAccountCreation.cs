using CK.Core;
using CK.DB.Actor;
using CK.DB.Auth;
using CK.DB.User.UserOidc;
using CK.SqlServer;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CK.Sample.AuthCenter;

/// <summary>
/// Sample automatic account creation for the OpenID Connect schemes. The AuthCenter calls it only for a scheme that
/// allows it (<c>"AllowAccountCreation": true</c>), when the identity is not registered and nobody is logged in:
/// <list type="bullet">
///   <item>the user name is the longest of the <c>preferred_username</c> and <c>name</c> claims, normalized (the
///   characters other than letters, digits, '.', '_' and '-' are replaced by '-'), with a "-2", "-3"... suffix on a clash;</item>
///   <item>the identity is then bound to the new user, that is logged in.</item>
/// </list>
/// This is sample code: a real application typically requires more (an invitation in the <see cref="LoginContext.UserData"/>,
/// a domain of the email, etc.).
/// </summary>
public sealed class SampleAccountCreation : IAutoCreateAccountService
{
    readonly IAuthenticationDatabaseService _db;
    readonly UserTable _users;

    /// <summary>
    /// Initializes a new <see cref="SampleAccountCreation"/>.
    /// </summary>
    /// <param name="db">The authentication database service.</param>
    /// <param name="users">The user table.</param>
    public SampleAccountCreation( IAuthenticationDatabaseService db, UserTable users )
    {
        _db = db;
        _users = users;
    }

    /// <inheritdoc />
    public async ValueTask<ActorLoginResult?> CreateAccountAndLoginAsync( IActivityMonitor monitor, LoginContext context )
    {
        if( context.Payload is not IUserOidcInfo info )
        {
            return context.SetError( "User.AutoRegistration.NotSupported", $"The '{context.Scheme}' scheme cannot create an account." );
        }
        var name = GetUserName( info );
        if( name == null )
        {
            return context.SetError( "User.AutoRegistration.NoUserName", "The identity provider gave no name (the 'preferred_username' and 'name' claims)." );
        }
        using var ctx = new SqlStandardCallContext( monitor );
        var userName = name;
        for( int i = 2; await _users.FindByNameAsync( ctx, userName ) != 0; ++i )
        {
            userName = $"{name}-{i}";
        }
        var userId = await _users.CreateUserAsync( ctx, 1, userName );
        monitor.Info( $"User '{userName}' (#{userId}) created for a '{context.Scheme}' login." );
        // Binds the identity to the new user and logs it in.
        var provider = _db.FindRequiredProvider( context.Scheme, mustHavePayload: false );
        var result = await provider.CreateOrUpdateUserAsync( ctx, 1, userId, info, UCLMode.CreateOnly | UCLMode.WithActualLogin );
        return await _db.ToUserLoginResultAsync( ctx, result.LoginResult );
    }

    static string? GetUserName( IUserOidcInfo info )
    {
        string? best = null;
        foreach( var claim in new[] { "preferred_username", "name" } )
        {
            if( info.TryGetClaim( claim, out var v )
                && v.ValueKind == JsonValueKind.String
                && Normalize( v.GetString() ) is { } n
                && n.Length > (best?.Length ?? 0) )
            {
                best = n;
            }
        }
        return best;
    }

    static string? Normalize( string? s )
    {
        if( string.IsNullOrWhiteSpace( s ) ) return null;
        var b = new StringBuilder( s.Length );
        foreach( var c in s.Trim() )
        {
            b.Append( char.IsLetterOrDigit( c ) || c == '.' || c == '_' || c == '-' ? c : '-' );
        }
        // The column is an nvarchar(255): room is kept for the clash suffix.
        return b.Length > 120 ? b.ToString( 0, 120 ) : b.ToString();
    }
}
