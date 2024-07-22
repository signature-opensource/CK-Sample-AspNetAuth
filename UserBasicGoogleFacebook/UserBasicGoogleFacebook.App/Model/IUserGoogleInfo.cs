using System;
using System.Collections.Generic;
using System.Text;

namespace UserBasicGoogleFacebook.App
{
    /// <summary>
    /// We use both default "profile" scope and "email".
    /// </summary>
    public interface IUserGoogleInfo : CK.DB.User.UserGoogle.EMailColumns.IUserGoogleInfo
    {
        string FirstName { get; set; }
        string LastName { get; set; }
        string UserName { get; set; }
        string PictureUrl { get; set; }
    }

}
