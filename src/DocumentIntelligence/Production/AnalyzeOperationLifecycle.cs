using System.Diagnostics.Tracing;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Core;

namespace DocumentIntelligence.Production;

internal sealed class AnalyzeOperationLifecycle(
    AzureDocumentIntelligenceOptions options, TokenCredential credential, HttpClient client)
{
    private const string ModelPath = "/documentintelligence/documentModels/prebuilt-layout";
    private const string VersionQuery = "?api-version=" + AzureDocumentIntelligenceOptions.ApprovedApiVersion;
    private const int MaximumResponseBytes = 16 * 1024 * 1024;

    internal async Task<bool> ProbeAsync(CancellationToken token)
    {
        try
        {
            using var request = await RequestAsync(HttpMethod.Get,
                new Uri(new Uri(options.Endpoint!), "/documentintelligence/info" + VersionQuery), token).ConfigureAwait(false);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception) { return false; }
    }

    internal async Task<T?> ExecuteAsync<T>(Stream source, Func<JsonElement, T?> interpret,
        CancellationToken cancellationToken, TimeSpan remaining) where T : class
    {
        if (source is null || !source.CanRead || remaining <= options.CleanupDeadline) return null;
        using var total = new CancellationTokenSource(remaining);
        using var work = CancellationTokenSource.CreateLinkedTokenSource(total.Token, cancellationToken);
        work.CancelAfter(remaining - options.CleanupDeadline);
        Uri? resultUri = null;
        T? result = null;
        byte[]? bytes = null;
        var deleted = false;
        try
        {
            bytes = await ReadBoundedAsync(source, options.MaximumSourceBytes, work.Token).ConfigureAwait(false);
            if (bytes.Length == 0) return null;
            using var request = await RequestAsync(HttpMethod.Post,
                new Uri(new Uri(options.Endpoint!), ModelPath + ":analyze" + VersionQuery + "&features=keyValuePairs"),
                work.Token).ConfigureAwait(false);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            // One submission only. This client has neither a retry handler nor redirect following.
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, work.Token).ConfigureAwait(false);
            resultUri = ReadResultUri(response);
            if (response.StatusCode == HttpStatusCode.Accepted && resultUri is not null)
                result = await PollAsync(resultUri, interpret, work.Token).ConfigureAwait(false);
        }
        catch (Exception) { result = null; }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            if (resultUri is not null)
            {
                using var cleanup = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
                cleanup.CancelAfter(options.CleanupDeadline);
                deleted = await DeleteAsync(resultUri, cleanup.Token).ConfigureAwait(false);
                if (!deleted) ProviderLifecycleEvents.Log.DeletionUnavailable();
            }
        }
        return deleted && !work.IsCancellationRequested && !cancellationToken.IsCancellationRequested ? result : null;
    }

    private async Task<T?> PollAsync<T>(Uri resultUri, Func<JsonElement, T?> interpret,
        CancellationToken token) where T : class
    {
        // An in-progress status is not a failure: it polls until the operation
        // deadline. Only transient read failures consume the bounded retry budget.
        var failures = 0;
        var progress = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var request = await RequestAsync(HttpMethod.Get, resultUri, token).ConfigureAwait(false);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    var payload = await ReadBoundedAsync(stream, MaximumResponseBytes, token).ConfigureAwait(false);
                    try
                    {
                        using var json = JsonDocument.Parse(payload);
                        var status = json.RootElement.GetProperty("status").GetString();
                        if (status == "succeeded")
                        {
                            var transformed = interpret(json.RootElement.GetProperty("analyzeResult"));
                            token.ThrowIfCancellationRequested();
                            return transformed;
                        }
                        if (status is not ("running" or "notStarted")) return null;
                    }
                    finally { CryptographicOperations.ZeroMemory(payload); }
                    await ProgressDelayAsync(progress++, token).ConfigureAwait(false);
                    continue;
                }
                if (!Transient(response.StatusCode)) return null;
            }
            catch (HttpRequestException) { }
            if (++failures >= options.MaximumAttempts) return null;
            await BackoffAsync(failures - 1, token).ConfigureAwait(false);
        }
    }

    private async Task<bool> DeleteAsync(Uri resultUri, CancellationToken token)
    {
        for (var attempt = 0; attempt < options.MaximumAttempts; attempt++)
        {
            try
            {
                using var request = await RequestAsync(HttpMethod.Delete, resultUri, token).ConfigureAwait(false);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NoContent) return true;
                if (!Transient(response.StatusCode)) return false;
            }
            catch (Exception) { if (token.IsCancellationRequested) return false; }
            try
            {
                if (attempt + 1 < options.MaximumAttempts) await BackoffAsync(attempt, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, Uri uri, CancellationToken token)
    {
        var access = await credential.GetTokenAsync(new TokenRequestContext(
            ["https://cognitiveservices.azure.com/.default"]), token).ConfigureAwait(false);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.Token);
        return request;
    }

    private Uri? ReadResultUri(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Operation-Location", out var values)) return null;
        var locations = values.ToArray();
        if (locations.Length != 1 || !Uri.TryCreate(locations[0], UriKind.Absolute, out var candidate)) return null;
        var endpoint = new Uri(options.Endpoint!);
        var prefix = ModelPath + "/analyzeResults/";
        if (candidate.Scheme != endpoint.Scheme || candidate.Authority != endpoint.Authority ||
            candidate.UserInfo.Length != 0 || candidate.Fragment.Length != 0 ||
            candidate.Query != VersionQuery || !candidate.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(candidate.AbsolutePath[prefix.Length..], "D", out var id) || id == Guid.Empty) return null;
        // Reconstruct from the approved origin, model and UUID, never follow the supplied URL.
        return new Uri(endpoint, prefix + id.ToString("D") + VersionQuery);
    }

    private static bool Transient(HttpStatusCode status) => status == HttpStatusCode.TooManyRequests ||
        status == HttpStatusCode.RequestTimeout || (int)status >= 500;
    private static Task BackoffAsync(int attempt, CancellationToken token) =>
        Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), token);
    // Growth capped at two seconds; the linked operation deadline, not a count, ends waiting.
    private static Task ProgressDelayAsync(int poll, CancellationToken token) =>
        Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << Math.Min(poll, 3))), token);

    private static async Task<byte[]> ReadBoundedAsync(Stream source, long maximum, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var block = new byte[8192];
        try
        {
            int count;
            while ((count = await source.ReadAsync(block, token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + count > maximum) throw new InvalidOperationException("Provider content limit exceeded.");
                await buffer.WriteAsync(block.AsMemory(0, count), token).ConfigureAwait(false);
            }
            return buffer.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(block);
            if (buffer.TryGetBuffer(out var allocated)) CryptographicOperations.ZeroMemory(allocated.AsSpan());
        }
    }
}

[EventSource(Name = "TaxAI-DocumentIntelligence-Lifecycle")]
internal sealed class ProviderLifecycleEvents : EventSource
{
    internal static readonly ProviderLifecycleEvents Log = new();
    [Event(1, Level = EventLevel.Warning, Message = "Document processing cleanup unavailable.")]
    public void DeletionUnavailable() => WriteEvent(1);
}
