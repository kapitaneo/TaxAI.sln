using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TaxAI.Portal.Tests.Identity;
using TaxLogicEngine.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.Portal.Tests;

/// <summary>
/// Spec 012 T01 — exercises the real cookie session, current-user/current-
/// membership authorization stage, endpoint, closed DTO projection, and HTML
/// renderer. Only the identity tables, session table, and review repository are
/// substituted; every HTTP and authorization decision is production code.
/// </summary>
public sealed class ReviewPageTests
{
    private static readonly Regex NineDigitRun = new(
        @"[0-9]{9}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Guid FirmOne =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");

    private static readonly Guid FirmTwo =
        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2");

    private static readonly Guid RecordOne =
        Guid.Parse("cccccccc-cccc-4ccc-8ccc-ccccccccccc3");

    private static readonly Guid RecordTwo =
        Guid.Parse("dddddddd-dddd-4ddd-8ddd-ddddddddddd4");

    private static readonly Guid MissingRecord =
        Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee5");

    [Fact]
    public async Task AuthorizedReviewUsesOnlyTheServerFirmAndRendersTheClosedAllowlist()
    {
        var repository = new RecordingReviewRepository();
        var record = CompleteRecord(FirmOne, RecordOne, version: 7);
        repository.Add(record);

        using var host = new ReviewHost(FirmOne, repository);
        using var client = host.CreateClient();
        await host.SignInAsync(client);

        using var response = await client.GetAsync(ReviewPath(RecordOne));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.True(response.Headers.CacheControl?.MustRevalidate);
        Assert.Contains(response.Headers.Pragma, value => value.Name == "no-cache");

        var read = Assert.Single(repository.Reads);
        Assert.Equal(FirmOne, read.Firm.Value);
        Assert.Equal(RecordOne, read.RecordId);
        Assert.True(read.CancellationToken.CanBeCanceled);
        Assert.Equal(0, repository.SaveCalls);

        foreach (var allowed in new[]
                 {
                     "2025",
                     ">7<",
                     "T4",
                     "&lt;Taxpayer &amp; Partner&gt;",
                     "Employer &quot;Quoted&quot;",
                     "box-second",
                     "2.0050",
                     "box-first",
                     "-1.005",
                     "line-second",
                     "4.0050",
                     "source-second",
                     "source-first",
                     "unmapped-second",
                     "unmapped-first",
                 })
        {
            Assert.Contains(allowed, body, StringComparison.Ordinal);
        }

        AssertAppearsBefore(body, "box-second", "box-first");
        AssertAppearsBefore(body, "line-second", "line-first");
        AssertAppearsBefore(body, "source-second", "source-first");
        AssertAppearsBefore(body, "unmapped-second", "unmapped-first");

        var liveSession = Assert.Single(host.Sessions.All);
        foreach (var prohibited in new[]
                 {
                     record.CaseId,
                     record.ParticipantId,
                     record.RecordId.ToString("D"),
                     record.FirmId.ToString("D"),
                     record.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                     record.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                     record.RetentionUntilUtc.ToString("O", CultureInfo.InvariantCulture),
                     host.User.UserId.ToString("D"),
                     host.Membership.MembershipId.ToString("D"),
                     liveSession.SessionId.ToString("D"),
                     PortalAuthorizationHarness.Issuer,
                     PortalAuthorizationHarness.Subject,
                 })
        {
            Assert.DoesNotContain(prohibited, body, StringComparison.Ordinal);
        }

        foreach (var slip in record.Slips)
        {
            Assert.DoesNotContain(
                slip.SlipId.ToString("D"),
                body,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                slip.DocumentReference.ToString("D"),
                body,
                StringComparison.Ordinal);
        }

        Assert.DoesNotContain("data-", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type=\"hidden\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
        Assert.False(NineDigitRun.IsMatch(body), "The review response contains a nine-digit run.");
    }

    [Fact]
    public async Task MissingAndForeignRecordsReturnByteIdenticalSafeNotFoundResponses()
    {
        var repository = new RecordingReviewRepository();
        repository.Add(CompleteRecord(FirmTwo, RecordTwo, version: 1));

        using var host = new ReviewHost(FirmOne, repository);
        using var client = host.CreateClient();
        await host.SignInAsync(client);

        using var foreign = await client.GetAsync(ReviewPath(RecordTwo));
        using var missing = await client.GetAsync(ReviewPath(MissingRecord));

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(await SnapshotAsync(missing), await SnapshotAsync(foreign));
        Assert.Equal(
            new[] { FirmOne, FirmOne },
            repository.Reads.Select(read => read.Firm.Value));
        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task AnonymousMalformedAndNonGetRequestsNeverReachTheRepository()
    {
        var repository = new RecordingReviewRepository();
        repository.Add(CompleteRecord(FirmOne, RecordOne, version: 1));

        using var host = new ReviewHost(FirmOne, repository);
        using var client = host.CreateClient();

        using (var anonymous = await client.GetAsync(ReviewPath(RecordOne)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        await host.SignInAsync(client);

        using (var malformed = await client.GetAsync("/reviews/not-a-guid"))
        {
            Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        }

        foreach (var method in new[]
                 {
                     HttpMethod.Post,
                     HttpMethod.Put,
                     HttpMethod.Patch,
                     HttpMethod.Delete,
                 })
        {
            using var request = new HttpRequestMessage(method, ReviewPath(RecordOne));
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        Assert.Empty(repository.Reads);
        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task UserDeactivationIsRefusedBeforeReviewLookup()
    {
        var repository = new RecordingReviewRepository();
        repository.Add(CompleteRecord(FirmOne, RecordOne, version: 1));

        using var host = new ReviewHost(FirmOne, repository);
        using var client = host.CreateClient();
        await host.SignInAsync(client);

        host.Directory.SetUserActive(host.User.UserId, isActive: false);

        using var response = await client.GetAsync(ReviewPath(RecordOne));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            PortalAuthorizationHarness.FixedRefusalBody,
            await response.Content.ReadAsStringAsync());
        Assert.Empty(repository.Reads);
    }

    [Fact]
    public async Task MembershipDeactivationIsRefusedBeforeReviewLookup()
    {
        var repository = new RecordingReviewRepository();
        repository.Add(CompleteRecord(FirmOne, RecordOne, version: 1));

        using var host = new ReviewHost(FirmOne, repository);
        using var client = host.CreateClient();
        await host.SignInAsync(client);

        host.Directory.SetMembershipActive(
            host.Membership.MembershipId,
            isActive: false);

        using var response = await client.GetAsync(ReviewPath(RecordOne));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            PortalAuthorizationHarness.FixedRefusalBody,
            await response.Content.ReadAsStringAsync());
        Assert.Empty(repository.Reads);
    }

    [Theory]
    [InlineData("Throw")]
    [InlineData("Timeout")]
    public async Task IdentityStoreFailureIsRefusedBeforeReviewLookup(
        string failureName)
    {
        var failure = Enum.Parse<PortalAuthorizationHarness.DirectoryFailure>(
            failureName);
        var repository = new RecordingReviewRepository();
        repository.Add(CompleteRecord(FirmOne, RecordOne, version: 1));

        using var host = new ReviewHost(FirmOne, repository);
        using var client = host.CreateClient();
        await host.SignInAsync(client);

        host.Directory.MembershipFailure = failure;

        using var response = await client.GetAsync(ReviewPath(RecordOne));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            PortalAuthorizationHarness.FixedRefusalBody,
            await response.Content.ReadAsStringAsync());
        Assert.Empty(repository.Reads);
    }

    [Fact]
    public async Task EmptyPackageAndEmptyCollectionsHaveExplicitStates()
    {
        var repository = new RecordingReviewRepository();
        repository.Add(Record(
            FirmOne,
            RecordOne,
            version: 1,
            slips: []));
        repository.Add(Record(
            FirmOne,
            RecordTwo,
            version: 2,
            slips:
            [
                Slip(
                    FirmOne,
                    RecordTwo,
                    boxes: [],
                    lines: [],
                    unmappedCodes: []),
            ]));

        using var host = new ReviewHost(FirmOne, repository);
        using var client = host.CreateClient();
        await host.SignInAsync(client);

        var packageBody = await client.GetStringAsync(ReviewPath(RecordOne));
        var collectionsBody = await client.GetStringAsync(ReviewPath(RecordTwo));

        Assert.Contains(
            "This review package contains no slips.",
            packageBody,
            StringComparison.Ordinal);
        Assert.Contains("No populated boxes.", collectionsBody, StringComparison.Ordinal);
        Assert.Contains("No tax lines.", collectionsBody, StringComparison.Ordinal);
        Assert.Contains("No unmapped codes.", collectionsBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptySourceBoxCollectionHasAnExplicitState()
    {
        var repository = new RecordingReviewRepository();
        repository.Add(Record(
            FirmOne,
            RecordOne,
            version: 1,
            slips:
            [
                Slip(
                    FirmOne,
                    RecordOne,
                    lines:
                    [
                        Line(FirmOne, "line-without-source", 0m, []),
                    ]),
            ]));

        using var host = new ReviewHost(FirmOne, repository);
        using var client = host.CreateClient();
        await host.SignInAsync(client);

        var body = await client.GetStringAsync(ReviewPath(RecordOne));

        Assert.Contains(
            "No source-box references.",
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoredAmountTextIsInvariantAndIsNeverRoundedOrAggregated()
    {
        var repository = new RecordingReviewRepository();
        repository.Add(CompleteRecord(FirmOne, RecordOne, version: 1));

        using var host = new ReviewHost(FirmOne, repository);
        using var client = host.CreateClient();
        await host.SignInAsync(client);

        var body = await client.GetStringAsync(ReviewPath(RecordOne));

        Assert.Contains("2.0050", body, StringComparison.Ordinal);
        Assert.Contains("-1.005", body, StringComparison.Ordinal);
        Assert.Contains(">0<", body, StringComparison.Ordinal);
        Assert.DoesNotContain("2,0050", body, StringComparison.Ordinal);
        Assert.DoesNotContain("-1.01", body, StringComparison.Ordinal);
        Assert.DoesNotContain("4.01", body, StringComparison.Ordinal);

        Assert.Equal(
            2,
            Regex.Matches(
                body,
                ">line-second<",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)).Count);
    }

    [Fact]
    public async Task ImmutableVersionsRemainSeparatelyAddressableAndNoSaveOccurs()
    {
        var repository = new RecordingReviewRepository();
        repository.Add(Record(
            FirmOne,
            RecordOne,
            version: 1,
            slips:
            [
                Slip(
                    FirmOne,
                    RecordOne,
                    taxpayer: "Original Taxpayer",
                    employer: "Original Employer"),
            ]));
        repository.Add(Record(
            FirmOne,
            RecordTwo,
            version: 2,
            slips:
            [
                Slip(
                    FirmOne,
                    RecordTwo,
                    taxpayer: "Regenerated Taxpayer",
                    employer: "Regenerated Employer"),
            ]));

        using var host = new ReviewHost(FirmOne, repository);
        using var client = host.CreateClient();
        await host.SignInAsync(client);

        var first = await client.GetStringAsync(ReviewPath(RecordOne));
        var second = await client.GetStringAsync(ReviewPath(RecordTwo));

        Assert.Contains(">1<", first, StringComparison.Ordinal);
        Assert.Contains("Original Taxpayer", first, StringComparison.Ordinal);
        Assert.DoesNotContain("Regenerated Taxpayer", first, StringComparison.Ordinal);

        Assert.Contains(">2<", second, StringComparison.Ordinal);
        Assert.Contains("Regenerated Taxpayer", second, StringComparison.Ordinal);
        Assert.DoesNotContain("Original Taxpayer", second, StringComparison.Ordinal);

        Assert.Equal(0, repository.SaveCalls);
        Assert.Equal(
            new[] { RecordOne, RecordTwo },
            repository.Reads.Select(read => read.RecordId));
    }

    [Fact]
    public async Task RepositoryFailureUsesTheFixedErrorAndWritesNoSensitiveLog()
    {
        var repository = new RecordingReviewRepository
        {
            FailureMessage = "sensitive-taxpayer-marker",
        };
        var logs = new RecordingLoggerProvider();

        using var host = new ReviewHost(FirmOne, repository, logs);
        using var client = host.CreateClient();
        await host.SignInAsync(client);

        using var response = await client.GetAsync(ReviewPath(RecordOne));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(
            HttpStatusCode.InternalServerError,
            response.StatusCode);
        Assert.Equal("Request could not be completed.", body);

        var combinedLogs = string.Join("\n", logs.Messages);
        foreach (var prohibited in new[]
                 {
                     repository.FailureMessage,
                     RecordOne.ToString("D"),
                     FirmOne.ToString("D"),
                     host.User.UserId.ToString("D"),
                     host.Membership.MembershipId.ToString("D"),
                     PortalAuthorizationHarness.Issuer,
                     PortalAuthorizationHarness.Subject,
                 })
        {
            Assert.DoesNotContain(
                prohibited,
                combinedLogs,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Spec 014 T01 — the same closed allowlist, now on the component's parameter
    /// model instead of the endpoint's private nested DTOs. The field set is
    /// unchanged; only where it is declared moved.
    /// </summary>
    [Fact]
    public void BrowserModelHasExactlyTheApprovedPropertyVocabulary()
    {
        var assembly = typeof(Program).Assembly;

        Assert.Equal(
            new[] { "Slips", "Version", "Year" },
            PropertyNames(assembly, "TaxAI.Portal.Components.Pages.ReviewPageModel"));
        Assert.Equal(
            new[]
            {
                "Boxes",
                "Employer",
                "Lines",
                "Taxpayer",
                "Type",
                "UnmappedCodes",
            },
            PropertyNames(assembly, "TaxAI.Portal.Components.Pages.ReviewSlipModel"));
        Assert.Equal(
            new[] { "Amount", "Code" },
            PropertyNames(assembly, "TaxAI.Portal.Components.Pages.ReviewBoxModel"));
        Assert.Equal(
            new[] { "Amount", "Code", "SourceBoxes" },
            PropertyNames(assembly, "TaxAI.Portal.Components.Pages.ReviewLineModel"));

        // The old nested DTOs are gone, not merely bypassed.
        Assert.Null(assembly.GetType("TaxAI.Portal.ReviewEndpoints+ReviewPageDto"));

        var modelProperties = assembly
            .GetTypes()
            .Where(type =>
                type.FullName?.StartsWith(
                    "TaxAI.Portal.Components.Pages.Review",
                    StringComparison.Ordinal) == true &&
                type.Name.EndsWith("Model", StringComparison.Ordinal) &&
                type.Name != "ReviewPageModel")
            .Concat([assembly.GetType("TaxAI.Portal.Components.Pages.ReviewPageModel")!])
            .SelectMany(type => type.GetProperties())
            .ToArray();

        Assert.NotEmpty(modelProperties);

        Assert.DoesNotContain(
            modelProperties,
            property =>
                property.PropertyType == typeof(Guid) ||
                property.PropertyType == typeof(DateTimeOffset) ||
                property.PropertyType == typeof(decimal) ||
                property.Name.Contains("Id", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Time", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Address", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Email", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Phone", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Account", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Spec 014 AC-94 — the migrated model omits taxpayer values from serialization
    /// and from its own rendering, so a future diagnostic sink cannot make one
    /// visible by serializing or logging whatever it is handed.
    /// </summary>
    [Fact]
    public void TheBrowserModelSerializesAndRendersNoTaxpayerValue()
    {
        var assembly = typeof(Program).Assembly;
        var pageModelType = assembly.GetType(
            "TaxAI.Portal.Components.Pages.ReviewPageModel",
            throwOnError: true)!;

        foreach (var type in new[]
                 {
                     pageModelType,
                     assembly.GetType("TaxAI.Portal.Components.Pages.ReviewSlipModel", true)!,
                     assembly.GetType("TaxAI.Portal.Components.Pages.ReviewBoxModel", true)!,
                     assembly.GetType("TaxAI.Portal.Components.Pages.ReviewLineModel", true)!,
                 })
        {
            Assert.All(
                type.GetProperties(),
                property => Assert.Contains(
                    property.GetCustomAttributes(),
                    attribute => attribute.GetType().Name == "JsonIgnoreAttribute"));

            // Structural ToString, declared by hand: the compiler-generated record
            // ToString prints every property, including the omitted ones.
            Assert.NotNull(type.GetMethod(
                "ToString",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                Type.EmptyTypes));
        }

        var box = Activator.CreateInstance(
            assembly.GetType("TaxAI.Portal.Components.Pages.ReviewBoxModel", true)!,
            "box-marker",
            "12.34")!;

        Assert.DoesNotContain("box-marker", box.ToString()!, StringComparison.Ordinal);
        Assert.DoesNotContain("12.34", box.ToString()!, StringComparison.Ordinal);
    }

#pragma warning disable CA1859
    private static string[] PropertyNames(Assembly assembly, string typeName) =>
        assembly
            .GetType(typeName, throwOnError: true)!
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
#pragma warning restore CA1859

    private static async Task<string> SnapshotAsync(HttpResponseMessage response)
    {
        var headers = response.Headers
            .Concat(response.Content.Headers)
            .Select(header =>
                $"{header.Key}:{string.Join(",", header.Value)}")
            .OrderBy(value => value, StringComparer.Ordinal);

        return string.Join(
            "\n",
            ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
            string.Join("|", headers),
            await response.Content.ReadAsStringAsync());
    }

    private static void AssertAppearsBefore(
        string body,
        string first,
        string second)
    {
        var firstIndex = body.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = body.IndexOf(second, StringComparison.Ordinal);

        Assert.True(
            firstIndex >= 0 && secondIndex > firstIndex,
            $"Expected '{first}' before '{second}'.");
    }

    private static string ReviewPath(Guid recordId) =>
        $"/reviews/{recordId:D}";

    private static PersistedReviewRecord CompleteRecord(
        Guid firmId,
        Guid recordId,
        int version) =>
        Record(
            firmId,
            recordId,
            version,
            slips:
            [
                Slip(
                    firmId,
                    recordId,
                    taxpayer: "<Taxpayer & Partner>",
                    employer: "Employer \"Quoted\"",
                    boxes:
                    [
                        Box(firmId, "box-second", 2.0050m),
                        Box(firmId, "box-first", -1.005m),
                    ],
                    lines:
                    [
                        Line(
                            firmId,
                            "line-second",
                            4.0050m,
                            ["source-second", "source-first"]),
                        Line(firmId, "line-first", 0m, ["source-zero"]),
                        Line(firmId, "line-second", 6.0050m, ["source-repeat"]),
                    ],
                    unmappedCodes:
                    [
                        Unmapped(firmId, "unmapped-second"),
                        Unmapped(firmId, "unmapped-first"),
                    ]),
            ]);

    private static PersistedReviewRecord Record(
        Guid firmId,
        Guid recordId,
        int version,
        IReadOnlyList<PersistedSlip> slips) =>
        PortalAuthorizationHarness.Construct<PersistedReviewRecord>(
            recordId,
            firmId,
            "case-secret-alpha",
            "participant-secret-alpha",
            2025,
            version,
            new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero),
            new DateTimeOffset(2037, 1, 1, 0, 0, 0, TimeSpan.Zero),
            slips);

    private static PersistedSlip Slip(
        Guid firmId,
        Guid recordId,
        string taxpayer = "Example Taxpayer",
        string employer = "Example Employer",
        IReadOnlyList<PersistedBoxAmount>? boxes = null,
        IReadOnlyList<PersistedLineEntry>? lines = null,
        IReadOnlyList<PersistedUnmappedBoxCode>? unmappedCodes = null) =>
        PortalAuthorizationHarness.Construct<PersistedSlip>(
            Guid.NewGuid(),
            firmId,
            recordId,
            0,
            "T4",
            Guid.NewGuid(),
            taxpayer,
            employer,
            boxes ?? [Box(firmId, "box-default", 1m)],
            lines ?? [Line(firmId, "line-default", 1m, ["source-default"])],
            unmappedCodes ?? [Unmapped(firmId, "unmapped-default")]);

    private static PersistedBoxAmount Box(
        Guid firmId,
        string code,
        decimal amount) =>
        PortalAuthorizationHarness.Construct<PersistedBoxAmount>(
            Guid.NewGuid(),
            firmId,
            Guid.NewGuid(),
            0,
            code,
            amount);

    private static PersistedLineEntry Line(
        Guid firmId,
        string code,
        decimal amount,
        IReadOnlyList<string> sourceCodes) =>
        PortalAuthorizationHarness.Construct<PersistedLineEntry>(
            Guid.NewGuid(),
            firmId,
            Guid.NewGuid(),
            0,
            code,
            amount,
            sourceCodes
                .Select(source => Source(firmId, source))
                .ToArray());

    private static PersistedSourceBoxCode Source(
        Guid firmId,
        string code) =>
        PortalAuthorizationHarness.Construct<PersistedSourceBoxCode>(
            Guid.NewGuid(),
            firmId,
            Guid.NewGuid(),
            0,
            code);

    private static PersistedUnmappedBoxCode Unmapped(
        Guid firmId,
        string code) =>
        PortalAuthorizationHarness.Construct<PersistedUnmappedBoxCode>(
            Guid.NewGuid(),
            firmId,
            Guid.NewGuid(),
            0,
            code);

    private sealed class ReviewHost : IDisposable
    {
        private readonly WebApplicationFactory<Program> _baseHost;

        private readonly WebApplicationFactory<Program> _host;

        internal ReviewHost(
            Guid firmId,
            RecordingReviewRepository repository,
            RecordingLoggerProvider? logger = null)
        {
            Directory = new PortalAuthorizationHarness.IdentityDirectory();
            Sessions = new PortalAuthorizationHarness.SessionStore();
            User = Directory.AddUser();
            Membership = Directory.AddMembership(
                User.UserId,
                "Example Firm",
                firmId: firmId);

            _baseHost = PortalAuthorizationHarness.CreateHost(Directory, Sessions);
            _host = _baseHost.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IReviewRecordRepository>();
                    services.AddSingleton<IReviewRecordRepository>(repository);
                });

                if (logger is not null)
                {
                    builder.ConfigureLogging(logging => logging.AddProvider(logger));
                }
            });
        }

        internal PortalAuthorizationHarness.IdentityDirectory Directory { get; }

        internal PortalAuthorizationHarness.SessionStore Sessions { get; }

        internal PortalUser User { get; }

        internal FirmMembership Membership { get; }

        internal HttpClient CreateClient() =>
            PortalAuthorizationHarness.CreateClient(_host);

        internal Task SignInAsync(HttpClient client) =>
            PortalAuthorizationHarness.SignInAsync(client);

        public void Dispose()
        {
            _host.Dispose();
            _baseHost.Dispose();
        }
    }

    private sealed class RecordingReviewRepository : IReviewRecordRepository
    {
        private readonly Dictionary<(Guid FirmId, Guid RecordId), PersistedReviewRecord>
            _records = new();

        internal List<ReadCall> Reads { get; } = new();

        internal int SaveCalls { get; private set; }

        internal string? FailureMessage { get; init; }

        internal void Add(PersistedReviewRecord record) =>
            _records[(record.FirmId, record.RecordId)] = record;

        public Task<ParseResult<PersistedReviewRecord>> SaveAsync(
            TrustedFirmId firm,
            PersistedReviewRecord record,
            CancellationToken cancellationToken)
        {
            SaveCalls++;
            throw new InvalidOperationException("The read-only test repository was asked to save.");
        }

        public Task<PersistedReviewRecord?> FindAsync(
            TrustedFirmId firm,
            Guid recordId,
            CancellationToken cancellationToken)
        {
            Reads.Add(new ReadCall(firm, recordId, cancellationToken));

            if (FailureMessage is not null)
            {
                throw new InvalidOperationException(FailureMessage);
            }

            return Task.FromResult(
                _records.TryGetValue((firm.Value, recordId), out var record)
                    ? record
                    : null);
        }
    }

    private sealed record ReadCall(
        TrustedFirmId Firm,
        Guid RecordId,
        CancellationToken CancellationToken);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        internal IReadOnlyList<string> Messages => _messages.ToArray();

        public ILogger CreateLogger(string categoryName) =>
            new RecordingLogger(categoryName, _messages);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly string _categoryName;

            private readonly ConcurrentQueue<string> _messages;

            internal RecordingLogger(
                string categoryName,
                ConcurrentQueue<string> messages)
            {
                _categoryName = categoryName;
                _messages = messages;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull =>
                null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _messages.Enqueue(
                    string.Join(
                        "|",
                        _categoryName,
                        logLevel,
                        formatter(state, exception),
                        exception?.ToString() ?? string.Empty));
            }
        }
    }
}
