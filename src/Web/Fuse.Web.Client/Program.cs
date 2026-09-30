using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Services;
using MudBlazor.Services;
using Fuse.Contracts.Api;
using Fuse.UI.Auth;
using Fuse.Web.Client.Api;
using Fuse.Web.Client.Auth;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

Fuse.Shared.Globalization.MonthAbbreviations.UseThreeLetterMonths();

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IApi, HttpApi>();
builder.Services.AddScoped<Fuse.UI.FileSaver>();
builder.Services.AddScoped<LazyAssemblyLoader>();
builder.Services.AddMudServices();

builder.Services.AddAuthorizationCore();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

// Wrap the prerendered auth state so sign-in/out can update it without a reload.
var prerendered = builder.Services.Last(d => d.ServiceType == typeof(AuthenticationStateProvider));
builder.Services.Remove(prerendered);
builder.Services.AddScoped(sp => new ClientAuthenticationStateProvider(
    (AuthenticationStateProvider)(prerendered.ImplementationFactory?.Invoke(sp)
        ?? ActivatorUtilities.CreateInstance(sp, prerendered.ImplementationType!)),
    sp.GetRequiredService<HttpClient>()));
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<ClientAuthenticationStateProvider>());
builder.Services.AddScoped<IAuthClient, HttpAuthClient>();

await builder.Build().RunAsync();
