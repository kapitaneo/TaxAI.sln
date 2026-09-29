using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ClientIntake.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxCases.Models;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 014 T04 — the integrated security proof over the real portal
/// composition, the real workflows, and the real Development encrypted content
/// adapter.
/// <para>
/// Two claims this file is careful about. First, the SIN-free claim covers every
/// <b>structured</b> surface — browser models, HTML, URLs, cookies, headers,
/// stored metadata, audit, logs, and diagnostics — and explicitly <b>excludes</b>
/// the raw synthetic source bytes, which are the one place a real T4 image would
/// legitimately carry one. Second, every probe here is assembled at runtime and
/// is obviously synthetic; none is a real identifier.
/// </para>
/// </summary>
public sealed class ClientIntakeSecurityAcceptanceTests
{
    private static string SinProbe => string.Concat("046", "454", "286");

    private static IReadOnlyList<string> SinShapes =>
    [
        SinProbe,
        string.Join("-", "046", "454", "286"),
        string.Join(" ", "046", "454", "286"),
        string.Join("-", "***", "***", "286"),
        string.Join("-", "xxx", "xxx", "286"),
        "286",
    ];

    // ------------------------------------------------- safe refusal equivalence

    [Fact] // AC-61, AC-22, AC-18, AC-17
    public async Task EveryInvalidClientResourceProducesTheSameEmptyRefusal()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        // One case whose link is spent, and one whose link is revoked.
        var spent = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);
        using var spender = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(spender, spent.Credential);

        var revoked = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);
        (await IntakeFlow.RevokeAsync(accountant, revoked.CaseId)).Dispose();

        using var probe = world.FirmOne.CreateAnonymousClient();
        var refusals = new List<(string Case, HttpStatusCode Status, string Body, string Headers)>();

        foreach (var (name, path) in new[]
                 {
                     ("malformed", "/client-intake/link/not-a-canonical-token"),
                     ("wrong-length", $"/client-intake/link/{new string('A', 42)}"),
                     ("unknown", $"/client-intake/link/{RandomCredential()}"),
                     ("spent-replay", $"/client-intake/link/{spent.Credential}"),
                     ("revoked", $"/client-intake/link/{revoked.Credential}"),
                     ("no-session", "/client-intake"),
                 })
        {
            using var response = await probe.GetAsync(path);
            refusals.Add((
                name,
                response.StatusCode,
                await response.Content.ReadAsStringAsync(),
                Normalize(response)));
        }

        Assert.All(refusals, refusal =>
        {
            Assert.Equal(HttpStatusCode.NotFound, refusal.Status);
            Assert.Equal(string.Empty, refusal.Body);
        });

        // Status, body, and the security/cache header set are identical across
        // every refusal: nothing distinguishes "never existed" from "revoked".
        var distinct = refusals
            .Select(refusal => refusal.Headers)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            distinct.Length == 1,
            $"Refusals differed by headers: {string.Join(" | ", distinct)}");

        // Nothing was mutated by any refusal.
        Assert.Equal(2, world.Store.Count);
        Assert.All(world.Store.All, stored => Assert.Empty(stored.Documents));
    }

    [Fact] // AC-59, AC-60
    public async Task ForeignFirmAndForeignCaseSelectorsAreByteIdenticalToMissing()
    {
        await using var world = IntakeWorld.Create();
        using var firstAccountant = world.FirmOne.CreateAccountantClient();
        using var secondAccountant = world.FirmTwo.CreateAccountantClient();
        await world.FirmOne.SignInAsync(firstAccountant);
        await world.FirmTwo.SignInAsync(secondAccountant);

        var owned = await IntakeFlow.CreateCaseAsync(firstAccountant, 1, 0);

        // Firm two asks for firm one's case, and for a case that never existed.
        using var foreign = await secondAccountant.GetAsync(
            $"/intake-cases/{owned.CaseId:D}");
        using var missing = await secondAccountant.GetAsync(
            $"/intake-cases/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(foreign.StatusCode, missing.StatusCode);
        Assert.Equal(string.Empty, await foreign.Content.ReadAsStringAsync());
        Assert.Equal(
            await foreign.Content.ReadAsStringAsync(),
            await missing.Content.ReadAsStringAsync());
        Assert.Equal(Normalize(foreign), Normalize(missing));

        // The case is still readable by the firm that owns it, so the refusal
        // above was a scope decision rather than a missing row.
        using var owner = await firstAccountant.GetAsync(
            $"/intake-cases/{owned.CaseId:D}");
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact] // AC-43
    public async Task IdenticalBytesInTwoFirmsRevealNothingAboutEachOther()
    {
        await using var world = IntakeWorld.Create();
        using var firstAccountant = world.FirmOne.CreateAccountantClient();
        using var secondAccountant = world.FirmTwo.CreateAccountantClient();
        await world.FirmOne.SignInAsync(firstAccountant);
        await world.FirmTwo.SignInAsync(secondAccountant);

        var body = SyntheticSource.Pdf($"shared body {SinProbe}");

        var firstCase = await IntakeFlow.CreateCaseAsync(firstAccountant, 1, 0);
        using var firstClient = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(firstClient, firstCase.Credential);
        using var firstUpload = await IntakeFlow.UploadAsync(
            firstClient,
            DocumentCategory.T4Slip,
            1,
            IntakeMediaKind.Pdf,
            body);

        var secondCase = await IntakeFlow.CreateCaseAsync(secondAccountant, 1, 0);
        using var secondClient = world.FirmTwo.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(secondClient, secondCase.Credential);
        using var secondUpload = await IntakeFlow.UploadAsync(
            secondClient,
            DocumentCategory.T4Slip,
            1,
            IntakeMediaKind.Pdf,
            body);

        // Both are evaluated as new within their own server-derived case, and
        // the two responses are indistinguishable.
        Assert.Equal(HttpStatusCode.OK, firstUpload.StatusCode);
        Assert.Equal(firstUpload.StatusCode, secondUpload.StatusCode);

        var firstBody = await firstUpload.Content.ReadAsStringAsync();
        var secondBody = await secondUpload.Content.ReadAsStringAsync();

        // The two hosts have independent data-protection key rings, so the
        // framework's protected component descriptors necessarily differ. What
        // must match is everything the reader can actually see, and what must be
        // absent is any trace of the other scope.
        Assert.Equal(
            StripProtectedMarkers(firstBody),
            StripProtectedMarkers(secondBody));

        foreach (var (rendered, foreignIdentifiers) in new[]
                 {
                     (firstBody, ScopeIdentifiers(world, secondCase.CaseId)),
                     (secondBody, ScopeIdentifiers(world, firstCase.CaseId)),
                 })
        {
            Assert.All(
                foreignIdentifiers,
                identifier => Assert.DoesNotContain(
                    identifier,
                    rendered,
                    StringComparison.OrdinalIgnoreCase));
        }

        // The two scope-derived fingerprints of identical bytes are different.
        var fingerprints = world.Store.All
            .SelectMany(stored => stored.Documents)
            .Select(document => Convert.ToBase64String(document.Fingerprint.ToArray()))
            .ToArray();
        Assert.Equal(2, fingerprints.Length);
        Assert.Equal(2, fingerprints.Distinct(StringComparer.Ordinal).Count());
    }

    // ------------------------------------- revocation between render and action

    [Fact] // AC-97
    public async Task ARevokedAccountantMembershipRefusesTheNextOperation()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);

        // The accountant holds a rendered page and a valid antiforgery token.
        using var rendered = await accountant.GetAsync(
            $"/intake-cases/{issued.CaseId:D}");
        Assert.Equal(HttpStatusCode.OK, rendered.StatusCode);
        var token = IntakeFlow.AntiforgeryToken(
            await rendered.Content.ReadAsStringAsync());

        // Membership is revoked out from under it.
        world.FirmOne.Directory.SetMembershipActive(
            world.FirmOne.Membership.MembershipId,
            isActive: false);
        var beforeVersion = world.Store.Single().ConcurrencyVersion;

        using var refused = await accountant.PostAsync(
            $"/intake-cases/{issued.CaseId:D}/link/reissue",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                }));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(beforeVersion, world.Store.Single().ConcurrencyVersion);
    }

    [Fact] // AC-22, AC-97
    public async Task ARevokedClientSessionRefusesTheNextOperation()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);

        using var client = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(client, issued.Credential);

        using var page = await client.GetAsync("/client-intake");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = IntakeFlow.AntiforgeryToken(await page.Content.ReadAsStringAsync());

        // The accountant revokes the link, which invalidates the derived session.
        world.Store.RevokeAllSessions(DateTimeOffset.UtcNow);

        using var refused = await client.PostAsync(
            "/client-intake/answers",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["t4Answer"] = nameof(AvailabilityAnswerCode.AvailableNow),
                }));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Equal(string.Empty, await refused.Content.ReadAsStringAsync());
        Assert.Empty(world.Store.Single().AnswerVersions);
    }

    [Fact] // AC-23
    public async Task AFailingIntakeStoreFailsClosedWithoutATaxpayerResponse()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);

        using var client = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(client, issued.Credential);

        world.Store.FailNextRead = true;
        using var response = await client.GetAsync("/client-intake");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(string.Empty, body);
        Assert.DoesNotContain(
            IntakeFlow.DisplayName,
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("unavailable", body, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------- content, filenames, and no-SIN

    [Fact] // AC-40, AC-64, AC-65, AC-47
    public async Task SinProbesAndFilenamesSurviveOnlyInsideTheEncryptedObject()
    {
        var logs = new RecordingLoggerProvider();
        await using var world = IntakeWorld.Create(
            builder => builder.ConfigureLogging(
                logging => logging.AddProvider(logs)));

        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);

        using var client = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(client, issued.Credential);

        // The transport sends the fixed neutral name; the planted original name
        // is what a browser would otherwise have disclosed.
        var plantedFilename = $"T4-{SinProbe}-<script>alert(1)</script>.pdf";
        var body = SyntheticSource.Pdf(
            $"{SinProbe} {string.Join("-", "046", "454", "286")} -1.005 1.005 Box 14");

        var browserSurfaces = new List<string>();
        using (var upload = await IntakeFlow.UploadAsync(
                   client,
                   DocumentCategory.T4Slip,
                   1,
                   IntakeMediaKind.Pdf,
                   body,
                   transportFilename: "upload.bin"))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            browserSurfaces.Add(await upload.Content.ReadAsStringAsync());
            browserSurfaces.Add(upload.Headers.ToString());
        }

        // A second upload that carries the planted name on the wire: even then,
        // the received filename is discarded before anything is stored.
        using (var planted = await IntakeFlow.UploadAsync(
                   client,
                   DocumentCategory.T4Slip,
                   1,
                   IntakeMediaKind.Pdf,
                   SyntheticSource.Pdf("second distinct body"),
                   transportFilename: plantedFilename))
        {
            browserSurfaces.Add(await planted.Content.ReadAsStringAsync());
            browserSurfaces.Add(planted.Headers.ToString());
        }

        (await IntakeFlow.SubmitAsync(client)).Dispose();

        using (var clientPage = await client.GetAsync("/client-intake"))
        {
            browserSurfaces.Add(await clientPage.Content.ReadAsStringAsync());
            browserSurfaces.Add(clientPage.Headers.ToString());
        }

        using (var progress = await accountant.GetAsync(
                   $"/intake-cases/{issued.CaseId:D}"))
        {
            browserSurfaces.Add(await progress.Content.ReadAsStringAsync());
            browserSurfaces.Add(progress.Headers.ToString());
        }

        // Structured stored metadata, rendered as text.
        var metadata = world.Store.All
            .SelectMany(stored => new[]
            {
                stored.ToString(),
                string.Join(",", stored.Documents.Select(document => document.ToString())),
                string.Join(",", stored.Documents.Select(document => document.ContentKey)),
                string.Join(",", stored.UploadAttempts.Select(attempt => attempt.ToString())),
                string.Join(
                    ",",
                    world.Store
                        .Audit(stored.Firm.Value, stored.CaseId)
                        .Select(audit => audit.ToString())),
            })
            .ToArray();

        var scanned = browserSurfaces
            .Concat(metadata)
            .Concat(logs.Messages)
            .ToArray();

        Assert.NotEmpty(scanned);

        foreach (var probe in SinShapes
                     .Concat([plantedFilename, "T4-046", "alert(1)", ".pdf"]))
        {
            Assert.All(
                scanned,
                value => Assert.DoesNotContain(
                    probe,
                    value,
                    StringComparison.OrdinalIgnoreCase));
        }

        // The probe is retained only inside the opaque encrypted object, and
        // this suite makes no claim that the source document is SIN-free.
        var objects = Directory
            .EnumerateFiles(IntakeWorld.ContentRoot, "*", SearchOption.AllDirectories)
            .ToArray();
        Assert.NotEmpty(objects);
        Assert.All(objects, path =>
        {
            var raw = File.ReadAllBytes(path);
            Assert.DoesNotContain(
                SinProbe,
                Encoding.UTF8.GetString(raw),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                Path.GetFileName(path),
                plantedFilename,
                StringComparison.OrdinalIgnoreCase);
        });

        // The staged object lives outside the web root by construction.
        var webRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "wwwroot"));
        Assert.All(objects, path =>
            Assert.False(
                Path.GetFullPath(path).StartsWith(webRoot, StringComparison.OrdinalIgnoreCase),
                "A staged object was written inside the web root."));
    }

    [Fact] // AC-64, AC-66
    public async Task NoBrowserModelDeclaresASinOrExcludedIdentifierField()
    {
        var browserModels = new[]
        {
            typeof(ClientIntakeView),
            typeof(AccountantIntakeView),
            typeof(IntakeQuestionView),
            typeof(ExpectedDocumentView),
            typeof(IntakeLinkInspection),
            typeof(CreatedIntakeCase),
            typeof(IssuedIntakeLink),
            typeof(RedeemedIntakeSession),
        };

        foreach (var model in browserModels)
        {
            var members = model
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .ToArray();

            Assert.NotEmpty(members);

            foreach (var forbidden in new[]
                     {
                         "Sin",
                         "SocialInsurance",
                         "Masked",
                         "Redact",
                         "LastThree",
                         "LastFour",
                         "DateOfBirth",
                         "Address",
                         "Email",
                         "Phone",
                         "FileName",
                         "Filename",
                         "Content",
                         "Fingerprint",
                     })
            {
                Assert.DoesNotContain(
                    members,
                    member => member.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
            }
        }

        // The two browser projections carry no firm, case, participant, session,
        // or membership identifier at all.
        foreach (var model in new[] { typeof(ClientIntakeView), typeof(IntakeQuestionView) })
        {
            Assert.DoesNotContain(
                model.GetProperties(BindingFlags.Public | BindingFlags.Instance),
                property => property.Name.EndsWith("Id", StringComparison.Ordinal));
        }

        await Task.CompletedTask;
    }

    // ------------------------------------------------ no raw-content read route

    [Fact] // AC-4 of FR-55, and the raw-content gate
    public async Task NoMappedRouteReadsPreviewsOrDownloadsSourceContent()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);
        using var client = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(client, issued.Credential);
        using (var upload = await IntakeFlow.UploadAsync(
                   client,
                   DocumentCategory.T4Slip,
                   1,
                   IntakeMediaKind.Pdf,
                   SyntheticSource.Pdf($"body {SinProbe}")))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        var contentKey = world.Store.Single().Documents[0].ContentKey;

        // No route serves the object under any plausible spelling.
        foreach (var candidate in new[]
                 {
                     $"/client-intake/documents/{contentKey}",
                     $"/client-intake/content/{contentKey}",
                     $"/intake-cases/{issued.CaseId:D}/documents/{contentKey}",
                     $"/{contentKey}",
                     $"/documents/{contentKey}",
                 })
        {
            using var anonymous = await client.GetAsync(candidate);
            using var authenticated = await accountant.GetAsync(candidate);

            Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, authenticated.StatusCode);
        }

        // And the route inventory itself declares no read/preview/download path.
        var endpoints = world.FirmOne
            .Resolve<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToArray();

        Assert.NotEmpty(endpoints);
        foreach (var forbidden in new[]
                 {
                     "download",
                     "preview",
                     "raw",
                     "contentKey",
                     "file",
                 })
        {
            Assert.DoesNotContain(
                endpoints,
                pattern => pattern.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }

        // The one GET on /client-intake/documents does not exist: it is POST only.
        var documentRoutes = endpoints
            .Where(pattern => pattern.Contains(
                "client-intake/documents",
                StringComparison.Ordinal))
            .ToArray();
        Assert.Single(documentRoutes);

        using var methodProbe = await client.GetAsync("/client-intake/documents");
        Assert.NotEqual(HttpStatusCode.OK, methodProbe.StatusCode);
    }

    // ------------------------------- no extraction, tax, or review manufacture

    [Fact] // AC-75, AC-77
    public async Task NoExtractionParserMapperOrReviewAssemblerIsInTheRuntimeGraph()
    {
        await using var world = IntakeWorld.Create();

        // The portal's compiled reference set excludes the review assembler and
        // the extraction contract entirely.
        var referenced = typeof(Program).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.DoesNotContain(
            referenced,
            name => name.Contains("CaseReview", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            referenced,
            name => name.Contains("DocumentExtraction", StringComparison.OrdinalIgnoreCase));

        // And nothing that could extract, parse, map, or assemble is registered.
        foreach (var forbidden in new[]
                 {
                     "IDocumentExtractionIngress",
                     "ExtractionEnvelopeValidator",
                     "T4SlipParser",
                     "T5SlipParser",
                     "T4LineMapper",
                     "T5LineMapper",
                     "ReviewPackageAssembler",
                     "MoneyRounding",
                 })
        {
            Assert.DoesNotContain(
                world.FirmOne.RegisteredServiceNames(),
                name => name.Contains(forbidden, StringComparison.Ordinal));
        }

        // The whole intake flow runs without one review record being created.
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.ReadyCaseAsync(world, accountant);
        _ = issued;

        Assert.Equal(0, world.Reviews.SaveCalls);
    }

    [Fact] // AC-77, AC-78
    public async Task NoIntakeProductionSourceNamesACraLineBoxTableOrRounding()
    {
        var intakeSources = ProductionSources("ClientIntake")
            .Concat(PortalIntakeSources())
            .ToArray();

        Assert.NotEmpty(intakeSources);

        var fiveDigitLiteral = new Regex(
            @"\b\d{5}\b",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        foreach (var path in intakeSources)
        {
            var executable = ExecutableLines(path).ToArray();
            var joined = string.Join("\n", executable);

            foreach (var forbidden in new[]
                     {
                         "MoneyRounding",
                         "Math.Round",
                         "MidpointRounding",
                         "T4LineMap",
                         "T5LineMap",
                         "BoxLineMapping",
                         "TaxLineEntry",
                         "RecipientIdentifier",
                         " double ",
                         " float ",
                     })
            {
                Assert.DoesNotContain(forbidden, joined, StringComparison.Ordinal);
            }

            Assert.DoesNotContain(
                executable,
                line => fiveDigitLiteral.IsMatch(line));
        }

        await Task.CompletedTask;
    }

    [Fact] // AC-81
    public async Task NoRetentionOrPurgeRuleWasIntroducedForIntakeOrSourceContent()
    {
        var intakeSources = ProductionSources("ClientIntake")
            .Concat(PortalIntakeSources())
            .ToArray();

        foreach (var path in intakeSources)
        {
            var text = WithoutApprovedCleanupMembers(path, string.Join("\n", ExecutableLines(path)));

            foreach (var forbidden in new[]
                     {
                         "RetentionUntilUtc",
                         "seven-year",
                         "SevenYear",
                         "PurgeAsync",
                         "IReviewRecordPurgeService",
                     })
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Spec 016 T04 narrows this prohibition by exactly two declarations in one
    /// file: the source registry's compensating-cleanup record and the scoped
    /// physical purger of an object that never became reachable. No retention
    /// rule, deadline, due purge or other purge member is allowed, and the same
    /// file is still scanned for everything else.
    /// </summary>
    internal static readonly string[] ApprovedCleanupDeclarations =
    [
        "Task<SourceRegistryWriteCode> RecordPurgeAsync(",
        "Task<SourceObjectPurgeOutcome> PurgeAsync(",
    ];

    internal static string WithoutApprovedCleanupMembers(string path, string executable)
    {
        if (!string.Equals(Path.GetFileName(path), "SourceRegistryContracts.cs", StringComparison.Ordinal))
        {
            return executable;
        }

        foreach (var declaration in ApprovedCleanupDeclarations)
        {
            Assert.Equal(1, CountOf(executable, declaration));
            executable = executable.Replace(declaration, "Task<approved>(", StringComparison.Ordinal);
        }

        return executable;
    }

    [Fact] // spec 016 T04: the narrowed scan still catches any other purge member.
    public async Task TheNarrowedIntakePurgeScanStillCatchesAnyOtherPurgeMember()
    {
        var contracts = ProductionSources("ClientIntake")
            .Single(path => Path.GetFileName(path) == "SourceRegistryContracts.cs");
        var actual = string.Join("\n", ExecutableLines(contracts));
        var planted = actual + "\nTask<int> PurgeDueSourcesAsync(TrustedFirmId firm);\nDateTimeOffset RetentionUntilUtc { get; }";

        Assert.DoesNotContain("PurgeAsync", WithoutApprovedCleanupMembers(contracts, actual), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PurgeDueSourcesAsync", WithoutApprovedCleanupMembers(contracts, planted), StringComparison.Ordinal);
        Assert.Contains("RetentionUntilUtc", WithoutApprovedCleanupMembers(contracts, planted), StringComparison.Ordinal);
        Assert.Contains("PurgeAsync", WithoutApprovedCleanupMembers("Other.cs", actual), StringComparison.Ordinal);

        await Task.CompletedTask;
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Removes the framework's Blazor component-state markers. They are random
    /// per render and Data-Protection-protected per host, so two otherwise
    /// identical bodies never compare equal; they carry the client allowlist
    /// only, which the other assertions in this suite already pin.
    /// </summary>
    private static string StripProtectedMarkers(string html)
    {
        var withoutComponentState = Regex.Replace(
            html,
            "<!--Blazor:.*?-->",
            "<!--Blazor:normalized-->",
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));

        // Every Data-Protection payload this host emits — antiforgery tokens,
        // redemption handles, component descriptors — is prefixed "CfDJ8" and is
        // key-ring-specific by design.
        return Regex.Replace(
            withoutComponentState,
            "CfDJ8[A-Za-z0-9\\-_\\\\u]+",
            "protected-normalized",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    /// <summary>Every identifier that belongs to one case's scope.</summary>
    private static IReadOnlyList<string> ScopeIdentifiers(
        IntakeWorld world,
        Guid caseId)
    {
        var stored = world.Store.All.Single(candidate => candidate.CaseId == caseId);
        return
        [
            stored.CaseId.ToString("D"),
            stored.Firm.Value.ToString("D"),
            stored.PrimaryParticipantId.ToString("D"),
            .. stored.Documents.Select(document => document.ContentKey),
            .. stored.Documents.Select(document =>
                Convert.ToBase64String(document.Fingerprint.ToArray())),
        ];
    }

    private static string RandomCredential()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// A stable rendering of the security and cache headers, so two refusals can
    /// be compared without their per-request values.
    /// </summary>
    private static string Normalize(HttpResponseMessage response) =>
        string.Join(
            "|",
            response.Headers
                .Concat(response.Content.Headers)
                .Where(header => !string.Equals(
                    header.Key,
                    "Date",
                    StringComparison.OrdinalIgnoreCase))
                .Select(header =>
                    $"{header.Key}={string.Join(",", header.Value)}")
                .OrderBy(entry => entry, StringComparer.Ordinal));

    private static IEnumerable<string> ProductionSources(string module) =>
        Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", module),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path));

    private static IEnumerable<string> PortalIntakeSources() =>
        Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "TaxAI.Portal"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => Path.GetFileName(path)
                .Contains("Intake", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find the repository root containing TaxAI.sln.");
    }

    internal sealed class RecordingLoggerProvider : ILoggerProvider
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
                where TState : notnull => null;

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
                        " ",
                        _categoryName,
                        formatter(state, exception),
                        exception?.ToString() ?? string.Empty));
            }
        }
    }
}
