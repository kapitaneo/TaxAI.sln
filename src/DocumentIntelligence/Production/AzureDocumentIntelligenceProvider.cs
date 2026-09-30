using Azure.Core;
using Azure.Identity;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;

namespace DocumentIntelligence.Production;

/// <summary>One approved REST processor. No cross-call source, operation, or OCR cache.</summary>
public sealed class AzureDocumentIntelligenceProvider : IDocumentIntelligenceProvider, IDisposable
{
    private readonly AnalyzeOperationLifecycle _lifecycle;
    private readonly HttpClient _client;
    private readonly IExtractionAvailabilityGate _availability;
    private readonly bool _ownsClient;

    public AzureDocumentIntelligenceProvider(AzureDocumentIntelligenceOptions options,
        IExtractionAvailabilityGate availability)
        : this(options, availability, CreateCredential(options), CreateClient(), true) { }

    /// <summary>Controlled transport seam. Production composition uses the managed-identity constructor.</summary>
    public AzureDocumentIntelligenceProvider(AzureDocumentIntelligenceOptions options,
        IExtractionAvailabilityGate availability, TokenCredential credential, HttpClient client)
        : this(options, availability, credential, client, false) { }

    private AzureDocumentIntelligenceProvider(AzureDocumentIntelligenceOptions options,
        IExtractionAvailabilityGate availability, TokenCredential credential, HttpClient client, bool ownsClient)
    {
        Options = AzureDocumentIntelligenceOptions.RequireValid(options);
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentNullException.ThrowIfNull(credential);
        _ownsClient = ownsClient;
        _lifecycle = new AnalyzeOperationLifecycle(Options, credential, client);
    }

    public AzureDocumentIntelligenceOptions Options { get; }

    /// <summary>Non-content data-plane availability, bounded by the configured cleanup/probe budget.</summary>
    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(Options.CleanupDeadline);
        return await _lifecycle.ProbeAsync(bounded.Token).ConfigureAwait(false);
    }

    public Task<ProviderClassificationResult?> ClassifyAsync(Stream source, CancellationToken cancellationToken) =>
        RunAsync(source, LayoutResultInterpreter.Classify, cancellationToken);
    public Task<ProviderT4Result?> ExtractT4Async(Stream source, CancellationToken cancellationToken) =>
        RunAsync(source, LayoutResultInterpreter.T4, cancellationToken);
    /// <summary>
    /// Refused without submission. No CRA-printed medical receipt title or
    /// layout exists to recognize, so production medical interpretation is a
    /// pending Class A item (spec 016 T05); sending the receipt would disclose
    /// it for a result that could never be released.
    /// </summary>
    public Task<ProviderMedicalResult?> ExtractMedicalAsync(Stream source, CancellationToken cancellationToken) =>
        Task.FromResult<ProviderMedicalResult?>(null);

    private async Task<T?> RunAsync<T>(Stream source, Func<System.Text.Json.JsonElement, T?> interpret,
        CancellationToken cancellationToken) where T : class
    {
        try
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            using var admission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            admission.CancelAfter(Options.CleanupDeadline);
            if (!await _availability.IsAvailableAsync(admission.Token).ConfigureAwait(false)) return null;
            return await _lifecycle.ExecuteAsync(source, interpret, cancellationToken,
                Options.OperationDeadline - elapsed.Elapsed).ConfigureAwait(false);
        }
        catch (Exception) { return null; }
    }

    private static TokenCredential CreateCredential(AzureDocumentIntelligenceOptions options)
    {
        AzureDocumentIntelligenceOptions.RequireValid(options);
        var credentialOptions = new ManagedIdentityCredentialOptions(
            ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId!));
        credentialOptions.Diagnostics.IsLoggingEnabled = false;
        credentialOptions.Diagnostics.IsLoggingContentEnabled = false;
        credentialOptions.Diagnostics.IsDistributedTracingEnabled = false;
        credentialOptions.Retry.MaxRetries = 0;
        return new ManagedIdentityCredential(credentialOptions);
    }

    private static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ActivityHeadersPropagator = null,
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public void Dispose() { if (_ownsClient) _client.Dispose(); }
    public override string ToString() => nameof(AzureDocumentIntelligenceProvider);
}
