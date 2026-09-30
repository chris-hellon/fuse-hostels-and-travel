using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mediator;
using Fuse.Contracts.Api;
using Fuse.Contracts.Common;

namespace Fuse.Web.Client.Api;

/// <summary>
/// <see cref="IApi"/> in the browser: sends each contract to the endpoint its route attribute
/// declares, and turns ProblemDetails back into the exception the handler threw.
/// </summary>
public sealed class HttpApi(HttpClient http) : IApi
{
    public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(query, cancellationToken);

    public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(command, cancellationToken);

    public async ValueTask Send(ICommand command, CancellationToken cancellationToken = default) =>
        await SendAsync<Unit>(command, cancellationToken);

    private async ValueTask<TResponse> SendAsync<TResponse>(object request, CancellationToken cancellationToken)
    {
        var contract = ContractMetadata.For(request.GetType());
        if (!contract.IsExposed)
            throw new InvalidOperationException($"{contract.Type.Name} has no route and can't be sent from the browser.");

        using var message = BuildRequest(contract, request);
        using var response = await http.SendAsync(message, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw await ReadProblemAsync(response, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent || typeof(TResponse) == typeof(Unit))
            return default!;

        return (await response.Content.ReadFromJsonAsync<TResponse>(ApiJson.Options, cancellationToken))!;
    }

    private static HttpRequestMessage BuildRequest(ContractMetadata contract, object request)
    {
        var json = JsonSerializer.SerializeToNode(request, request.GetType(), ApiJson.Options)!.AsObject();
        var path = new StringBuilder(contract.Route!.Template);

        foreach (var name in contract.RouteParameters)
        {
            var key = json.Select(p => p.Key).First(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            path.Replace($"{{{name}}}", Uri.EscapeDataString(json[key]?.ToString() ?? ""));
            if (!contract.Route.HasBody)
                json.Remove(key);
        }

        var url = $"{ContractMetadata.RoutePrefix}/{path}";
        var message = new HttpRequestMessage(new HttpMethod(contract.Route.Method), contract.Route.HasBody ? url : url + QueryString(json));
        message.Headers.Add(ApiHeaders.Marker, "1");
        if (contract.Route.HasBody)
            message.Content = JsonContent.Create(json, options: ApiJson.Options);
        return message;
    }

    private static string QueryString(JsonObject json)
    {
        var parts = new List<string>();
        foreach (var (key, value) in json)
        {
            switch (value)
            {
                case null:
                    continue;
                case JsonArray array:
                    parts.AddRange(array.Where(v => v is not null).Select(v => $"{key}={Uri.EscapeDataString(Text(v!))}"));
                    break;
                default:
                    parts.Add($"{key}={Uri.EscapeDataString(Text(value))}");
                    break;
            }
        }

        return parts.Count == 0 ? "" : "?" + string.Join('&', parts);

        static string Text(JsonNode node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node.ToJsonString().Trim('"');
    }

    private static async Task<AppException> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonObject>(ApiJson.Options, cancellationToken);
            var title = problem?["title"]?.GetValue<string>() ?? response.ReasonPhrase ?? "Request failed.";
            var errorType = problem?[ApiHeaders.ErrorType]?.GetValue<string>();
            var errors = problem?[ApiHeaders.Errors]?.Deserialize<Dictionary<string, string[]>>(ApiJson.Options);
            return AppExceptions.Create(errorType, title, errors, status);
        }
        catch (JsonException)
        {
            return AppExceptions.Create(null, response.ReasonPhrase ?? "Request failed.", null, status);
        }
    }
}
