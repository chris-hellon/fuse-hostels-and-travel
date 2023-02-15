using System;
using Microsoft.AspNetCore.Identity;

namespace FuseHostelsAndTravel.Core.Models
{
	public class ApplicationUserRole : Travaloud.Core.Models.ApplicationUserRole
    {
		public ApplicationUserRole()
		{
			TenantId = "fuse";
		}

        public ApplicationUserRole(string tenantId, string roleId, string userId) : base("fuse", roleId, userId)
		{

		}
	}
}

