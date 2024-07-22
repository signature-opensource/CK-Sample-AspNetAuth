using CK.DB.User.UserOidc;
using System.Collections.Generic;

namespace CK.Sample.User.UserOidc.App
{
    public interface IUserOidcInfo : CK.DB.User.UserOidc.IUserOidcInfo
    {
        string Username { get; set; }
        string DisplayName { get; set; }
        string? Email { get; set; }
        IList<string> Phones { get; }
    }
}
