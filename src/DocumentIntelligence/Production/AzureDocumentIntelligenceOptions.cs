using System.Text.Json.Serialization;

namespace DocumentIntelligence.Production;

/// <summary>Explicit production bounds. Resource approval is independently checked by the host.</summary>
public sealed class AzureDocumentIntelligenceOptions
{
    public const string ApprovedApiVersion = "2024-11-30";
    public const string ApprovedModel = "prebuilt-layout";
    public const string ApprovedFeatures = "keyValuePairs";
    [JsonIgnore] public string? Endpoint { get; init; }
    [JsonIgnore] public string? ResourceId { get; init; }
    [JsonIgnore] public string? ManagedIdentityClientId { get; init; }
    public string? Region { get; init; }
    public string? ApiVersion { get; init; }
    public string? Model { get; init; }
    public string? Features { get; init; }
    public long MaximumSourceBytes { get; init; }
    public TimeSpan OperationDeadline { get; init; }
    public TimeSpan CleanupDeadline { get; init; }
    public int MaximumAttempts { get; init; }

    public static AzureDocumentIntelligenceOptions RequireValid(AzureDocumentIntelligenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || endpoint.Port != 443 ||
            endpoint.UserInfo.Length != 0 || endpoint.AbsolutePath != "/" ||
            endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            !endpoint.Host.EndsWith(".cognitiveservices.azure.com", StringComparison.Ordinal) ||
            endpoint.Host.Split('.').Length != 4 ||
            string.IsNullOrWhiteSpace(options.ResourceId) ||
            !options.ResourceId.EndsWith("/providers/Microsoft.CognitiveServices/accounts/" + endpoint.Host.Split('.')[0], StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(options.ManagedIdentityClientId, "D", out var identity) || identity == Guid.Empty ||
            options.Region != "canadacentral" || options.ApiVersion != ApprovedApiVersion ||
            options.Model != ApprovedModel || options.Features != ApprovedFeatures ||
            options.MaximumSourceBytes <= 0 || options.MaximumSourceBytes > 500L * 1024 * 1024 ||
            options.MaximumAttempts is < 1 or > 5 ||
            options.OperationDeadline <= TimeSpan.Zero || options.OperationDeadline > TimeSpan.FromMinutes(5) ||
            options.CleanupDeadline <= TimeSpan.Zero || options.CleanupDeadline >= options.OperationDeadline)
        {
            throw new ArgumentException("Production document intelligence configuration is invalid.");
        }
        return options;
    }

    public override string ToString() => nameof(AzureDocumentIntelligenceOptions);
}
