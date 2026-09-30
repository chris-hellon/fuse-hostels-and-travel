namespace Fuse.Contracts.Api;

/// <summary>
/// Exposes a request contract as an HTTP endpoint under <c>/api/app/</c>.
/// Route placeholders (<c>{Id}</c>) bind to request properties of the same name.
/// GET/DELETE bind remaining properties from the query string; POST/PUT send the request as JSON.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public abstract class ApiRouteAttribute(string method, string template) : Attribute
{
    public string Method { get; } = method;
    public string Template { get; } = template.Trim('/');
    public bool HasBody => Method is "POST" or "PUT";
}

public sealed class GetAttribute(string template) : ApiRouteAttribute("GET", template);
public sealed class PostAttribute(string template) : ApiRouteAttribute("POST", template);
public sealed class PutAttribute(string template) : ApiRouteAttribute("PUT", template);
public sealed class DeleteAttribute(string template) : ApiRouteAttribute("DELETE", template);
