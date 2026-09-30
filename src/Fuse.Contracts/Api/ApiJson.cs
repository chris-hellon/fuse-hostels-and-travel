using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fuse.Contracts.Api;

/// <summary>Serializer settings shared by the endpoints and <c>HttpApi</c> so both ends agree.</summary>
public static class ApiJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        if (!options.Converters.OfType<JsonStringEnumConverter>().Any())
            options.Converters.Add(new JsonStringEnumConverter());
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
