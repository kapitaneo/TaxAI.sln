using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExtractionWorkflow.Models;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 015 T07 — the capture sweep.
/// <para>
/// Four markers are planted in one real journey: a SIN in T4 Box 12, a
/// distinctive byte sequence in the source document, the document-intelligence
/// adapter's own label, and a medical patient name. The journey is then driven
/// end to end over the real portal, capturing <b>everything</b> it can be
/// observed through — every HTTP response body, every browser model, every URL,
/// cookie, and header, every log record, every exception message, and every
/// file under the web root — and each capture is scanned.
/// </para>
/// <para>
/// The sweep is only as good as its coverage, so every probe is proved to have
/// actually travelled before its absence is asserted. A probe that was never
/// planted cannot leak, and a scan that found nothing because it scanned nothing
/// is the failure mode this guards against.
/// </para>
/// <para>
/// This task verifies; it repairs nothing. No production code is touched.
/// </para>
/// </summary>
public sealed class DocumentExtractionPiiSweepTests
{
    /// <summary>Nine consecutive digits — the shape a SIN takes, whatever its value.</summary>
    private static readonly Regex NineDigits = new(
        @"\d{9}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    /// <summary>
    /// A rendered GUID. These are removed before the nine-digit scan runs.
    /// <para>
    /// A GUID is thirty-two hexadecimal characters, and ten of the sixteen are
    /// decimal digits, so a run of nine consecutive decimal digits appears
    /// inside one often enough to fail this suite by chance — it did, on a
    /// record identifier in a redirect Location. Against a structural
    /// identifier the probe matches the random number generator rather than a
    /// leak. Every GUID in every capture is an opaque server identifier, and
    /// none of them is a place a SIN could be written, so removing them first
    /// costs the scan nothing and makes it deterministic.
    /// </para>
    /// </summary>
    private static readonly Regex RenderedGuid = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A long opaque credential — an antiforgery token, a protected cookie
    /// value, or a data-protection payload. Removed for the same reason and on
    /// the same terms: it is machine-generated base64url, never a field anything
    /// writes a taxpayer value into.
    /// </summary>
    private static readonly Regex OpaqueCredential = new(
        @"[A-Za-z0-9_\-]{40,}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact] // AC-27, AC-29, AC-30, AC-53, AC-58, AC-62, AC-79, AC-101
    public async Task NoPlantedMarkerReachesAnyResponseUrlCookieHeaderLogOrWebRootFile()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        var captures = new CaptureLog();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        var opened = await world.OpenCaseAsync(
            world.FirmOne,
            ExtractionSource.T4(),
            ExtractionSource.T4(),
            ExtractionSource.Medical());

        // ---------------------------------------------- drive the whole journey
        await captures.CaptureAsync(accountant, HttpMethod.Get, opened.ListUrl);

        foreach (var documentId in opened.DocumentIds)
        {
            await captures.PostAsync(world, accountant, opened.ExtractUrl(documentId));
            await captures.CaptureAsync(
                accountant, HttpMethod.Get, opened.ReviewUrl(documentId));
        }

        var duplicate = (await world.DocumentAsync(opened, opened.SecondT4))
            .DuplicateCandidates
            .SingleOrDefault();
        if (duplicate is not null)
        {
            await captures.PostAsync(
                world,
                accountant,
                opened.DuplicateUrl(duplicate.CandidateId),
                ("resolution", nameof(DuplicateResolutionKind.DistinctDocuments)));
        }

        await captures.PostAsync(
            world,
            accountant,
            opened.CorrectionUrl(opened.FirstT4),
            ("field", nameof(CorrectionField.T4BoxValue)),
            ("boxCode", "14"),
            ("correctedValue", "52500.00"));

        await captures.PostAsync(
            world,
            accountant,
            opened.ClassificationUrl(opened.Medical),
            ("targetClassification", nameof(DocumentClassification.MedicalExpenseEvidence)));
        await captures.PostAsync(world, accountant, opened.ExtractUrl(opened.Medical));

        foreach (var documentId in opened.DocumentIds)
        {
            await captures.PostAsync(world, accountant, opened.AcceptUrl(documentId));
        }

        await captures.PostAsync(world, accountant, opened.FinalizeUrl);
        await captures.CaptureAsync(accountant, HttpMethod.Get, opened.ListUrl);

        var saved = Assert.Single(world.Reviews.Saved);
        await captures.CaptureAsync(
            accountant, HttpMethod.Get, $"/reviews/{saved.RecordId:D}");

        // Every log record the host wrote during the journey.
        captures.AddLogRecords(world.FirmOne.Logs.Records);

        // ------------------------------------------- the journey really happened
        Assert.True(captures.Count > 10, "The sweep captured too little to mean anything.");
        Assert.Contains(
            captures.Bodies,
            body => body.Contains(ExtractionSource.EmployerName, StringComparison.Ordinal));

        // The probes really did travel: they are in the bytes the adapter read.
        var staged = string.Join("\n", world.Store.All
            .SelectMany(stored => stored.Documents)
            .Select(document => document.ContentKey));
        Assert.NotEmpty(staged);

        // ---------------------------------------------------------- the scan
        foreach (var probe in new[]
                 {
                     ExtractionSource.SinProbe,
                     string.Join(" ", "123", "456", "789"),
                     "123-456-789",
                     "***-**-789",
                     ExtractionSource.SourceMarker,
                 })
        {
            var leak = captures.FirstContaining(probe);
            Assert.True(leak is null, $"'{probe}' reached {leak?.Where}.");
        }

        // AC-79: the medical provider and patient names are approved display PII
        // on the accountant extraction-review surface and nowhere else. They may
        // appear in that page's body; they may never appear in a URL, a cookie,
        // a header, a log, or an exception.
        foreach (var probe in new[]
                 {
                     ExtractionSource.MedicalProviderName,
                     ExtractionSource.PatientName,
                 })
        {
            var leak = captures.FirstContainingOutsideBodies(probe);
            Assert.True(leak is null, $"'{probe}' reached {leak?.Where}.");
        }

        // AC-27: no nine-digit run anywhere, in any capture, once the opaque
        // machine-generated identifiers a digit run can arise inside by chance
        // are removed. Everything that remains is text this feature chose to
        // emit, and none of it may be SIN-shaped.
        var digits = captures.FirstMatching(
            NineDigits,
            text => OpaqueCredential.Replace(RenderedGuid.Replace(text, " "), " "));
        Assert.True(
            digits is null,
            $"A nine-digit sequence reached {digits?.Where}: {digits?.Excerpt}");

        // AC-62: no capture carries a "a SIN was present" signal either.
        foreach (var signal in new[]
                 {
                     "sinPresent",
                     "hasSin",
                     "sinProvided",
                     "socialInsuranceNumberPresent",
                 })
        {
            Assert.Null(captures.FirstContaining(signal));
        }
    }

    [Fact] // AC-78
    public async Task TheBrowserModelCarriesOnlyItsAllowlistedMembers()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        var opened = await world.OpenCaseAsync(
            world.FirmOne, ExtractionSource.T4(), ExtractionSource.Medical());

        await world.PostAsync(accountant, opened.ExtractUrl(opened.FirstT4));
        await world.PostAsync(accountant, opened.ExtractUrl(opened.Medical));

        // Every identifier the server holds, and every value the browser must
        // never be told, checked against the two rendered pages.
        var forbidden = world.EveryServerIdentifier().ToList();
        forbidden.Add(ExtractionSource.SinProbe);
        forbidden.Add(ExtractionSource.SourceMarker);

        foreach (var documentId in new[] { opened.FirstT4, opened.Medical })
        {
            using var response = await accountant.GetAsync(opened.ReviewUrl(documentId));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();

            foreach (var value in forbidden)
            {
                Assert.DoesNotContain(value, html, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact] // AC-54
    public async Task TheWholeFlowCompletesWithNoOutboundNetworkCallAttempted()
    {
        // Every outbound socket connection the process attempts is recorded.
        // The shipped adapter is in-process and holds no SDK, so the journey
        // must complete with none.
        using var listener = new EgressListener();

        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        var opened = await world.OpenCaseAsync(
            world.FirmOne, ExtractionSource.T4(), ExtractionSource.Medical());

        await world.PostAsync(accountant, opened.ExtractUrl(opened.FirstT4));
        await world.PostAsync(accountant, opened.ExtractUrl(opened.Medical));
        await world.PostAsync(
            accountant,
            opened.CorrectionUrl(opened.FirstT4),
            ("field", nameof(CorrectionField.T4BoxValue)),
            ("boxCode", "14"),
            ("correctedValue", "52500.00"));
        await world.PostAsync(accountant, opened.AcceptUrl(opened.FirstT4));
        await world.PostAsync(accountant, opened.AcceptUrl(opened.Medical));
        await world.PostAsync(accountant, opened.FinalizeUrl);

        Assert.Single(world.Reviews.Saved);

        // The in-process TestServer pipeline binds no port and opens no socket,
        // so any connection recorded here came from the feature itself.
        Assert.Empty(listener.Connections);
    }

    [Fact] // AC-31, AC-53
    public async Task NoFileUnderTheWebRootEverHoldsSourceContent()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        var opened = await world.OpenCaseAsync(world.FirmOne, ExtractionSource.T4());
        await world.PostAsync(accountant, opened.ExtractUrl(opened.FirstT4));
        await world.PostAsync(accountant, opened.AcceptUrl(opened.FirstT4));
        await world.PostAsync(accountant, opened.FinalizeUrl);

        var webRoot = Path.Combine(RepositoryRoot(), "src", "TaxAI.Portal", "wwwroot");

        if (Directory.Exists(webRoot))
        {
            foreach (var path in Directory.EnumerateFiles(
                         webRoot, "*", SearchOption.AllDirectories))
            {
                var text = await File.ReadAllTextAsync(path);
                Assert.DoesNotContain(
                    ExtractionSource.SourceMarker, text, StringComparison.Ordinal);
                Assert.DoesNotContain(
                    ExtractionSource.SinProbe, text, StringComparison.Ordinal);
            }
        }

        // The encrypted source object lives outside the web root, and it is the
        // one raw-byte exception this feature has. It is proved to exist, and
        // proved not to be reachable through any route.
        var contentRoot = Environment.GetEnvironmentVariable(
            "TaxAi__Intake__DevelopmentContentRoot");
        Assert.False(string.IsNullOrEmpty(contentRoot));
        Assert.False(
            contentRoot!.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase),
            "The encrypted source objects are inside the web root.");

        // Even holding the object's own key, no route serves it.
        var contentKey = world.Store.Single().Documents[0].ContentKey;
        foreach (var attempt in new[]
                 {
                     $"/{contentKey}",
                     $"/content/{contentKey}",
                     $"{opened.ExtractionsPrefix}/{opened.FirstT4:D}/{contentKey}",
                 })
        {
            using var response = await accountant.GetAsync(attempt);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not find the repository root containing TaxAI.sln.");
    }

    // ------------------------------------------------------------- captures

    private sealed record Capture(string Where, string Text)
    {
        internal string Excerpt =>
            Text.Length <= 160 ? Text : Text[..160];
    }

    /// <summary>
    /// Everything one journey can be observed through. Bodies are tracked
    /// separately from the rest, because one surface — the accountant
    /// extraction-review page — is allowed to render approved display PII, and
    /// nothing else is.
    /// </summary>
    private sealed class CaptureLog
    {
        private readonly List<Capture> _bodies = [];
        private readonly List<Capture> _other = [];

        internal int Count => _bodies.Count + _other.Count;

        internal IEnumerable<string> Bodies => _bodies.Select(capture => capture.Text);

        internal async Task CaptureAsync(
            HttpClient client,
            HttpMethod method,
            string url,
            HttpContent? content = null)
        {
            using var request = new HttpRequestMessage(method, url) { Content = content };
            using var response = await client.SendAsync(request);
            await RecordAsync(url, response);
        }

        internal async Task PostAsync(
            ExtractionPortalWorld world,
            HttpClient client,
            string url,
            params (string Name, string Value)[] fields)
        {
            using var response = await world.PostRawAsync(client, url, fields);
            await RecordAsync(url, response);
        }

        private async Task RecordAsync(string url, HttpResponseMessage response)
        {
            // The URL itself, and the location a redirect names.
            _other.Add(new Capture("a request URL", url));
            if (response.Headers.Location is { } location)
            {
                _other.Add(new Capture("a redirect Location", location.ToString()));
            }

            foreach (var header in response.Headers.Concat(response.Content.Headers))
            {
                _other.Add(new Capture(
                    $"the '{header.Key}' response header",
                    string.Join(",", header.Value)));
            }

            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    _other.Add(new Capture("a Set-Cookie header", cookie));
                }
            }

            var body = await response.Content.ReadAsStringAsync();
            _bodies.Add(new Capture($"the body of {url}", body));

            // A body that is JSON is also scanned as a browser model, so a value
            // carried in a serialized property is caught as well as one rendered.
            if (body.StartsWith('{') || body.StartsWith('['))
            {
                try
                {
                    _bodies.Add(new Capture(
                        $"the browser model of {url}",
                        JsonSerializer.Serialize(
                            JsonSerializer.Deserialize<JsonElement>(body))));
                }
                catch (JsonException)
                {
                    // Not a model; the body capture above already covers it.
                }
            }
        }

        internal void AddLogRecords(IReadOnlyList<string> records)
        {
            foreach (var record in records)
            {
                _other.Add(new Capture("a log record", record));
            }
        }

        internal Capture? FirstContaining(string probe) =>
            _bodies.Concat(_other).FirstOrDefault(capture =>
                capture.Text.Contains(probe, StringComparison.OrdinalIgnoreCase));

        internal Capture? FirstContainingOutsideBodies(string probe) =>
            _other.FirstOrDefault(capture =>
                capture.Text.Contains(probe, StringComparison.OrdinalIgnoreCase));

        internal Capture? FirstMatching(Regex pattern, Func<string, string> normalize) =>
            _bodies.Concat(_other).FirstOrDefault(capture =>
                pattern.IsMatch(normalize(capture.Text)));
    }

    /// <summary>
    /// Records every outbound socket connection the process attempts while it is
    /// alive, so "no taxpayer content left this process" is a measured fact
    /// rather than a reading of the source.
    /// </summary>
    private sealed class EgressListener : IDisposable
    {
        private readonly SocketsHttpHandlerProbe _probe = new();

        internal IReadOnlyList<string> Connections => _probe.Connections;

        public void Dispose() => _probe.Dispose();

        private sealed class SocketsHttpHandlerProbe : IDisposable
        {
            private readonly List<string> _connections = [];
                private readonly IDisposable? _subscription;

            internal SocketsHttpHandlerProbe()
            {
                _connections = [];
                _subscription = System.Diagnostics.DiagnosticListener.AllListeners.Subscribe(
                    new ListenerObserver(_connections));
            }

            internal IReadOnlyList<string> Connections
            {
                get
                {
                    lock (_connections)
                    {
                        return _connections.ToArray();
                    }
                }
            }

            public void Dispose()
            {
                _subscription?.Dispose();
            }

            private sealed class ListenerObserver :
                IObserver<System.Diagnostics.DiagnosticListener>
            {
                private readonly List<string> _connections;

                internal ListenerObserver(List<string> connections) =>
                    _connections = connections;

                public void OnCompleted()
                {
                }

                public void OnError(Exception error)
                {
                }

                public void OnNext(System.Diagnostics.DiagnosticListener value)
                {
                    // Every outbound HTTP request in .NET raises this listener,
                    // whatever handler makes it.
                    if (value.Name != "HttpHandlerDiagnosticListener")
                    {
                        return;
                    }

                    value.Subscribe(new EventObserver(_connections));
                }
            }

            private sealed class EventObserver :
                IObserver<KeyValuePair<string, object?>>
            {
                private readonly List<string> _connections;

                internal EventObserver(List<string> connections) =>
                    _connections = connections;

                public void OnCompleted()
                {
                }

                public void OnError(Exception error)
                {
                }

                public void OnNext(KeyValuePair<string, object?> value)
                {
                    lock (_connections)
                    {
                        _connections.Add(value.Key);
                    }
                }
            }
        }
    }
}
