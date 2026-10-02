using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ClientIntake.Models;
using DocumentExtraction.Models;
using DocumentIntelligence;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxAI.Portal.Tests.Production;
using TaxCases.Models;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 017 T02 — the bridge between the client upload route and the
/// deterministic Development provider, proved through the real multipart
/// transport.
/// <para>
/// Earlier extraction suites staged source bytes straight into the content
/// store, so no test had ever sent a file through the unchanged upload gate and
/// then extracted it. Here every source reaches the store only as a browser's
/// upload would: <c>POST /client-intake/documents</c>, with the signature gate,
/// streaming, and encryption all real. Only the SQL-backed collaborators are
/// substituted, exactly as in the spec 015 portal suite.
/// </para>
/// <para>
/// The recipient-identifier value is assembled at run time. No nine-digit
/// literal exists in this file.
/// </para>
/// </summary>
public sealed class UploadExtractionBridgeTests
{
    /// <summary>The planning baseline of spec 017 (merge of PR #152).</summary>
    private const string Baseline = "7ee2574e246d574fb328d185db56fb8a1894b64d";

    private const string Signature = "%PDF-1.7";

    private const string Header = DeterministicDocumentIntelligenceProvider.DocumentHeader;

    private const string ProductionAdapterDirectory = "src/DocumentIntelligence/Production";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    // ----------------------------------------------------------------- AC-36

    [Fact] // AC-36
    public async Task ABridgedT4PassesTheUploadGateAndExtractsAsAT4()
    {
        await using var world = ExtractionPortalWorld.Create();
        var recipientIdentifier = NewRecipientIdentifier();

        var uploaded = await UploadAsync(world, T4Source(2025, recipientIdentifier));
        Assert.Equal(HttpStatusCode.OK, uploaded.T4Status);

        using var accountant = await SignedInAccountantAsync(world);
        var captures = new List<ExtractionHttpCapture>
        {
            await ExtractionPortalHttp.ExtractAsync(accountant, uploaded.CaseId, uploaded.T4DocumentId),
        };

        var document = await FindAsync(world, uploaded, uploaded.T4DocumentId);
        var version = Assert.Single(document!.Versions);

        Assert.Equal(
            DocumentClassification.T4Slip,
            version.Classification.AutomatedClassification);
        Assert.False(version.Classification.IsOverridden);
        Assert.Empty(version.FieldOutcomes);
        Assert.Equal("Synthetic Employer Ltd.", version.EmployerDisplayName);
        Assert.Equal("Synthetic Employee", version.EmployeeDisplayName);
        Assert.Equal(
            new[] { ("14", "50000.00"), ("45", "1") },
            version.BoxValues.Select(box => (box.Code, box.Value)).ToArray());
        Assert.Empty(document.MedicalEvidenceVersions);

        // The bridged source continues through the unchanged chain to a review
        // record: the signature line changed how the bytes were recognized and
        // nothing about what the engine made of them.
        captures.Add(await ExtractionPortalHttp.PostReviewMutationAsync(
            accountant,
            uploaded.CaseId,
            uploaded.T4DocumentId,
            $"/intake-cases/{uploaded.CaseId:D}/extractions/{uploaded.T4DocumentId:D}/accept",
            new Dictionary<string, string>()));
        var finalized = await ExtractionPortalHttp.FinalizeAsync(
            accountant, uploaded.CaseId, uploaded.T4DocumentId);
        captures.Add(finalized);

        Assert.Equal(HttpStatusCode.OK, finalized.StatusCode);
        Assert.Contains("Accountant review", finalized.Body, StringComparison.Ordinal);
        Assert.Contains("50000.00", finalized.Body, StringComparison.Ordinal);
        var record = Assert.Single(world.Reviews.All);
        Assert.Single(record.Slips);

        // The recipient identifier was read from the uploaded bytes for one
        // parser call and reached no response, URL, or header.
        var allHttp = string.Join("\n", captures.Select(capture => capture.All));
        Assert.DoesNotContain(recipientIdentifier, allHttp, StringComparison.Ordinal);
        Assert.DoesNotContain(Header, allHttp, StringComparison.Ordinal);
    }

    [Fact] // AC-36: the same bridge serves the medical sample.
    public async Task ABridgedMedicalReceiptPassesTheUploadGateAndExtractsAsMedicalEvidence()
    {
        await using var world = ExtractionPortalWorld.Create();

        var uploaded = await UploadAsync(
            world,
            T4Source(2025, NewRecipientIdentifier()),
            MedicalSource());
        Assert.Equal(HttpStatusCode.OK, uploaded.MedicalStatus);

        using var accountant = await SignedInAccountantAsync(world);
        await ExtractionPortalHttp.ExtractAsync(
            accountant, uploaded.CaseId, uploaded.MedicalDocumentId);

        var document = await FindAsync(world, uploaded, uploaded.MedicalDocumentId);
        var evidence = Assert.Single(document!.MedicalEvidenceVersions);

        Assert.Empty(document.Versions);
        Assert.Equal("Synthetic Dental Clinic", evidence.ProviderDisplayName);
        Assert.Equal("120.50", evidence.GrossAmountAsPrinted);
        Assert.Equal("2025-03-14", evidence.ServiceDateAsPrinted);
    }

    // ----------------------------------------------------------------- AC-38

    [Fact] // AC-38
    public async Task ABridgedT4PrintingAnotherYearFailsYearCoherenceAndProducesNothing()
    {
        await using var world = ExtractionPortalWorld.Create();

        var uploaded = await UploadAsync(world, T4Source(2024, NewRecipientIdentifier()));
        Assert.Equal(HttpStatusCode.OK, uploaded.T4Status);

        using var accountant = await SignedInAccountantAsync(world);
        await ExtractionPortalHttp.ExtractAsync(accountant, uploaded.CaseId, uploaded.T4DocumentId);

        // The existing envelope validator refused the pair. The recorded
        // version carries that refusal and no engine result.
        var extracted = await FindAsync(world, uploaded, uploaded.T4DocumentId);
        var version = Assert.Single(extracted!.Versions);
        var refusal = Assert.Single(version.FieldOutcomes);
        Assert.Equal(ExtractionFields.PayloadYear, refusal.Field);
        Assert.Equal(FieldOutcomeKind.EngineRejected, refusal.Kind);

        var accepted = await ExtractionPortalHttp.PostReviewMutationAsync(
            accountant,
            uploaded.CaseId,
            uploaded.T4DocumentId,
            $"/intake-cases/{uploaded.CaseId:D}/extractions/{uploaded.T4DocumentId:D}/accept",
            new Dictionary<string, string>());
        var finalized = await ExtractionPortalHttp.FinalizeAsync(
            accountant, uploaded.CaseId, uploaded.T4DocumentId);

        Assert.Equal(HttpStatusCode.NotFound, accepted.StatusCode);

        // Spec 017 T03 (FR-42, human-authorized 2026-10-01): finalizing an
        // in-scope case with no accepted T4 is refused with the fixed,
        // value-free finalization notice rather than the empty 404.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, finalized.StatusCode);
        Assert.Contains(
            "<p class=\"notice\" role=\"alert\">The case was not finalized. Review each document "
            + "and resolve anything still awaiting a decision, then try again.</p>",
            finalized.Body,
            StringComparison.Ordinal);

        var after = await FindAsync(world, uploaded, uploaded.T4DocumentId);
        Assert.Empty(after!.Acceptances);
        Assert.All(
            after.Versions,
            item => Assert.Equal(
                ExtractionAcceptanceState.AwaitingAccountantDecision,
                item.AcceptanceState));
        Assert.Empty(world.Reviews.All);

        // Neither year was coerced: the case keeps its stored year, and the
        // document's printed year was never rewritten to match it.
        var stored = world.Intake.Store.Single();
        Assert.Equal(2025, stored.TaxYear);
        Assert.Null(stored.ReviewAssociation);
    }

    // ----------------------------------------------------------------- AC-39

    [Fact] // AC-39
    public async Task AHeaderFirstSourceDeclaredAsPdfIsStillRejectedAtUpload()
    {
        await using var world = ExtractionPortalWorld.Create();
        var headerFirst = Encoding.UTF8.GetBytes(string.Join('\n', T4Lines(2025, NewRecipientIdentifier()).Skip(1)));

        var uploaded = await UploadAsync(world, headerFirst);

        Assert.Equal(HttpStatusCode.BadRequest, uploaded.T4Status);
        Assert.Empty(world.Intake.Store.Single().Documents);
        Assert.Equal(Guid.Empty, uploaded.T4DocumentId);
    }

    // ------------------------------------------------- AC-40 through the route

    [Fact] // AC-40: the gate is unchanged, so a non-bridge PDF still uploads — and yields nothing.
    public async Task ASignatureValidUploadWithoutTheHeaderExtractsNothing()
    {
        await using var world = ExtractionPortalWorld.Create();

        var uploaded = await UploadAsync(world, SyntheticSource.Pdf("1 0 obj\nKIND: T4\nBOX 14: 50000.00\n"));
        Assert.Equal(HttpStatusCode.OK, uploaded.T4Status);

        using var accountant = await SignedInAccountantAsync(world);
        await ExtractionPortalHttp.ExtractAsync(accountant, uploaded.CaseId, uploaded.T4DocumentId);

        var document = await FindAsync(world, uploaded, uploaded.T4DocumentId);
        Assert.True(document is null || document.Versions.Count == 0);
        Assert.True(document is null || document.MedicalEvidenceVersions.Count == 0);
        Assert.Empty(world.Reviews.All);
    }

    // ----------------------------------------------------------------- AC-44

    [Fact] // AC-44
    public void TheProductionAdapterIsByteIdenticalToTheBaseline()
    {
        var (exitCode, output) = Git(
            "diff", "--exit-code", "--no-ext-diff", "--stat", Baseline, "--", ProductionAdapterDirectory);

        Assert.True(exitCode == 0, $"The production adapter differs from {Baseline}.{Environment.NewLine}{output}");

        var (statusExitCode, untracked) = Git(
            "status", "--porcelain", "--untracked-files=all", "--", ProductionAdapterDirectory);
        Assert.Equal(0, statusExitCode);
        Assert.Empty(untracked.Trim());
    }

    [Fact] // AC-44, FR-24
    public void NoProductionAdapterFileNamesTheDeterministicHeaderOrTheBridge()
    {
        var files = Directory.GetFiles(
            Path.Combine(RepositoryRoot, "src", "DocumentIntelligence", "Production"),
            "*",
            SearchOption.AllDirectories);

        Assert.NotEmpty(files);
        Assert.All(files, file =>
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(Header, text, StringComparison.Ordinal);
            Assert.DoesNotContain("DocumentHeader", text, StringComparison.Ordinal);
            Assert.DoesNotContain("%PDF", text, StringComparison.OrdinalIgnoreCase);
        });
    }

    // ----------------------------------------------------------------- AC-46

    [Fact] // AC-46
    public void AProductionProfileNamingTheDeterministicProviderIsStillRefused()
    {
        var settings = ProductionStartupTests.ProductionProfile();
        settings["TaxAi:Extraction:Provider"] = "Deterministic";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        // The configured name is refused by the extraction production-shape
        // check the admission gate runs...
        var shapeRefusals = (IReadOnlyList<ProductionAdmissionRefusal>)typeof(Program).Assembly
            .GetType("TaxAI.Portal.ExtractionConfiguration", throwOnError: true)!
            .GetMethod("ProductionShapeRefusals", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { configuration })!;
        Assert.Contains(ProductionAdmissionRefusal.DevelopmentProviderConfigured, shapeRefusals);

        // ...and admission itself refuses a host that registered the provider.
        using var provider = new ServiceCollection()
            .AddSingleton<IDocumentIntelligenceProvider>(
                new DeterministicDocumentIntelligenceProvider(new DocumentIntelligenceOptions
                {
                    MaximumSourceBytes = 1024,
                    ProviderLabel = "deterministic-test-adapter",
                }))
            .BuildServiceProvider();
        var readiness = (ProductionReadiness)typeof(Program).Assembly
            .GetType("TaxAI.Portal.Production.ProductionAdmissionGate", throwOnError: true)!
            .GetMethod("Evaluate", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(
                null,
                new object[]
                {
                    provider,
                    configuration,
                    "Production",
                    new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
                })!;

        Assert.False(readiness.IsAdmitted);
        Assert.Contains(ProductionAdmissionRefusal.DevelopmentProviderConfigured, readiness.Refusals);
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>
    /// Creates a case and sends the sources through the real client route. A
    /// refused upload leaves its document identifier empty.
    /// </summary>
    private static async Task<UploadedCase> UploadAsync(
        ExtractionPortalWorld world,
        byte[] t4Source,
        byte[]? medicalSource = null)
    {
        using var accountant = world.Intake.FirmOne.CreateAccountantClient();
        await world.Intake.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.CreateCaseAsync(
            accountant,
            expectedT4Count: 1,
            expectedMedicalCount: medicalSource is null ? 0 : 1);

        using var client = world.Intake.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(client, issued.Credential);
        await IntakeFlow.SaveAnswersAsync(
            client,
            AvailabilityAnswerCode.AvailableNow,
            medicalSource is null ? null : AvailabilityAnswerCode.AvailableNow);

        HttpStatusCode t4Status;
        using (var response = await IntakeFlow.UploadAsync(
                   client, DocumentCategory.T4Slip, 1, IntakeMediaKind.Pdf, t4Source))
        {
            t4Status = response.StatusCode;
        }

        HttpStatusCode? medicalStatus = null;
        if (medicalSource is not null)
        {
            using var response = await IntakeFlow.UploadAsync(
                client,
                DocumentCategory.MedicalExpenseEvidence,
                1,
                IntakeMediaKind.Pdf,
                medicalSource);
            medicalStatus = response.StatusCode;
        }

        if (t4Status == HttpStatusCode.OK)
        {
            using var submitted = await IntakeFlow.SubmitAsync(client);
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        }

        var stored = world.Intake.Store.Single();
        Assert.Equal(issued.CaseId, stored.CaseId);

        return new UploadedCase(
            stored.Firm,
            stored.CaseId,
            t4Status,
            DocumentOf(stored, DocumentCategory.T4Slip),
            medicalStatus,
            DocumentOf(stored, DocumentCategory.MedicalExpenseEvidence));
    }

    private static Guid DocumentOf(StoredIntakeCase stored, DocumentCategory category) =>
        stored.Documents
            .Where(document => document.Category == category)
            .Select(document => document.DocumentId)
            .SingleOrDefault();

    private static async Task<HttpClient> SignedInAccountantAsync(ExtractionPortalWorld world)
    {
        var accountant = world.Intake.FirmOne.CreateAnonymousClient();
        await world.Intake.FirmOne.SignInAsync(accountant);
        return accountant;
    }

    private static Task<StoredDocumentExtraction?> FindAsync(
        ExtractionPortalWorld world,
        UploadedCase uploaded,
        Guid documentId) =>
        world.Extractions.FindDocumentAsync(
            uploaded.Firm, uploaded.CaseId, documentId, CancellationToken.None);

    /// <summary>The shape the local sample generator writes.</summary>
    private static byte[] T4Source(int printedYear, string recipientIdentifier) =>
        Encoding.UTF8.GetBytes(string.Join('\n', T4Lines(printedYear, recipientIdentifier)) + "\n");

    private static string[] T4Lines(int printedYear, string recipientIdentifier) =>
    [
        Signature,
        Header,
        "KIND: T4",
        FormattableString.Invariant($"YEAR: {printedYear}"),
        "EMPLOYER-NAME: Synthetic Employer Ltd.",
        "EMPLOYEE-NAME: Synthetic Employee",
        $"BOX 12: {recipientIdentifier}",
        "BOX 14: 50000.00",
        "BOX 45: 1",
    ];

    private static byte[] MedicalSource() => Encoding.UTF8.GetBytes(string.Join(
        '\n',
        Signature,
        Header,
        "KIND: MEDICAL-EXPENSE-EVIDENCE",
        "SERVICE-DATE: 2025-03-14",
        "GROSS-AMOUNT: 120.50",
        "CURRENCY: CAD",
        "PROVIDER-NAME: Synthetic Dental Clinic",
        "PATIENT-NAME: Synthetic Employee",
        "RECEIPT-REFERENCE: R-SYNTHETIC-1") + "\n");

    /// <summary>Assembled at run time; never a literal in a tracked file.</summary>
    private static string NewRecipientIdentifier() =>
        string.Concat(Enumerable.Range(0, 9).Select(_ => RandomNumberGenerator.GetInt32(0, 10)));

    private static (int ExitCode, string Output) Git(params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new InvalidOperationException("The repository root was not found.");
    }

    private sealed record UploadedCase(
        TrustedFirmId Firm,
        Guid CaseId,
        HttpStatusCode T4Status,
        Guid T4DocumentId,
        HttpStatusCode? MedicalStatus,
        Guid MedicalDocumentId);
}
