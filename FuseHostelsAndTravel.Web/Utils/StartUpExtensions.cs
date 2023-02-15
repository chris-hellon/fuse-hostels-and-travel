using System.IO.Compression;
using System.Globalization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Rollbar;
using Rollbar.NetCore.AspNet;

namespace FuseHostelsAndTravel.Web.Utils
{
	public static class StartUpExtensions
	{
        public static IServiceCollection AddFuseServices(this IServiceCollection services)
        {
            services.AddScoped<IMockData, MockData>();

            return services;
        }

        public static IServiceCollection AddFuseIdentityServices(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            services.AddDbContext<ApplicationDbContext>(options =>
                 options.UseSqlServer(connectionString));
            services.AddDatabaseDeveloperPageExceptionFilter();

            services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 4;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
            }).AddEntityFrameworkStores<ApplicationDbContext>().AddUserManager<ApplicationUserManager<ApplicationUser>>().AddRoles<ApplicationRole>().AddDefaultUI().AddDefaultTokenProviders();

            services.ConfigureApplicationCookie(options =>
            {
                options.Cookie.MaxAge = TimeSpan.FromDays(30);
                options.Cookie.HttpOnly = true;
                options.LoginPath = "/login";
                options.LogoutPath = "/logout";
                options.SlidingExpiration = true;
                options.Cookie.Name = "fuse-hostels-and-travel";
            });

            services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>>();

            return services;
        }
    }
}

