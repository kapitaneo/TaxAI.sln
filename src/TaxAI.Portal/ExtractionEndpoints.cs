using System.Globalization;
using ClientIntake.Contracts;
using ClientIntake.Models;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using TaxAI.Portal.Components.Pages;
using TaxCases.Models;
using Tenancy.Models;

namespace TaxAI.Portal;

/// <summary>
/// The authenticated accountant extraction-review surface. Route identifiers
/// are selectors only: every handler obtains the firm and actor from the
/// revalidated server-side authorization state and verifies selectors against
/// firm-scoped stored state.
/// </summary>
internal static class ExtractionEndpoints
{
    internal const string ListPath =
        "/intake-cases/{caseId:guid}/extractions";
    internal const string ReviewPath =
        "/intake-cases/{caseId:guid}/extractions/{documentId:guid}";
    internal const string ExtractPath =
        "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/extract";
    internal const string ClassificationPath =
        "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/classification";
    internal const string CorrectionPath =
        "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/corrections";
    internal const string AcceptPath =
        "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/accept";
    internal const string DuplicatePath =
        "/intake-cases/{caseId:guid}/extractions/duplicates/{candidateId:guid}";
    internal const string FinalizePath =
        "/intake-cases/{caseId:guid}/extractions/finalize";

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(ListPath, ListAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy);
        endpoints.MapGet(ReviewPath, ReviewAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy);
        endpoints.MapPost(ExtractPath, ExtractAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
        endpoints.MapPost(ClassificationPath, OverrideClassificationAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
        endpoints.MapPost(CorrectionPath, CorrectAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
        endpoints.MapPost(AcceptPath, AcceptAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
        endpoints.MapPost(DuplicatePath, ResolveDuplicateAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
        endpoints.MapPost(FinalizePath, FinalizeAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
    }

    private static async Task<IResult> ListAsync(
        Guid caseId,
        HttpContext context,
        IIntakeStore intake,
        IExtractionStore extractions)
    {
        if (!TryScope(context, out var firm, out _))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var loaded = await LoadCaseAsync(
            firm!,
            caseId,
            intake,
            extractions,
            context.RequestAborted);
        if (loaded is null)
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var items = new List<ExtractionListItemModel>();
        foreach (var document in loaded.Intake.Documents)
        {
            if (!TryExpectedItem(
                    loaded.Intake,
                    document.ExpectedItemId,
                    out var expected))
            {
                return IntakeHttpSecurity.NotFound(context);
            }

            var stored = DocumentOf(loaded.Extractions, document.DocumentId);
            var state = ProjectState(stored);
            items.Add(new ExtractionListItemModel(
                document.DocumentId,
                expected!.Category,
                expected.Ordinal,
                state.AutomatedClassification,
                state.EffectiveClassification,
                state.AcceptanceState,
                stored?.Versions.Count ?? 0,
                stored?.MedicalEvidenceVersions.Count ?? 0,
                stored is not null &&
                    ExtractionProjection.HasUnresolvedDuplicateCandidate(stored)));
        }

        return new RazorComponentResult<ExtractionListPage>(
            new
            {
                Model = new ExtractionListPageModel(
                    caseId,
                    loaded.Intake.TaxYear,
                    Array.AsReadOnly(items.ToArray()),
                    loaded.Intake.ReviewAssociation is not null ||
                        (loaded.Extractions?.CaseAcceptances.Count ?? 0) > 0),
            });
    }

    private static async Task<IResult> ReviewAsync(
        Guid caseId,
        Guid documentId,
        HttpContext context,
        IIntakeStore intake,
        IExtractionStore extractions)
    {
        if (!TryScope(context, out var firm, out _))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var loaded = await LoadCaseAsync(
            firm!,
            caseId,
            intake,
            extractions,
            context.RequestAborted);
        if (loaded is null ||
            !TryIntakeDocument(loaded.Intake, documentId, out var intakeDocument) ||
            !TryExpectedItem(
                loaded.Intake,
                intakeDocument!.ExpectedItemId,
                out var expected))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var stored = DocumentOf(loaded.Extractions, documentId);
        return new RazorComponentResult<ExtractionReviewPage>(
            new
            {
                CaseId = caseId,
                DocumentId = documentId,
                Model = ProjectReview(expected!, stored),
            });
    }

    private static async Task<IResult> ExtractAsync(
        Guid caseId,
        Guid documentId,
        HttpContext context,
        IExtractionIngestWorkflow workflow,
        TimeProvider clock)
    {
        if (!TryScope(context, out var firm, out var actor))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.ExtractAsync(
            firm!,
            actor,
            new ExtractDocumentCommand
            {
                CaseId = caseId,
                DocumentId = documentId,
            },
            clock.GetUtcNow(),
            context.RequestAborted);
        return MutationResult(context, caseId, documentId, result);
    }

    private static async Task<IResult> OverrideClassificationAsync(
        Guid caseId,
        Guid documentId,
        HttpContext context,
        IExtractionReviewWorkflow workflow,
        TimeProvider clock,
        [FromForm(Name = "targetClassification")] string? target)
    {
        if (!TryScope(context, out var firm, out var actor) ||
            !TryEnum(target, out DocumentClassification classification) ||
            classification == DocumentClassification.Unsupported)
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.OverrideClassificationAsync(
            firm!,
            actor,
            new OverrideExtractionClassificationCommand
            {
                CaseId = caseId,
                DocumentId = documentId,
                TargetClassification = classification,
            },
            clock.GetUtcNow(),
            context.RequestAborted);
        return MutationResult(context, caseId, documentId, result);
    }

    private static async Task<IResult> CorrectAsync(
        Guid caseId,
        Guid documentId,
        HttpContext context,
        IExtractionReviewWorkflow workflow,
        TimeProvider clock,
        [FromForm(Name = "field")] string? fieldName,
        [FromForm(Name = "boxCode")] string? boxCode,
        [FromForm(Name = "correctedValue")] string? correctedValue)
    {
        if (!TryScope(context, out var firm, out var actor) ||
            !TryEnum(fieldName, out CorrectionField field))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.CorrectAsync(
            firm!,
            actor,
            new ApplyExtractionCorrectionCommand
            {
                CaseId = caseId,
                DocumentId = documentId,
                Field = field,
                BoxCode = string.IsNullOrEmpty(boxCode) ? null : boxCode,
                CorrectedValue = string.IsNullOrEmpty(correctedValue)
                    ? null
                    : correctedValue,
            },
            clock.GetUtcNow(),
            context.RequestAborted);
        return MutationResult(context, caseId, documentId, result);
    }

    private static async Task<IResult> AcceptAsync(
        Guid caseId,
        Guid documentId,
        HttpContext context,
        IExtractionReviewWorkflow workflow,
        TimeProvider clock)
    {
        if (!TryScope(context, out var firm, out var actor))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.AcceptDocumentAsync(
            firm!,
            actor,
            new AcceptExtractionDocumentCommand
            {
                CaseId = caseId,
                DocumentId = documentId,
            },
            clock.GetUtcNow(),
            context.RequestAborted);
        return MutationResult(context, caseId, documentId, result);
    }

    private static async Task<IResult> ResolveDuplicateAsync(
        Guid caseId,
        Guid candidateId,
        HttpContext context,
        IExtractionReviewWorkflow workflow,
        TimeProvider clock,
        [FromForm(Name = "resolution")] string? resolutionName)
    {
        if (!TryScope(context, out var firm, out var actor) ||
            !TryEnum(resolutionName, out DuplicateResolutionKind resolution))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.ResolveDuplicateAsync(
            firm!,
            actor,
            new ResolveExtractionDuplicateCommand
            {
                CaseId = caseId,
                CandidateId = candidateId,
                Resolution = resolution,
            },
            clock.GetUtcNow(),
            context.RequestAborted);
        if (!result.IsSuccess || result.Value is null)
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        return Results.Redirect(
            ReviewUrl(caseId, result.Value.DocumentId),
            permanent: false,
            preserveMethod: false);
    }

    private static async Task<IResult> FinalizeAsync(
        Guid caseId,
        HttpContext context,
        IExtractionReviewWorkflow workflow,
        TimeProvider clock)
    {
        if (!TryScope(context, out var firm, out var actor))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.FinalizeCaseAsync(
            firm!,
            actor,
            new FinalizeExtractionCaseCommand { CaseId = caseId },
            clock.GetUtcNow(),
            context.RequestAborted);
        if (!result.IsSuccess || result.Value is null)
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        return Results.Redirect(
            $"/reviews/{result.Value.ReviewRecordId:D}",
            permanent: false,
            preserveMethod: false);
    }

    private static IResult MutationResult<TValue>(
        HttpContext context,
        Guid caseId,
        Guid documentId,
        ExtractionResult<TValue> result)
        where TValue : class =>
        !result.IsSuccess || result.Value is null
            ? IntakeHttpSecurity.NotFound(context)
            : Results.Redirect(
                ReviewUrl(caseId, documentId),
                permanent: false,
                preserveMethod: false);

    private static async Task<LoadedExtractionCase?> LoadCaseAsync(
        TrustedFirmId firm,
        Guid caseId,
        IIntakeStore intake,
        IExtractionStore extractions,
        CancellationToken cancellationToken)
    {
        if (caseId == Guid.Empty)
        {
            return null;
        }

        var intakeCase = await intake.FindAsync(
            firm,
            caseId,
            cancellationToken);
        if (intakeCase is null ||
            intakeCase.CaseId != caseId ||
            !string.Equals(
                intakeCase.Firm.ToCanonicalString(),
                firm.ToCanonicalString(),
                StringComparison.Ordinal))
        {
            return null;
        }

        var storedExtractions = await extractions.FindCaseAsync(
            firm,
            caseId,
            cancellationToken);
        if (storedExtractions is not null &&
            (storedExtractions.CaseId != caseId ||
             !ExtractionDocumentsAreConsistent(intakeCase, storedExtractions)))
        {
            return null;
        }

        return new LoadedExtractionCase(intakeCase, storedExtractions);
    }

    private static bool ExtractionDocumentsAreConsistent(
        StoredIntakeCase intakeCase,
        StoredCaseExtractions extractions)
    {
        var seen = new HashSet<Guid>();
        foreach (var extraction in extractions.Documents)
        {
            if (!seen.Add(extraction.DocumentId) ||
                extraction.ParticipantId != intakeCase.PrimaryParticipantId ||
                !TryIntakeDocument(
                    intakeCase,
                    extraction.DocumentId,
                    out _))
            {
                return false;
            }
        }

        return true;
    }

    private static ExtractionReviewPageModel ProjectReview(
        IntakeExpectedItemRecord expected,
        StoredDocumentExtraction? stored)
    {
        var state = ProjectState(stored);
        var latestT4 = state.EffectiveClassification == DocumentClassification.T4Slip
            ? ExtractionProjection.LatestExtractionVersion(stored)
            : null;
        var latestMedical =
            state.EffectiveClassification == DocumentClassification.MedicalExpenseEvidence
                ? ExtractionProjection.LatestMedicalEvidenceVersion(stored)
                : null;
        object? latestVersion = latestT4 is null ? latestMedical : latestT4;
        var provenance = ProjectProvenance(latestVersion);
        var outcomes = latestT4?.FieldOutcomes ??
            latestMedical?.FieldOutcomes ??
            Array.Empty<FieldValidationOutcome>();

        return new ExtractionReviewPageModel(
            expected.Category,
            expected.Ordinal,
            state.AutomatedClassification,
            state.EffectiveClassification,
            state.IsClassificationOverridden,
            state.AcceptanceState,
            latestT4?.Version ?? latestMedical?.Version,
            stored?.Corrections.Count ?? 0,
            provenance.ProviderLabel,
            provenance.Confidence,
            ProjectFieldOutcomes(outcomes),
            latestT4 is null
                ? null
                : new T4ExtractionPanelModel(
                    latestT4.EmployerDisplayName,
                    latestT4.EmployeeDisplayName,
                    latestT4.EmployerAddress,
                    latestT4.EmployeeAddress,
                    Array.AsReadOnly(
                        latestT4.BoxValues
                            .Select(box => new T4BoxDisplayModel(
                                box.Code,
                                box.Value))
                            .ToArray())),
            latestMedical is null
                ? null
                : new MedicalEvidencePanelModel(
                    latestMedical.ServiceDateAsPrinted,
                    latestMedical.GrossAmountAsPrinted,
                    latestMedical.Currency.CodeAsPrinted,
                    latestMedical.ProviderDisplayName,
                    latestMedical.PatientDisplayName,
                    latestMedical.ReceiptReference),
            ProjectDuplicateCandidates(stored));
    }

    private static ProjectedProvenance ProjectProvenance(object? version)
    {
        var provenance = version?
            .GetType()
            .GetProperty("Provenance")?
            .GetValue(version);
        if (provenance is null)
        {
            return ProjectedProvenance.Empty;
        }

        var providerLabel = provenance
            .GetType()
            .GetProperty("ProviderLabel")?
            .GetValue(provenance) as string;
        var confidenceValue = provenance
            .GetType()
            .GetProperty("Confidence")?
            .GetValue(provenance);
        var confidence = confidenceValue is IFormattable formattable
            ? formattable.ToString(
                format: null,
                CultureInfo.InvariantCulture)
            : null;
        return new ProjectedProvenance(providerLabel, confidence);
    }

    private static IReadOnlyList<ExtractionFieldRowModel> ProjectFieldOutcomes(
        IReadOnlyList<FieldValidationOutcome> outcomes)
    {
        var projected = new List<ExtractionFieldRowModel>();
        foreach (var outcome in outcomes)
        {
            if (BrowserFieldLabel(outcome.Field) is { } label)
            {
                projected.Add(new ExtractionFieldRowModel(label, outcome.Kind));
            }
        }

        return Array.AsReadOnly(projected.ToArray());
    }

    private static string? BrowserFieldLabel(string field) =>
        field switch
        {
            "Year" or "TaxYear" => "Tax year",
            ExtractionWorkflowFields.Provider => "Provider result",
            ExtractionWorkflowFields.PayloadBoxes => "T4 box collection",
            ExtractionWorkflowFields.PayloadBox => "T4 box",
            ExtractionWorkflowFields.PayloadBoxCode => "T4 box code",
            ExtractionWorkflowFields.BoxCode => "T4 box code",
            ExtractionWorkflowFields.MedicalServiceDate => "Service date",
            ExtractionWorkflowFields.MedicalGrossAmount => "Gross amount",
            ExtractionWorkflowFields.MedicalCurrency => "Currency",
            ExtractionWorkflowFields.MedicalProviderDisplayName =>
                "Provider display name",
            ExtractionWorkflowFields.MedicalPatientDisplayName =>
                "Patient display name",
            ExtractionWorkflowFields.MedicalReceiptReference =>
                "Receipt reference",
            _ when field.StartsWith("Box ", StringComparison.Ordinal) => null,
            _ => null,
        };

    private static IReadOnlyList<DuplicateCandidateModel> ProjectDuplicateCandidates(
        StoredDocumentExtraction? stored)
    {
        if (stored is null)
        {
            return Array.Empty<DuplicateCandidateModel>();
        }

        var resolved = stored.DuplicateResolutions
            .Select(item => item.CandidateId)
            .ToHashSet();
        return Array.AsReadOnly(
            stored.DuplicateCandidates
                .Where(item => !resolved.Contains(item.CandidateId))
                .Select(item => new DuplicateCandidateModel(item.CandidateId))
                .ToArray());
    }

    private static ProjectedDocumentState ProjectState(
        StoredDocumentExtraction? stored)
    {
        if (stored is null)
        {
            return ProjectedDocumentState.Empty;
        }

        var latestT4 = ExtractionProjection.LatestExtractionVersion(stored);
        var latestMedical = ExtractionProjection.LatestMedicalEvidenceVersion(stored);
        DocumentClassification automated;
        if (latestT4 is null)
        {
            automated = latestMedical is null
                ? DocumentClassification.Unsupported
                : DocumentClassification.MedicalExpenseEvidence;
        }
        else if (latestMedical is null ||
                 latestT4.RecordedAtUtc >= latestMedical.RecordedAtUtc)
        {
            automated = latestT4.Classification.AutomatedClassification;
        }
        else
        {
            automated = DocumentClassification.MedicalExpenseEvidence;
        }

        var latestOverride = stored.ClassificationOverrides
            .OrderByDescending(item => item.OverriddenAtUtc)
            .FirstOrDefault();
        return new ProjectedDocumentState(
            automated,
            latestOverride?.OverriddenClassification ?? automated,
            latestOverride is not null,
            ExtractionProjection.AcceptanceState(stored));
    }

    private static StoredDocumentExtraction? DocumentOf(
        StoredCaseExtractions? extractions,
        Guid documentId)
    {
        StoredDocumentExtraction? matched = null;
        foreach (var document in extractions?.Documents ??
                 Array.Empty<StoredDocumentExtraction>())
        {
            if (document.DocumentId != documentId)
            {
                continue;
            }

            if (matched is not null)
            {
                return null;
            }

            matched = document;
        }

        return matched;
    }

    private static bool TryIntakeDocument(
        StoredIntakeCase intakeCase,
        Guid documentId,
        out IntakeDocumentRecord? matched)
    {
        matched = null;
        foreach (var document in intakeCase.Documents)
        {
            if (document.DocumentId != documentId)
            {
                continue;
            }

            if (matched is not null)
            {
                matched = null;
                return false;
            }

            matched = document;
        }

        return matched is not null;
    }

    private static bool TryExpectedItem(
        StoredIntakeCase intakeCase,
        Guid expectedItemId,
        out IntakeExpectedItemRecord? matched)
    {
        matched = null;
        foreach (var expected in intakeCase.ExpectedItems)
        {
            if (expected.ExpectedItemId != expectedItemId)
            {
                continue;
            }

            if (matched is not null)
            {
                matched = null;
                return false;
            }

            matched = expected;
        }

        return matched is not null;
    }

    private static bool TryScope(
        HttpContext context,
        out TrustedFirmId? firm,
        out Guid actor)
    {
        firm = null;
        actor = Guid.Empty;
        var authorization = PortalAuthorization.BootstrapStateFor(context);
        if (authorization?.TrustedFirmId is not Guid firmId ||
            firmId == Guid.Empty ||
            authorization.User.UserId == Guid.Empty)
        {
            return false;
        }

        firm = TrustedFirmId.FromGuid(firmId);
        actor = authorization.User.UserId;
        return true;
    }

    private static bool TryEnum<TEnum>(
        string? candidate,
        out TEnum value)
        where TEnum : struct, Enum =>
        Enum.TryParse(candidate, ignoreCase: false, out value) &&
        Enum.IsDefined(value);

    private static string ReviewUrl(Guid caseId, Guid documentId) =>
        $"/intake-cases/{caseId:D}/extractions/{documentId:D}";

    private sealed record LoadedExtractionCase(
        StoredIntakeCase Intake,
        StoredCaseExtractions? Extractions);

    private sealed record ProjectedDocumentState(
        DocumentClassification AutomatedClassification,
        DocumentClassification EffectiveClassification,
        bool IsClassificationOverridden,
        ExtractionAcceptanceState AcceptanceState)
    {
        internal static ProjectedDocumentState Empty { get; } = new(
            DocumentClassification.Unsupported,
            DocumentClassification.Unsupported,
            IsClassificationOverridden: false,
            ExtractionAcceptanceState.AwaitingAccountantDecision);
    }

    private sealed record ProjectedProvenance(
        string? ProviderLabel,
        string? Confidence)
    {
        internal static ProjectedProvenance Empty { get; } = new(null, null);
    }
}
