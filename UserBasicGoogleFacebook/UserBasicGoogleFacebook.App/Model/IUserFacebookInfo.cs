using System;
using System.Collections.Generic;
using System.Text;

namespace UserBasicGoogleFacebook.App.Model
{
    public interface IUserFacebookInfo : CK.DB.User.UserFacebook.IUserFacebookInfo
    {
        string EMail { get; set; }
        string FirstName { get; set; }
        string LastName { get; set; }
        string UserName { get; set; }
    }

}
