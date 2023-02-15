using System;
using Microsoft.AspNetCore.Identity;

namespace FuseHostelsAndTravel.Core.Models
{
    public class ApplicationRole : Travaloud.Core.Models.ApplicationRole
    {
        public ApplicationRole(string name, string description = null, string tenantId = null) : base(name, description, "fuse")
        {
        }
    }
}

