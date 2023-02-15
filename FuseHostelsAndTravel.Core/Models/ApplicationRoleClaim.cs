using System;
using Microsoft.AspNetCore.Identity;

namespace FuseHostelsAndTravel.Core.Models
{
    public class ApplicationRoleClaim : Travaloud.Core.Models.ApplicationRoleClaim
    {
        public ApplicationRoleClaim()
        {
            TenantId = "fuse";
        }
    }
}

