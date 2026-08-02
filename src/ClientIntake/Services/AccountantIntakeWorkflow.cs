using ClientIntake.Contracts;
using ClientIntake.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace ClientIntake.Services;

/// <summary>
/// The accountant side of the intake workflow.
/// <para>
/// T02 implements the frozen T01 contract behind in-process services only. No
/// intake route is mapped until the later UI wave.
/// </para>
/// <para>
/// The dependency set is the boundary. It takes the intake store, the secret
/// source, the <i>existing</i> firm-scoped review contract, and the required
/// options — and nothing else. In particular it takes no clock, no environment,
/// no current user, no HTTP context, and no content store: the accountant side
/// never touches uploaded bytes.
/// </para>
/// </summary>
public sealed class AccountantIntakeWorkflow : IAccountantIntakeWorkflow
{
    private readonly IIntakeStore _store;
    private readonly IIntakeCredentialSource _credentialSource;
    private readonly IReviewRecordRepository _reviewRecords;
    private readonly IntakeWorkflowOptions _options;

    public AccountantIntakeWorkflow(
        IIntakeStore store,
        IIntakeCredentialSource credentialSource,
        IReviewRecordRepository reviewRecords,
        IntakeWorkflowOptions options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(credentialSource);
        ArgumentNullException.ThrowIfNull(reviewRecords);

        _store = store;
        _credentialSource = credentialSource;
        _reviewRecords = reviewRecords;
        _options = IntakeWorkflowOptionsGuard.RequireValid(options);
    }

    public async Task<IntakeResult<CreatedIntakeCase>> CreateAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        CreateIntakeCaseCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var intakeCase = IntakeCaseFactory.TryCreate(
                firm,
                accountantActorReference,
                command,
                nowUtc,
                _credentialSource,
                _options.LinkLifetime,
                out var credential);
            if (intakeCase is null || credential is null)
            {
                return IntakeResult<CreatedIntakeCase>.Failure(IntakeResultCode.Invalid);
            }

            var write = await _store.CreateAsync(intakeCase, cancellationToken);
            return write == IntakeStoreWriteCode.Success
                ? IntakeResult<CreatedIntakeCase>.Success(
                    new CreatedIntakeCase(
                        intakeCase.Case.CaseId,
                        credential,
                        intakeCase.Case.CurrentLink.ExpiresAtUtc))
                : IntakeResult<CreatedIntakeCase>.Failure(Map(write));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return IntakeResult<CreatedIntakeCase>.Failure(IntakeResultCode.Invalid);
        }
    }

    public async Task<IntakeResult<AccountantIntakeView>> GetProgressAsync(
        TrustedFirmId firm,
        Guid caseId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!ValidScope(firm, caseId, nowUtc))
        {
            return IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound);
        }

        try
        {
            var storedCase = await _store.FindAsync(firm, caseId, cancellationToken);
            return storedCase is null
                ? IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound)
                : await ProjectProgressAsync(storedCase, nowUtc, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound);
        }
    }

    public async Task<IntakeResult<IssuedIntakeLink>> ReissueLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid accountantActorReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!ValidMutation(firm, caseId, accountantActorReference, nowUtc) ||
            !IntakeWorkflowOptionsGuard.TryAdd(
                nowUtc,
                _options.LinkLifetime,
                out var expiresAtUtc))
        {
            return IntakeResult<IssuedIntakeLink>.Failure(IntakeResultCode.Invalid);
        }

        try
        {
            var storedCase = await _store.FindAsync(firm, caseId, cancellationToken);
            if (storedCase is null)
            {
                return IntakeResult<IssuedIntakeLink>.Failure(IntakeResultCode.NotFound);
            }

            var credential = _credentialSource.CreateCredential();
            var replacement = new IntakeLinkRecord(
                _credentialSource.CreateIdentifier(),
                _credentialSource.Digest(
                    credential,
                    IntakeTokenPurpose.BootstrapLink),
                nowUtc,
                expiresAtUtc,
                SpentAtUtc: null,
                RevokedAtUtc: null);
            if (replacement.LinkId == Guid.Empty)
            {
                return IntakeResult<IssuedIntakeLink>.Failure(IntakeResultCode.Invalid);
            }

            var events = new[]
            {
                IntakeCaseFactory.Audit(
                    _credentialSource,
                    sequence: 0,
                    IntakeAuditKind.LinkReissued,
                    accountantActorReference,
                    nowUtc),
            };
            var write = await _store.ReissueLinkAsync(
                firm,
                caseId,
                storedCase.ConcurrencyVersion,
                replacement,
                events,
                cancellationToken);

            return write == IntakeStoreWriteCode.Success
                ? IntakeResult<IssuedIntakeLink>.Success(
                    new IssuedIntakeLink(credential, expiresAtUtc))
                : IntakeResult<IssuedIntakeLink>.Failure(Map(write));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return IntakeResult<IssuedIntakeLink>.Failure(IntakeResultCode.Invalid);
        }
    }

    public async Task<IntakeResult<AccountantIntakeView>> RevokeLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid accountantActorReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!ValidMutation(firm, caseId, accountantActorReference, nowUtc))
        {
            return IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.Invalid);
        }

        try
        {
            var storedCase = await _store.FindAsync(firm, caseId, cancellationToken);
            if (storedCase is null)
            {
                return IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound);
            }

            var write = await _store.RevokeLinkAsync(
                firm,
                caseId,
                storedCase.ConcurrencyVersion,
                nowUtc,
                IntakeCaseFactory.Audit(
                    _credentialSource,
                    sequence: 0,
                    IntakeAuditKind.LinkRevoked,
                    accountantActorReference,
                    nowUtc),
                cancellationToken);
            if (write != IntakeStoreWriteCode.Success)
            {
                return IntakeResult<AccountantIntakeView>.Failure(Map(write));
            }

            var updated = await _store.FindAsync(firm, caseId, cancellationToken);
            return updated is null
                ? IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound)
                : await ProjectProgressAsync(updated, nowUtc, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound);
        }
    }

    public async Task<IntakeResult<AccountantIntakeView>> AssociateReviewAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid recordId,
        Guid accountantActorReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!ValidMutation(firm, caseId, accountantActorReference, nowUtc) ||
            recordId == Guid.Empty)
        {
            return IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.Invalid);
        }

        try
        {
            var storedCase = await _store.FindAsync(firm, caseId, cancellationToken);
            if (storedCase is null)
            {
                return IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound);
            }

            if (storedCase.ReviewAssociation is not null)
            {
                return IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.Conflict);
            }

            var review = await _reviewRecords.FindAsync(
                firm,
                recordId,
                cancellationToken);
            if (!Matches(storedCase, review))
            {
                return IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound);
            }

            var write = await _store.AssociateReviewAsync(
                firm,
                caseId,
                storedCase.ConcurrencyVersion,
                new IntakeReviewAssociationRecord(recordId, nowUtc),
                IntakeCaseFactory.Audit(
                    _credentialSource,
                    sequence: 0,
                    IntakeAuditKind.ReviewAssociated,
                    accountantActorReference,
                    nowUtc),
                cancellationToken);
            if (write != IntakeStoreWriteCode.Success)
            {
                return IntakeResult<AccountantIntakeView>.Failure(Map(write));
            }

            var updated = await _store.FindAsync(firm, caseId, cancellationToken);
            return updated is null
                ? IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound)
                : await ProjectProgressAsync(updated, nowUtc, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound);
        }
    }

    private async Task<IntakeResult<AccountantIntakeView>> ProjectProgressAsync(
        StoredIntakeCase storedCase,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var replayed = IntakeStateReplayer.ReplayLatestSubmission(storedCase);
        if (replayed is null)
        {
            return IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound);
        }

        var handoffState = ReviewHandoffState.NotReady;
        Guid? reviewRecordId = null;
        if (replayed.Readiness.State == TaxCases.Models.ReadinessState.Ready)
        {
            if (storedCase.ReviewAssociation is null)
            {
                handoffState = ReviewHandoffState.AwaitingReviewRecord;
            }
            else
            {
                var review = await _reviewRecords.FindAsync(
                    storedCase.Firm,
                    storedCase.ReviewAssociation.RecordId,
                    cancellationToken);
                if (Matches(storedCase, review))
                {
                    handoffState = ReviewHandoffState.Available;
                    reviewRecordId = review!.RecordId;
                }
                else
                {
                    handoffState = ReviewHandoffState.Unavailable;
                }
            }
        }

        var view = IntakeProjection.Accountant(
            storedCase,
            nowUtc,
            handoffState,
            reviewRecordId);
        return view is null
            ? IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound)
            : IntakeResult<AccountantIntakeView>.Success(view);
    }

    private static bool Matches(
        StoredIntakeCase storedCase,
        Tenancy.Models.PersistedReviewRecord? review) =>
        review is not null &&
        string.Equals(
            review.CaseId,
            storedCase.CaseId.ToString("D"),
            StringComparison.Ordinal) &&
        review.TaxYear == storedCase.TaxYear;

    private static bool ValidScope(
        TrustedFirmId? firm,
        Guid caseId,
        DateTimeOffset nowUtc) =>
        firm is not null &&
        caseId != Guid.Empty &&
        IntakeWorkflowOptionsGuard.IsUtc(nowUtc);

    private static bool ValidMutation(
        TrustedFirmId? firm,
        Guid caseId,
        Guid actorReference,
        DateTimeOffset nowUtc) =>
        ValidScope(firm, caseId, nowUtc) &&
        actorReference != Guid.Empty;

    private static IntakeResultCode Map(IntakeStoreWriteCode code) =>
        code switch
        {
            IntakeStoreWriteCode.NotFound => IntakeResultCode.NotFound,
            IntakeStoreWriteCode.Conflict => IntakeResultCode.Conflict,
            IntakeStoreWriteCode.Duplicate => IntakeResultCode.Duplicate,
            _ => IntakeResultCode.Invalid,
        };
}
