
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FuseHostelsAndTravel.Web.Utils;
using Travaloud.Infrastructure.Utils;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Add services to the container.
builder.Services.AddFuseIdentityServices(configuration);
builder.Services.AddTravaloudServices(configuration, builder.Environment);
builder.Services.AddFuseServices();

var app = builder.Build();

app.ConfigureTravaloudApp(app.Environment);
app.MapRazorPages();
app.Run();

