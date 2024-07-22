using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace UserBasicGoogleFacebook.App.Auth
{
    [Route( "/api/config" )]
    public class ConfigController : Controller
    {
        private IConfiguration _configuration { get; set; }

        public ConfigController(
            IConfiguration configuration
        )
        {
            _configuration = configuration;
        }

        [HttpGet( "getAppSettings" )]
        public JsonResult GetAppSettings()
        {
            return Json( _configuration.ToString() );
        }
    }
}
