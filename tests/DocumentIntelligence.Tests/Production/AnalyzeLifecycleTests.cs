using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using DocumentIntelligence.Production;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;

namespace DocumentIntelligence.Tests.Production;

public sealed class AnalyzeLifecycleTests
{
    [Theory]
    [InlineData("classify")]
    [InlineData("t4")]
    public async Task EverySubmittingMethodAnalyzesOnceAndDeletesBeforeReturning(string method)
    {
        using var wire = new ProviderWire();
        using var provider = wire.Provider();
        var result = await wire.Invoke(provider, method);
        Assert.NotNull(result);
        Assert.Equal(new[] { "POST", "GET", "DELETE" }, wire.Methods);
        Assert.All(wire.Uris, uri => Assert.Equal("approved.cognitiveservices.azure.com", uri.Host));
        Assert.Contains("api-version=2024-11-30&features=keyValuePairs", wire.Uris[0].Query);
        Assert.DoesNotContain("pages=", wire.Uris[0].Query);
        Assert.True(wire.Source.CanRead);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(307)]
    [InlineData(400)]
    public async Task SubmissionIsNeverRetried(int status)
    {
        using var wire = new ProviderWire { SubmitStatus = (HttpStatusCode)status, OmitLocation = true };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
        Assert.Equal(new[] { "POST" }, wire.Methods);
    }

    [Fact]
    public async Task AmbiguousAcknowledgementIsNeverResubmitted()
    {
        using var wire = new ProviderWire { SubmitThrows = true };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
        Assert.Equal(new[] { "POST" }, wire.Methods);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("canceled")]
    [InlineData("unknown")]
    public async Task KnownIdTerminalFailureStillDeletes(string state)
    {
        using var wire = new ProviderWire { State = state };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
        Assert.Equal(new[] { "POST", "GET", "DELETE" }, wire.Methods);
    }

    [Fact]
    public async Task MalformedResponseIsNotPartialAndStillDeletes()
    {
        using var wire = new ProviderWire { RawResponse = "{" };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
        Assert.Equal("DELETE", wire.Methods.Last());
    }

    [Fact]
    public async Task TransientPollRetriesAreBoundedAndExponential()
    {
        using var wire = new ProviderWire { PollStatus = HttpStatusCode.ServiceUnavailable };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
        Assert.Equal(3, wire.Methods.Count(method => method == "GET"));
        Assert.True(wire.Times[2] - wire.Times[1] >= TimeSpan.FromMilliseconds(200));
        Assert.True(wire.Times[3] - wire.Times[2] >= TimeSpan.FromMilliseconds(400));
        Assert.Equal("DELETE", wire.Methods.Last());
    }

    [Fact]
    public async Task InProgressPollsDoNotConsumeTheTransientRetryBudget()
    {
        using var wire = new ProviderWire { RunningPolls = 3 };
        using var provider = wire.Provider(ProviderWire.Options(attempts: 2));
        Assert.NotNull(await wire.Invoke(provider));
        Assert.Equal(4, wire.Methods.Count(method => method == "GET"));
        Assert.Equal(new[] { "POST", "DELETE" }, wire.Methods.Where(method => method != "GET"));
    }

    [Fact]
    public async Task PermanentlyRunningAnalysisEndsAtTheDeadlineAndDeletes()
    {
        using var wire = new ProviderWire { RunningPolls = int.MaxValue };
        using var provider = wire.Provider(ProviderWire.Options(operation: TimeSpan.FromMilliseconds(900), cleanup: TimeSpan.FromMilliseconds(300)));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await wire.Invoke(provider));
        Assert.InRange(timer.Elapsed, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(3));
        Assert.Equal("DELETE", wire.Methods.Last());
    }

    [Fact]
    public async Task CancelDuringPollStillRunsBoundedCleanup()
    {
        using var cancellation = new CancellationTokenSource();
        using var wire = new ProviderWire { DuringPoll = () => cancellation.Cancel() };
        using var provider = wire.Provider();
        Assert.Null(await provider.ExtractT4Async(wire.Source, cancellation.Token));
        Assert.Equal("DELETE", wire.Methods.Last());
        Assert.False(wire.DeleteTokenWasCancelled);
    }

    [Fact]
    public async Task HangingPollAndCleanupAreBounded()
    {
        using var wire = new ProviderWire { HangPoll = true, HangDelete = true };
        using var provider = wire.Provider(ProviderWire.Options(operation: TimeSpan.FromMilliseconds(400), cleanup: TimeSpan.FromMilliseconds(150)));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await wire.Invoke(provider));
        Assert.InRange(timer.Elapsed, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(3));
        Assert.Equal("DELETE", wire.Methods.Last());
    }

    [Fact]
    public async Task MedicalInterpretationIsRefusedWithoutAnySubmission()
    {
        using var wire = new ProviderWire { Paragraphs = ["Medical expense receipt"] };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider, "medical"));
        Assert.Empty(wire.Methods);
    }

    [Fact]
    public async Task SourceLimitRefusesWithoutSubmittingOrTruncating()
    {
        using var wire = new ProviderWire();
        using var provider = wire.Provider(ProviderWire.Options(maximum: 1));
        Assert.Null(await wire.Invoke(provider));
        Assert.Empty(wire.Methods);
    }
}

internal sealed class ProviderWire : HttpMessageHandler
{
    internal readonly MemoryStream Source = new(Encoding.UTF8.GetBytes("synthetic document only"));
    internal readonly List<string> Methods = [];
    internal readonly List<Uri> Uris = [];
    internal readonly List<TimeSpan> Times = [];
    private readonly System.Diagnostics.Stopwatch _timer = System.Diagnostics.Stopwatch.StartNew();
    // The printed CRA T4 (25) designator and its English and French slip titles.
    internal string[] Paragraphs = ["T4", "Statement of Remuneration Paid", "État de la rémunération payée"];
    internal string State = "succeeded";
    internal string? RawResponse;
    internal HttpStatusCode SubmitStatus = HttpStatusCode.Accepted;
    internal HttpStatusCode PollStatus = HttpStatusCode.OK;
    internal HttpStatusCode DeleteStatus = HttpStatusCode.NoContent;
    internal bool OmitLocation, SubmitThrows, HangPoll, HangDelete, DeleteTokenWasCancelled;
    internal string? Location;
    internal Action? DuringPoll;
    internal List<object> Pairs = [Pair("Employment income – Revenus d'emploi", "1.00")];
    internal Func<Dictionary<string, object?>, Dictionary<string, object?>>? TransformLayout;
    internal int DeleteFailures;
    internal int RunningPolls;

    internal static AzureDocumentIntelligenceOptions Options(string endpoint = "https://approved.cognitiveservices.azure.com/",
        string region = "canadacentral", string api = "2024-11-30", string model = "prebuilt-layout",
        int attempts = 3, long maximum = 1024, TimeSpan? operation = null, TimeSpan? cleanup = null) => new()
    {
        Endpoint = endpoint,
        ResourceId = "/subscriptions/11111111-1111-4111-8111-111111111111/resourceGroups/synthetic/providers/Microsoft.CognitiveServices/accounts/approved",
        ManagedIdentityClientId = "22222222-2222-4222-8222-222222222222",
        Region = region, ApiVersion = api, Model = model, Features = "keyValuePairs",
        MaximumSourceBytes = maximum, MaximumAttempts = attempts,
        OperationDeadline = operation ?? TimeSpan.FromSeconds(5), CleanupDeadline = cleanup ?? TimeSpan.FromSeconds(2),
    };
    internal AzureDocumentIntelligenceProvider Provider(AzureDocumentIntelligenceOptions? options = null,
        IExtractionAvailabilityGate? availability = null) =>
        new(options ?? Options(), availability ?? new Available(), new Credential(), new HttpClient(this, false));
    internal async Task<object?> Invoke(AzureDocumentIntelligenceProvider provider, string method = "t4") => method switch
    {
        "classify" => await provider.ClassifyAsync(Source, CancellationToken.None),
        "medical" => await provider.ExtractMedicalAsync(Source, CancellationToken.None),
        _ => await provider.ExtractT4Async(Source, CancellationToken.None),
    };
    internal static object[] Geometry(int page = 1) => [new { pageNumber = page, polygon = new[] { 0, 0, 1, 0, 1, 1, 0, 1 } }];
    // A null value is an empty printed field: the pair carries no value element.
    internal static object Pair(string key, string? value, decimal? confidence = null) => value is null
        ? new { key = new { content = key, boundingRegions = Geometry() }, confidence }
        : new
        {
            key = new { content = key, boundingRegions = Geometry() },
            value = new { content = value, boundingRegions = Geometry() },
            confidence,
        };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Methods.Add(request.Method.Method); Uris.Add(request.RequestUri!); Times.Add(_timer.Elapsed);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        if (request.Method == HttpMethod.Post)
        {
            if (SubmitThrows) throw new HttpRequestException("synthetic failure");
            var response = new HttpResponseMessage(SubmitStatus);
            if (!OmitLocation) response.Headers.Add("Operation-Location", Location ??
                "https://approved.cognitiveservices.azure.com/documentintelligence/documentModels/prebuilt-layout/analyzeResults/33333333-3333-4333-8333-333333333333?api-version=2024-11-30");
            return response;
        }
        if (request.Method == HttpMethod.Delete)
        {
            DeleteTokenWasCancelled = token.IsCancellationRequested;
            if (HangDelete) await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(DeleteFailures-- > 0 ? HttpStatusCode.ServiceUnavailable : DeleteStatus);
        }
        DuringPoll?.Invoke();
        if (HangPoll) await Task.Delay(Timeout.Infinite, token);
        token.ThrowIfCancellationRequested();
        var layout = new Dictionary<string, object?>
        {
            ["apiVersion"] = "2024-11-30", ["modelId"] = "prebuilt-layout",
            ["pages"] = new[] { new { pageNumber = 1 } },
            ["paragraphs"] = Paragraphs.Select(content => new { content, boundingRegions = Geometry() }).ToArray(),
            ["keyValuePairs"] = Pairs,
        };
        return new HttpResponseMessage(PollStatus)
        {
            Content = new StringContent(RawResponse ?? JsonSerializer.Serialize(new
            { status = RunningPolls-- > 0 ? "running" : State, analyzeResult = TransformLayout?.Invoke(layout) ?? layout })),
        };
    }
    internal sealed class Available(bool value = true) : IExtractionAvailabilityGate
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }
    private sealed class Credential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("synthetic", DateTimeOffset.MaxValue);
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
