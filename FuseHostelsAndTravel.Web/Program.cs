using Travaloud.Infrastructure.Utils;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.Services.AddTravaloudIdentityServices(configuration);
builder.Services.AddTravaloudServices(configuration, builder.Environment);

var app = builder.Build();

app.ConfigureTravaloudApp(app.Environment);
app.MapRazorPages();
app.Run();

