using System;
using Microsoft.AspNetCore.Identity;

namespace FuseHostelsAndTravel.Core.Models
{
	public class ApplicationUserToken : Travaloud.Core.Models.ApplicationUserToken
    {
        public ApplicationUserToken()
		{
			TenantId = "fuse";
		}
	}
}

