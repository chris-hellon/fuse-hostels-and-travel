using Mediator;
using Microsoft.AspNetCore.Mvc;
using Fuse.Contracts.Api;
using Fuse.Shared.Authorization;

namespace Fuse.Web.Api;

public static class ContractEndpoints
{
    public const string StaffPolicy = "Staff";

    /// <summary>Maps every contract to <c>/api/app/{route}</c>, dispatching to its Mediator handler.</summary>
    public static IEndpointRouteBuilder MapContractEndpoints(this IEndpointRouteBuilder app)
    {
        var contracts = ContractCatalog.Load(typeof(ContractMetadata).Assembly);
        var group = app.MapGroup(ContractMetadata.RoutePrefix)
            .WithTags("App")
            .AddEndpointFilter<ApiMarkerFilter>();

        foreach (var contract in contracts)
        {
            var endpoint = group.MapMethods(contract.Route!.Template, [contract.Route.Method], async (HttpContext context, [FromServices] ISender sender) =>
            {
                var request = await QueryBinding.BindAsync(context, contract);
                var response = await sender.Send(request, context.RequestAborted);
                return response is null or Unit ? Results.NoContent() : Results.Json(response, ApiJson.Options);
            });

            endpoint.WithName(contract.Type.Name);
            if (contract.Type.IsDefined(typeof(RateLimitedAttribute), inherit: false))
                endpoint.RequireRateLimiting(RateLimitedAttribute.Policy);

            // Coarse gate at the edge (401 vs 403); the pipeline's AuthorizationBehaviour does the exact check.
            _ = contract.Access switch
            {
                PublicAttribute => endpoint.AllowAnonymous(),
                AuthenticatedAttribute => endpoint.RequireAuthorization(),
                _ => endpoint.RequireAuthorization(StaffPolicy),
            };
        }

        return app;
    }

    public static IServiceCollection AddContractEndpointAuthorization(this IServiceCollection services) =>
        services.AddAuthorizationBuilder()
            .AddPolicy(StaffPolicy, policy => policy.RequireRole([.. FuseRoles.StaffRoles]))
            .Services;

    /// <summary>
    /// Rejects state-changing calls without the <see cref="ApiHeaders.Marker"/> header. Browsers won't
    /// attach custom headers cross-site without a CORS preflight (which we never allow), so a forged
    /// form post can't reach a handler even though the cookie rides along.
    /// </summary>
    private sealed class ApiMarkerFilter : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var request = context.HttpContext.Request;
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !request.Headers.ContainsKey(ApiHeaders.Marker))
                return ValueTask.FromResult<object?>(Results.Problem(statusCode: 400, title: $"Missing {ApiHeaders.Marker} header."));
            return next(context);
        }
    }
}
