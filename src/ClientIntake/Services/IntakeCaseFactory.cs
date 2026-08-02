using ClientIntake.Contracts;
using ClientIntake.Models;
using TaxCases.Models;
using Tenancy.Models;

namespace ClientIntake.Services;

internal static class IntakeCaseFactory
{
    private const int MaximumExpectedCount = 25;

    internal static NewIntakeCase? TryCreate(
        TrustedFirmId firm,
        Guid actorReference,
        CreateIntakeCaseCommand command,
        DateTimeOffset nowUtc,
        IIntakeCredentialSource credentialSource,
        TimeSpan linkLifetime,
        out IntakeSecret? linkCredential)
    {
        linkCredential = null;

        if (firm is null ||
            actorReference == Guid.Empty ||
            command is null ||
            credentialSource is null ||
            !IntakeWorkflowOptionsGuard.IsUtc(nowUtc) ||
            !IntakeDisplayNameGuard.IsValid(command.PrimaryTaxpayerDisplayName) ||
            !ValidCount(command.ExpectedT4Count) ||
            !ValidCount(command.ExpectedMedicalExpenseCount) ||
            command.ExpectedT4Count!.Value + command.ExpectedMedicalExpenseCount!.Value <= 0 ||
            !IntakeWorkflowOptionsGuard.TryAdd(nowUtc, linkLifetime, out var linkExpiry))
        {
            return null;
        }

        var caseId = credentialSource.CreateIdentifier();
        var participantId = credentialSource.CreateIdentifier();
        var linkId = credentialSource.CreateIdentifier();
        var credential = credentialSource.CreateCredential();

        if (caseId == Guid.Empty ||
            participantId == Guid.Empty ||
            linkId == Guid.Empty ||
            credential is null ||
            caseId == participantId ||
            caseId == linkId ||
            participantId == linkId)
        {
            return null;
        }

        var expectedItems = BuildExpectedItems(
            command.ExpectedT4Count.Value,
            command.ExpectedMedicalExpenseCount.Value,
            credentialSource);
        if (expectedItems is null)
        {
            return null;
        }

        var domainCase = TaxCase.Create(
            new CreateTaxCaseRequest(
                caseId.ToString("D"),
                firm.ToCanonicalString(),
                command.TaxYear,
                new ParticipantDescriptor(
                    participantId.ToString("D"),
                    RelationshipKind.PrimaryTaxpayer,
                    command.PrimaryTaxpayerDisplayName,
                    null,
                    firm.ToCanonicalString()),
                CorrelationReference: null));
        if (!domainCase.IsSuccess)
        {
            return null;
        }

        var requirements = expectedItems
            .GroupBy(item => item.Category)
            .OrderBy(group => (int)group.Key)
            .Select(group => new EvidenceRequirementDescriptor(
                $"requirement-{IntakeQuestionnairePolicy.QuestionId(group.Key)}",
                participantId.ToString("D"),
                group.Key.ToString(),
                group.Count()))
            .ToArray();
        var checklist = domainCase.Value!.Case.ConfirmChecklist(
            new ChecklistDescriptor(
                requirements,
                IntakeQuestionnairePolicy.DomainQuestions(expectedItems)),
            correlationReference: null);
        if (!checklist.IsSuccess)
        {
            return null;
        }

        var transitioned = checklist.Value!.Case.TransitionTo(
            CaseStatus.InformationGathering,
            correlationReference: null);
        if (!transitioned.IsSuccess)
        {
            return null;
        }

        var digest = credentialSource.Digest(
            credential,
            IntakeTokenPurpose.BootstrapLink);
        var link = new IntakeLinkRecord(
            linkId,
            digest,
            nowUtc,
            linkExpiry,
            SpentAtUtc: null,
            RevokedAtUtc: null);
        var storedCase = new StoredIntakeCase(
            firm,
            caseId,
            participantId,
            command.PrimaryTaxpayerDisplayName!,
            transitioned.Value!.Case.TaxYear,
            transitioned.Value.Case.Status,
            ConcurrencyVersion: 0,
            expectedItems,
            link,
            AnswerVersions: [],
            Documents: [],
            UploadAttempts: [],
            Submissions: [],
            ReviewAssociation: null);
        var events = new[]
        {
            Audit(credentialSource, 1, IntakeAuditKind.CaseCreated, actorReference, nowUtc),
            Audit(credentialSource, 2, IntakeAuditKind.ChecklistConfirmed, actorReference, nowUtc),
            Audit(credentialSource, 3, IntakeAuditKind.LinkIssued, actorReference, nowUtc),
        };

        linkCredential = credential;
        return new NewIntakeCase(storedCase, events);
    }

    internal static IntakeAuditEventRecord Audit(
        IIntakeCredentialSource source,
        long sequence,
        IntakeAuditKind kind,
        Guid actorReference,
        DateTimeOffset atUtc,
        Guid? expectedItemId = null) =>
        new(
            source.CreateIdentifier(),
            sequence,
            kind,
            IntakeActorKind.Accountant,
            actorReference,
            expectedItemId,
            atUtc);

    private static bool ValidCount(int? count) =>
        count is >= 0 and <= MaximumExpectedCount;

    private static IReadOnlyList<IntakeExpectedItemRecord>? BuildExpectedItems(
        int t4Count,
        int medicalCount,
        IIntakeCredentialSource source)
    {
        var items = new List<IntakeExpectedItemRecord>(t4Count + medicalCount);
        Add(DocumentCategory.T4Slip, t4Count);
        Add(DocumentCategory.MedicalExpenseEvidence, medicalCount);

        return items.Any(item => item.ExpectedItemId == Guid.Empty) ||
               items.Select(item => item.ExpectedItemId).Distinct().Count() != items.Count
            ? null
            : Array.AsReadOnly(items.ToArray());

        void Add(DocumentCategory category, int count)
        {
            for (var ordinal = 1; ordinal <= count; ordinal++)
            {
                items.Add(new IntakeExpectedItemRecord(
                    source.CreateIdentifier(),
                    category,
                    ordinal));
            }
        }
    }
}
