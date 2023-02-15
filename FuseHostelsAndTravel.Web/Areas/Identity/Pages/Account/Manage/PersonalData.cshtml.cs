// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace FuseHostelsAndTravel.Web.Areas.Identity.Pages.Account.Manage
{
    public class PersonalDataModel : BasePageModel
    {
        [TempData]
        public string StatusMessage { get; set; }

        private readonly ILogger<PersonalDataModel> _logger;

        public PersonalDataModel(
            ILogger<PersonalDataModel> logger)
        {
            _logger = logger;
        }

        public async Task<IActionResult> OnGet()
        {
            await base.OnGetDataAsync();

            var user = await UserManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{UserManager.GetUserId(User)}'.");
            }

            return Page();
        }
    }
}
