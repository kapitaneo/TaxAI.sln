using ClientIntake.Contracts;
using ClientIntake.Models;

namespace ClientIntake.Services;

/// <summary>
/// The client side of the intake workflow.
/// <para>
/// T02 implements the frozen T01 contract behind in-process services only. No
/// client intake route, upload transport, or component is mapped in this wave.
/// </para>
/// <para>
/// It takes no review repository, because a client never reaches a review record,
/// and no clock or HTTP context, because scope and time both arrive as arguments.
/// Its only extra dependency over the accountant side is the content store — the
/// client is the only actor that submits bytes.
/// </para>
/// </summary>
public sealed class ClientIntakeWorkflow : IClientIntakeWorkflow
{
    private readonly IIntakeStore _store;
    private readonly IIntakeContentStore _contentStore;
    private readonly IIntakeCredentialSource _credentialSource;
    private readonly IntakeWorkflowOptions _options;

    public ClientIntakeWorkflow(
        IIntakeStore store,
        IIntakeContentStore contentStore,
        IIntakeCredentialSource credentialSource,
        IntakeWorkflowOptions options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(credentialSource);

        _store = store;
        _contentStore = contentStore;
        _credentialSource = credentialSource;
        _options = IntakeWorkflowOptionsGuard.RequireValid(options);
    }

    public async Task<IntakeResult<IntakeLinkInspection>> InspectLinkAsync(
        IntakeSecret credential,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (credential is null || !IntakeWorkflowOptionsGuard.IsUtc(nowUtc))
        {
            return IntakeResult<IntakeLinkInspection>.Failure(IntakeResultCode.NotFound);
        }

        try
        {
            var digest = _credentialSource.Digest(
                credential,
                IntakeTokenPurpose.BootstrapLink);
            var scope = await _store.FindLinkScopeAsync(
                digest,
                nowUtc,
                cancellationToken);
            if (scope is null)
            {
                return IntakeResult<IntakeLinkInspection>.Failure(IntakeResultCode.NotFound);
            }

            var storedCase = await _store.FindAsync(
                scope.Firm,
                scope.CaseId,
                cancellationToken);
            if (storedCase is null ||
                !storedCase.CurrentLink.CredentialDigest.Equals(scope.LinkDigest) ||
                storedCase.CurrentLink.ExpiresAtUtc != scope.ExpiresAtUtc ||
                IntakeProjection.LinkState(storedCase.CurrentLink, nowUtc) !=
                    IntakeLinkState.Active)
            {
                return IntakeResult<IntakeLinkInspection>.Failure(IntakeResultCode.NotFound);
            }

            return IntakeResult<IntakeLinkInspection>.Success(
                new IntakeLinkInspection(
                    storedCase.TaxYear,
                    new IntakeRedemptionProof(scope.LinkDigest, scope.ExpiresAtUtc)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return IntakeResult<IntakeLinkInspection>.Failure(IntakeResultCode.NotFound);
        }
    }

    public async Task<IntakeResult<RedeemedIntakeSession>> RedeemLinkAsync(
        IntakeRedemptionProof proof,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (proof is null ||
            !IntakeWorkflowOptionsGuard.IsUtc(nowUtc) ||
            nowUtc >= proof.LinkExpiresAtUtc)
        {
            return IntakeResult<RedeemedIntakeSession>.Failure(IntakeResultCode.NotFound);
        }

        try
        {
            var linkScope = await _store.FindLinkScopeAsync(
                proof.LinkDigest,
                nowUtc,
                cancellationToken);
            if (linkScope is null ||
                linkScope.ExpiresAtUtc != proof.LinkExpiresAtUtc)
            {
                return IntakeResult<RedeemedIntakeSession>.Failure(IntakeResultCode.NotFound);
            }

            var credential = _credentialSource.CreateCredential();
            var sessionId = _credentialSource.CreateIdentifier();
            if (sessionId == Guid.Empty)
            {
                return IntakeResult<RedeemedIntakeSession>.Failure(IntakeResultCode.Invalid);
            }

            var idle = IntakeWorkflowOptionsGuard.RenewIdle(
                nowUtc,
                _options.ClientSessionIdleLifetime,
                linkScope.ExpiresAtUtc);
            var session = new NewIntakeSession(
                sessionId,
                _credentialSource.Digest(
                    credential,
                    IntakeTokenPurpose.ClientSession),
                nowUtc,
                linkScope.ExpiresAtUtc,
                idle);
            var audit = ClientAudit(
                IntakeAuditKind.LinkRedeemed,
                sessionId,
                nowUtc);
            var write = await _store.RedeemLinkAsync(
                linkScope,
                session,
                audit,
                cancellationToken);

            return write == IntakeStoreWriteCode.Success
                ? IntakeResult<RedeemedIntakeSession>.Success(
                    new RedeemedIntakeSession(
                        credential,
                        linkScope.ExpiresAtUtc))
                : IntakeResult<RedeemedIntakeSession>.Failure(Map(write));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return IntakeResult<RedeemedIntakeSession>.Failure(IntakeResultCode.NotFound);
        }
    }

    public async Task<IntakeResult<ClientIntakeView>> GetAsync(
        IntakeSecret sessionCredential,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(
            sessionCredential,
            nowUtc,
            cancellationToken);
        return Project(resolved.Case);
    }

    public async Task<IntakeResult<ClientIntakeView>> SaveAnswersAsync(
        IntakeSecret sessionCredential,
        SaveIntakeAnswersCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (command?.Answers is null || command.Answers.Count == 0)
        {
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.Invalid);
        }

        var resolved = await ResolveAsync(
            sessionCredential,
            nowUtc,
            cancellationToken);
        if (resolved.Scope is null || resolved.Case is null)
        {
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.NotFound);
        }

        var requested = resolved.Case.ExpectedItems
            .Select(item => item.Category)
            .Distinct()
            .ToHashSet();
        var categories = new HashSet<TaxCases.Models.DocumentCategory>();
        foreach (var answer in command.Answers)
        {
            if (answer is null ||
                answer.Category is null ||
                answer.Answer is null ||
                !Enum.IsDefined(answer.Category.Value) ||
                !Enum.IsDefined(answer.Answer.Value) ||
                !IntakeQuestionnairePolicy.IsSupportedCategory(answer.Category.Value) ||
                !requested.Contains(answer.Category.Value) ||
                !categories.Add(answer.Category.Value))
            {
                return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.Invalid);
            }
        }

        var current = IntakeQuestionnairePolicy.CurrentAnswers(resolved.Case);
        var additions = new List<IntakeAnswerVersionRecord>();
        foreach (var input in command.Answers)
        {
            var category = input.Category!.Value;
            if (current.TryGetValue(category, out var existing) &&
                existing.Answer == input.Answer)
            {
                continue;
            }

            var nextVersion = 1;
            if (existing is not null &&
                !TryIncrementVersion(existing.Version, out nextVersion))
            {
                return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.Invalid);
            }

            additions.Add(new IntakeAnswerVersionRecord(
                category,
                nextVersion,
                input.Answer!.Value,
                IntakeActorKind.Client,
                resolved.Scope.SessionId,
                nowUtc));
        }

        if (additions.Count == 0)
        {
            return Project(resolved.Case);
        }

        var events = additions
            .Select(_ => ClientAudit(
                IntakeAuditKind.AnswerSaved,
                resolved.Scope.SessionId,
                nowUtc))
            .ToArray();
        var renewed = IntakeWorkflowOptionsGuard.RenewIdle(
            nowUtc,
            _options.ClientSessionIdleLifetime,
            resolved.Scope.AbsoluteExpiresAtUtc);

        try
        {
            var write = await _store.AppendAnswersAsync(
                resolved.Scope,
                resolved.Case.ConcurrencyVersion,
                renewed,
                additions,
                events,
                cancellationToken);
            return write == IntakeStoreWriteCode.Success
                ? await ReloadAsync(resolved.Scope, cancellationToken)
                : IntakeResult<ClientIntakeView>.Failure(Map(write));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.NotFound);
        }
    }

    public async Task<IntakeResult<ClientIntakeView>> UploadAsync(
        IntakeSecret sessionCredential,
        IntakeUploadCommand command,
        Stream content,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(
            sessionCredential,
            nowUtc,
            cancellationToken);
        if (resolved.Scope is null || resolved.Case is null)
        {
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.NotFound);
        }

        if (command is null ||
            command.Category is null ||
            command.Ordinal is null ||
            !Enum.IsDefined(command.Category.Value) ||
            !IntakeQuestionnairePolicy.IsSupportedCategory(command.Category.Value))
        {
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.NotFound);
        }

        var expected = resolved.Case.ExpectedItems.SingleOrDefault(item =>
            item.Category == command.Category.Value &&
            item.Ordinal == command.Ordinal.Value);
        if (expected is null)
        {
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.NotFound);
        }

        Stream? validated;
        try
        {
            validated = await IntakeUploadValidator.ValidateAsync(
                command,
                content,
                _options.MaximumFileBytes,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            validated = null;
        }

        if (validated is null)
        {
            return await RecordRejectedUploadAsync(
                resolved.Scope,
                resolved.Case,
                expected,
                IntakeUploadOutcome.Rejected,
                IntakeAuditKind.UploadRejected,
                nowUtc,
                cancellationToken);
        }

        StagedIntakeContent staged;
        try
        {
            staged = await _contentStore.StageAsync(
                resolved.Scope.Firm,
                resolved.Scope.CaseId,
                validated,
                _options.MaximumFileBytes,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.Invalid);
        }

        if (staged.ByteCount <= 0 ||
            staged.ByteCount > _options.MaximumFileBytes ||
            staged.ByteCount != command.DeclaredLength ||
            staged.Fingerprint.Count != 32 ||
            string.IsNullOrWhiteSpace(staged.ContentKey))
        {
            await CleanupAsync(resolved.Scope, staged.ContentKey);
            return await RecordRejectedUploadAsync(
                resolved.Scope,
                resolved.Case,
                expected,
                IntakeUploadOutcome.Rejected,
                IntakeAuditKind.UploadRejected,
                nowUtc,
                cancellationToken);
        }

        if (!TryNextVersion(
                resolved.Case.Documents
                    .Where(document =>
                        document.ExpectedItemId == expected.ExpectedItemId)
                    .Select(document => document.Version),
                out var nextVersion))
        {
            await CleanupAsync(resolved.Scope, staged.ContentKey);
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.Invalid);
        }

        IntakeUploadAttemptRecord attempt;
        IntakeDocumentRecord document;
        try
        {
            attempt = new IntakeUploadAttemptRecord(
                _credentialSource.CreateIdentifier(),
                expected.ExpectedItemId,
                IntakeUploadOutcome.Accepted,
                nowUtc);
            document = new IntakeDocumentRecord(
                _credentialSource.CreateIdentifier(),
                expected.ExpectedItemId,
                expected.Category,
                nextVersion,
                staged.ContentKey,
                command.DeclaredMediaKind!.Value,
                staged.ByteCount,
                staged.Fingerprint,
                nowUtc);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await CleanupAsync(resolved.Scope, staged.ContentKey);
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.Invalid);
        }

        if (attempt.AttemptId == Guid.Empty || document.DocumentId == Guid.Empty)
        {
            await CleanupAsync(resolved.Scope, staged.ContentKey);
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.Invalid);
        }

        var renewed = IntakeWorkflowOptionsGuard.RenewIdle(
            nowUtc,
            _options.ClientSessionIdleLifetime,
            resolved.Scope.AbsoluteExpiresAtUtc);
        IntakeStoreWriteCode write;
        try
        {
            write = await _store.AppendUploadAttemptAsync(
                resolved.Scope,
                resolved.Case.ConcurrencyVersion,
                renewed,
                attempt,
                document,
                ClientAudit(
                    IntakeAuditKind.UploadAccepted,
                    resolved.Scope.SessionId,
                    nowUtc,
                    expected.ExpectedItemId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await CleanupAsync(resolved.Scope, staged.ContentKey);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            write = IntakeStoreWriteCode.Conflict;
        }

        if (write == IntakeStoreWriteCode.Success)
        {
            return await ReloadAsync(resolved.Scope, cancellationToken);
        }

        await CleanupAsync(resolved.Scope, staged.ContentKey);
        if (write is IntakeStoreWriteCode.Duplicate or
            IntakeStoreWriteCode.Conflict or
            IntakeStoreWriteCode.NotFound)
        {
            StoredIntakeCase? fresh;
            try
            {
                fresh = await _store.FindForClientAsync(
                    resolved.Scope,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return IntakeResult<ClientIntakeView>.Failure(Map(write));
            }

            if (fresh is not null &&
                fresh.Documents.Any(existing =>
                    existing.Fingerprint.SequenceEqual(staged.Fingerprint)))
            {
                _ = await RecordRejectedUploadAsync(
                    resolved.Scope,
                    fresh,
                    expected,
                    IntakeUploadOutcome.Duplicate,
                    IntakeAuditKind.DuplicateRejected,
                    nowUtc,
                    cancellationToken);
                return IntakeResult<ClientIntakeView>.Failure(
                    IntakeResultCode.Duplicate);
            }
        }

        return IntakeResult<ClientIntakeView>.Failure(Map(write));
    }

    public async Task<IntakeResult<ClientIntakeView>> SubmitAsync(
        IntakeSecret sessionCredential,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(
            sessionCredential,
            nowUtc,
            cancellationToken);
        if (resolved.Scope is null || resolved.Case is null)
        {
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.NotFound);
        }

        var currentAnswers = IntakeQuestionnairePolicy.CurrentAnswers(resolved.Case);
        var answerReferences = currentAnswers
            .OrderBy(pair => (int)pair.Key)
            .Select(pair => new IntakeSubmissionAnswerReference(
                pair.Key,
                pair.Value.Version))
            .ToArray();
        var documentReferences = resolved.Case.Documents
            .GroupBy(document => document.ExpectedItemId)
            .Select(group => group.OrderByDescending(document => document.Version).First())
            .OrderBy(document => (int)document.Category)
            .ThenBy(document => resolved.Case.ExpectedItems
                .Single(item => item.ExpectedItemId == document.ExpectedItemId)
                .Ordinal)
            .Select(document => new IntakeSubmissionDocumentReference(document.DocumentId))
            .ToArray();
        if (!TryNextVersion(
                resolved.Case.Submissions.Select(submission => submission.Version),
                out var nextVersion))
        {
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.Invalid);
        }

        var submission = new IntakeSubmissionRecord(
            nextVersion,
            answerReferences,
            documentReferences,
            IntakeActorKind.Client,
            resolved.Scope.SessionId,
            nowUtc);
        var previous = IntakeStateReplayer.ReplayLatestSubmission(resolved.Case);
        var projected = WithSubmission(resolved.Case, submission);
        var next = IntakeStateReplayer.ReplayLatestSubmission(projected);
        if (previous is null || next is null)
        {
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.Invalid);
        }

        var events = new List<IntakeAuditEventRecord>
        {
            ClientAudit(
                IntakeAuditKind.SubmissionCreated,
                resolved.Scope.SessionId,
                nowUtc),
        };
        if (previous.Readiness.State != next.Readiness.State)
        {
            events.Add(ClientAudit(
                IntakeAuditKind.ReadinessChanged,
                resolved.Scope.SessionId,
                nowUtc));
        }

        var renewed = IntakeWorkflowOptionsGuard.RenewIdle(
            nowUtc,
            _options.ClientSessionIdleLifetime,
            resolved.Scope.AbsoluteExpiresAtUtc);
        try
        {
            var write = await _store.AppendSubmissionAsync(
                resolved.Scope,
                resolved.Case.ConcurrencyVersion,
                renewed,
                submission,
                events,
                cancellationToken);
            return write == IntakeStoreWriteCode.Success
                ? await ReloadAsync(resolved.Scope, cancellationToken)
                : IntakeResult<ClientIntakeView>.Failure(Map(write));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.Conflict);
        }
    }

    private async Task<(IntakeClientScope? Scope, StoredIntakeCase? Case)> ResolveAsync(
        IntakeSecret sessionCredential,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (sessionCredential is null || !IntakeWorkflowOptionsGuard.IsUtc(nowUtc))
        {
            return (null, null);
        }

        try
        {
            var digest = _credentialSource.Digest(
                sessionCredential,
                IntakeTokenPurpose.ClientSession);
            var scope = await _store.FindClientScopeAsync(
                digest,
                nowUtc,
                cancellationToken);
            if (scope is null)
            {
                return (null, null);
            }

            var storedCase = await _store.FindForClientAsync(
                scope,
                cancellationToken);
            return storedCase is null
                ? (null, null)
                : (scope, storedCase);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, null);
        }
    }

    private async Task<IntakeResult<ClientIntakeView>> ReloadAsync(
        IntakeClientScope scope,
        CancellationToken cancellationToken)
    {
        var storedCase = await _store.FindForClientAsync(scope, cancellationToken);
        return Project(storedCase);
    }

    private static IntakeResult<ClientIntakeView> Project(StoredIntakeCase? storedCase)
    {
        if (storedCase is null)
        {
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.NotFound);
        }

        var view = IntakeProjection.Client(storedCase);
        return view is null
            ? IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.NotFound)
            : IntakeResult<ClientIntakeView>.Success(view);
    }

    private async Task<IntakeResult<ClientIntakeView>> RecordRejectedUploadAsync(
        IntakeClientScope scope,
        StoredIntakeCase storedCase,
        IntakeExpectedItemRecord expected,
        IntakeUploadOutcome outcome,
        IntakeAuditKind auditKind,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var renewed = IntakeWorkflowOptionsGuard.RenewIdle(
            nowUtc,
            _options.ClientSessionIdleLifetime,
            scope.AbsoluteExpiresAtUtc);
        try
        {
            var write = await _store.AppendUploadAttemptAsync(
                scope,
                storedCase.ConcurrencyVersion,
                renewed,
                new IntakeUploadAttemptRecord(
                    _credentialSource.CreateIdentifier(),
                    expected.ExpectedItemId,
                    outcome,
                    nowUtc),
                document: null,
                ClientAudit(
                    auditKind,
                    scope.SessionId,
                    nowUtc,
                    expected.ExpectedItemId),
                cancellationToken);
            if (write != IntakeStoreWriteCode.Success)
            {
                return IntakeResult<ClientIntakeView>.Failure(Map(write));
            }

            return IntakeResult<ClientIntakeView>.Failure(
                outcome == IntakeUploadOutcome.Duplicate
                    ? IntakeResultCode.Duplicate
                    : IntakeResultCode.Invalid);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.NotFound);
        }
    }

    private async Task CleanupAsync(IntakeClientScope scope, string contentKey)
    {
        try
        {
            await _contentStore.DeleteIfExistsAsync(
                scope.Firm,
                scope.CaseId,
                contentKey,
                CancellationToken.None);
        }
        catch (Exception)
        {
            // Compensating cleanup is best-effort at this boundary. The object
            // remains unreachable because metadata was never committed.
        }
    }

    private IntakeAuditEventRecord ClientAudit(
        IntakeAuditKind kind,
        Guid actorReference,
        DateTimeOffset atUtc,
        Guid? expectedItemId = null) =>
        new(
            _credentialSource.CreateIdentifier(),
            Sequence: 0,
            kind,
            IntakeActorKind.Client,
            actorReference,
            expectedItemId,
            atUtc);

    private static StoredIntakeCase WithSubmission(
        StoredIntakeCase storedCase,
        IntakeSubmissionRecord submission) =>
        new(
            storedCase.Firm,
            storedCase.CaseId,
            storedCase.PrimaryParticipantId,
            storedCase.PrimaryTaxpayerDisplayName,
            storedCase.TaxYear,
            storedCase.CaseStatus,
            storedCase.ConcurrencyVersion,
            storedCase.ExpectedItems,
            storedCase.CurrentLink,
            storedCase.AnswerVersions,
            storedCase.Documents,
            storedCase.UploadAttempts,
            [.. storedCase.Submissions, submission],
            storedCase.ReviewAssociation);

    private static IntakeResultCode Map(IntakeStoreWriteCode code) =>
        code switch
        {
            IntakeStoreWriteCode.NotFound => IntakeResultCode.NotFound,
            IntakeStoreWriteCode.Conflict => IntakeResultCode.Conflict,
            IntakeStoreWriteCode.Duplicate => IntakeResultCode.Duplicate,
            _ => IntakeResultCode.Invalid,
        };

    private static bool TryNextVersion(
        IEnumerable<int> existingVersions,
        out int nextVersion)
    {
        var maximum = existingVersions.DefaultIfEmpty(0).Max();
        return TryIncrementVersion(maximum, out nextVersion);
    }

    private static bool TryIncrementVersion(int version, out int nextVersion)
    {
        if (version < 0 || version == int.MaxValue)
        {
            nextVersion = 0;
            return false;
        }

        nextVersion = version + 1;
        return true;
    }
}
