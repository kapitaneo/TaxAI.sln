using ClientIntake.Contracts;
using ClientIntake.Models;
using Tenancy.Models;

namespace ClientIntake.Tests.Support;

internal sealed class InMemoryIntakeStore : IIntakeStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid Firm, Guid Case), StoredIntakeCase> _cases = [];
    private readonly List<SessionState> _sessions = [];
    private readonly Dictionary<(Guid Firm, Guid Case), List<IntakeAuditEventRecord>> _audits = [];

    internal bool CancelNextUploadMutation { get; set; }

    internal IReadOnlyList<IntakeAuditEventRecord> Audit(
        TrustedFirmId firm,
        Guid caseId)
    {
        lock (_gate)
        {
            return _audits.TryGetValue((firm.Value, caseId), out var events)
                ? events.ToArray()
                : [];
        }
    }

    public Task<IntakeStoreWriteCode> CreateAsync(
        NewIntakeCase intakeCase,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (intakeCase.Case.Firm.Value, intakeCase.Case.CaseId);
            if (_cases.ContainsKey(key))
            {
                return Task.FromResult(IntakeStoreWriteCode.Conflict);
            }

            _cases.Add(key, intakeCase.Case);
            _audits.Add(key, intakeCase.AuditEvents.ToList());
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeLinkScope?> FindLinkScopeAsync(
        IntakeTokenDigest linkDigest,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var match = _cases.Values.SingleOrDefault(candidate =>
                candidate.CurrentLink.CredentialDigest.Equals(linkDigest) &&
                candidate.CurrentLink.RevokedAtUtc is null &&
                candidate.CurrentLink.SpentAtUtc is null &&
                nowUtc < candidate.CurrentLink.ExpiresAtUtc);
            return Task.FromResult(match is null
                ? null
                : new IntakeLinkScope(
                    match.Firm,
                    match.CaseId,
                    linkDigest,
                    match.CurrentLink.ExpiresAtUtc));
        }
    }

    public Task<IntakeStoreWriteCode> RedeemLinkAsync(
        IntakeLinkScope linkScope,
        NewIntakeSession session,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (linkScope.Firm.Value, linkScope.CaseId);
            if (!_cases.TryGetValue(key, out var storedCase) ||
                !storedCase.CurrentLink.CredentialDigest.Equals(linkScope.LinkDigest) ||
                storedCase.CurrentLink.RevokedAtUtc is not null ||
                storedCase.CurrentLink.SpentAtUtc is not null ||
                session.CreatedAtUtc >= storedCase.CurrentLink.ExpiresAtUtc)
            {
                return Task.FromResult(IntakeStoreWriteCode.NotFound);
            }

            _sessions.Add(new SessionState(
                storedCase.Firm,
                storedCase.CaseId,
                session.SessionId,
                session.SessionDigest,
                session.AbsoluteExpiresAtUtc,
                session.IdleExpiresAtUtc,
                RevokedAtUtc: null));
            _cases[key] = Copy(
                storedCase,
                concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                currentLink: storedCase.CurrentLink with
                {
                    SpentAtUtc = session.CreatedAtUtc,
                });
            AddAudits(key, [auditEvent]);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeClientScope?> FindClientScopeAsync(
        IntakeTokenDigest sessionDigest,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var session = _sessions.SingleOrDefault(candidate =>
                candidate.Digest.Equals(sessionDigest) &&
                candidate.RevokedAtUtc is null &&
                nowUtc < candidate.IdleExpiresAtUtc &&
                nowUtc < candidate.AbsoluteExpiresAtUtc);
            return Task.FromResult(session is null
                ? null
                : new IntakeClientScope(
                    session.Firm,
                    session.CaseId,
                    session.SessionId,
                    session.Digest,
                    session.AbsoluteExpiresAtUtc,
                    session.IdleExpiresAtUtc,
                    nowUtc));
        }
    }

    public Task<IntakeCaseList> ListCasesAsync(
        TrustedFirmId firm,
        int maximumCases,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var newest = _cases.Values
                .Where(candidate => candidate.Firm.Value == firm.Value)
                .Select(candidate => new
                {
                    Case = candidate,
                    CreatedAtUtc = _audits[(candidate.Firm.Value, candidate.CaseId)]
                        .Where(audit => audit.Kind == IntakeAuditKind.CaseCreated)
                        .Min(audit => audit.OccurredAtUtc),
                })
                .OrderByDescending(candidate => candidate.CreatedAtUtc)
                .ThenBy(candidate => candidate.Case.CaseId)
                .Take(maximumCases + 1)
                .Select(candidate => new IntakeCaseListEntry(
                    candidate.Case.CaseId,
                    candidate.Case.PrimaryTaxpayerDisplayName,
                    candidate.Case.TaxYear))
                .ToArray();
            return Task.FromResult(new IntakeCaseList(
                newest.Take(maximumCases).ToArray(),
                newest.Length > maximumCases));
        }
    }

    public Task<StoredIntakeCase?> FindAsync(
        TrustedFirmId firm,
        Guid caseId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _cases.TryGetValue((firm.Value, caseId), out var storedCase);
            return Task.FromResult(storedCase);
        }
    }

    public Task<StoredIntakeCase?> FindForClientAsync(
        IntakeClientScope scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(Live(scope)
                ? _cases.GetValueOrDefault((scope.Firm.Value, scope.CaseId))
                : null);
        }
    }

    public Task<IntakeStoreWriteCode> ReissueLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        IntakeLinkRecord replacement,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (firm.Value, caseId);
            if (!_cases.TryGetValue(key, out var storedCase) ||
                storedCase.ConcurrencyVersion != expectedConcurrencyVersion)
            {
                return Task.FromResult(IntakeStoreWriteCode.NotFound);
            }

            RevokeSessions(key, replacement.IssuedAtUtc);
            _cases[key] = Copy(
                storedCase,
                concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                currentLink: replacement);
            AddAudits(key, auditEvents);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeStoreWriteCode> RevokeLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        DateTimeOffset revokedAtUtc,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (firm.Value, caseId);
            if (!_cases.TryGetValue(key, out var storedCase) ||
                storedCase.ConcurrencyVersion != expectedConcurrencyVersion ||
                storedCase.CurrentLink.RevokedAtUtc is not null)
            {
                return Task.FromResult(IntakeStoreWriteCode.Conflict);
            }

            RevokeSessions(key, revokedAtUtc);
            _cases[key] = Copy(
                storedCase,
                concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                currentLink: storedCase.CurrentLink with
                {
                    RevokedAtUtc = revokedAtUtc,
                });
            AddAudits(key, [auditEvent]);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeStoreWriteCode> AppendAnswersAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IReadOnlyList<IntakeAnswerVersionRecord> answers,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!TryMutation(scope, expectedConcurrencyVersion, renewedIdleExpiresAtUtc, out var storedCase))
            {
                return Task.FromResult(IntakeStoreWriteCode.NotFound);
            }

            if (answers.Any(answer => storedCase.AnswerVersions.Any(existing =>
                    existing.Category == answer.Category &&
                    existing.Version == answer.Version)))
            {
                return Task.FromResult(IntakeStoreWriteCode.Conflict);
            }

            StoreMutation(
                scope,
                Copy(
                    storedCase,
                    concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                    answers: [.. storedCase.AnswerVersions, .. answers]),
                renewedIdleExpiresAtUtc,
                auditEvents);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeStoreWriteCode> AppendUploadAttemptAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IntakeUploadAttemptRecord attempt,
        IntakeDocumentRecord? document,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CancelNextUploadMutation)
        {
            CancelNextUploadMutation = false;
            throw new OperationCanceledException(cancellationToken);
        }

        lock (_gate)
        {
            if (!TryMutation(scope, expectedConcurrencyVersion, renewedIdleExpiresAtUtc, out var storedCase))
            {
                if (document is not null &&
                    storedCase is not null &&
                    storedCase.Documents.Any(existing =>
                        existing.Fingerprint.SequenceEqual(document.Fingerprint)))
                {
                    return Task.FromResult(IntakeStoreWriteCode.Duplicate);
                }

                return Task.FromResult(IntakeStoreWriteCode.NotFound);
            }

            if (document is not null &&
                storedCase.Documents.Any(existing =>
                    existing.Fingerprint.SequenceEqual(document.Fingerprint)))
            {
                return Task.FromResult(IntakeStoreWriteCode.Duplicate);
            }

            StoreMutation(
                scope,
                Copy(
                    storedCase,
                    concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                    documents: document is null
                        ? storedCase.Documents
                        : [.. storedCase.Documents, document],
                    attempts: [.. storedCase.UploadAttempts, attempt]),
                renewedIdleExpiresAtUtc,
                [auditEvent]);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeStoreWriteCode> AppendSubmissionAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IntakeSubmissionRecord submission,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!TryMutation(scope, expectedConcurrencyVersion, renewedIdleExpiresAtUtc, out var storedCase))
            {
                return Task.FromResult(IntakeStoreWriteCode.NotFound);
            }

            if (storedCase.Submissions.Any(existing =>
                    existing.Version == submission.Version))
            {
                return Task.FromResult(IntakeStoreWriteCode.Conflict);
            }

            StoreMutation(
                scope,
                Copy(
                    storedCase,
                    concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                    submissions: [.. storedCase.Submissions, submission]),
                renewedIdleExpiresAtUtc,
                auditEvents);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeStoreWriteCode> AssociateReviewAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        IntakeReviewAssociationRecord association,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (firm.Value, caseId);
            if (!_cases.TryGetValue(key, out var storedCase))
            {
                return Task.FromResult(IntakeStoreWriteCode.NotFound);
            }

            if (storedCase.ConcurrencyVersion != expectedConcurrencyVersion ||
                storedCase.ReviewAssociation is not null)
            {
                return Task.FromResult(IntakeStoreWriteCode.Conflict);
            }

            _cases[key] = Copy(
                storedCase,
                concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                association: association,
                replaceAssociation: true);
            AddAudits(key, [auditEvent]);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    private bool Live(IntakeClientScope scope) =>
        _sessions.Any(session =>
            session.Firm.Equals(scope.Firm) &&
            session.CaseId == scope.CaseId &&
            session.SessionId == scope.SessionId &&
            session.Digest.Equals(scope.SessionDigest) &&
            session.RevokedAtUtc is null &&
            scope.EvaluationAtUtc < session.IdleExpiresAtUtc &&
            scope.EvaluationAtUtc < session.AbsoluteExpiresAtUtc);

    private bool TryMutation(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        out StoredIntakeCase storedCase)
    {
        storedCase = null!;
        if (!Live(scope))
        {
            return false;
        }

        if (!_cases.TryGetValue(
                (scope.Firm.Value, scope.CaseId),
                out var existing))
        {
            return false;
        }

        storedCase = existing;
        return storedCase.ConcurrencyVersion == expectedConcurrencyVersion &&
            renewedIdleExpiresAtUtc <= scope.AbsoluteExpiresAtUtc;
    }

    private void StoreMutation(
        IntakeClientScope scope,
        StoredIntakeCase replacement,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IReadOnlyList<IntakeAuditEventRecord> audits)
    {
        _cases[(scope.Firm.Value, scope.CaseId)] = replacement;
        var index = _sessions.FindIndex(session =>
            session.SessionId == scope.SessionId &&
            session.Firm.Equals(scope.Firm) &&
            session.CaseId == scope.CaseId);
        _sessions[index] = _sessions[index] with
        {
            IdleExpiresAtUtc = renewedIdleExpiresAtUtc,
        };
        AddAudits((scope.Firm.Value, scope.CaseId), audits);
    }

    private void RevokeSessions(
        (Guid Firm, Guid Case) key,
        DateTimeOffset revokedAtUtc)
    {
        for (var index = 0; index < _sessions.Count; index++)
        {
            var session = _sessions[index];
            if (session.Firm.Value == key.Firm && session.CaseId == key.Case)
            {
                _sessions[index] = session with { RevokedAtUtc = revokedAtUtc };
            }
        }
    }

    private void AddAudits(
        (Guid Firm, Guid Case) key,
        IReadOnlyList<IntakeAuditEventRecord> events)
    {
        var target = _audits[key];
        foreach (var audit in events)
        {
            target.Add(audit with { Sequence = target.Count + 1 });
        }
    }

    private static StoredIntakeCase Copy(
        StoredIntakeCase source,
        long? concurrencyVersion = null,
        IntakeLinkRecord? currentLink = null,
        IReadOnlyList<IntakeAnswerVersionRecord>? answers = null,
        IReadOnlyList<IntakeDocumentRecord>? documents = null,
        IReadOnlyList<IntakeUploadAttemptRecord>? attempts = null,
        IReadOnlyList<IntakeSubmissionRecord>? submissions = null,
        IntakeReviewAssociationRecord? association = null,
        bool replaceAssociation = false) =>
        new(
            source.Firm,
            source.CaseId,
            source.PrimaryParticipantId,
            source.PrimaryTaxpayerDisplayName,
            source.TaxYear,
            source.CaseStatus,
            concurrencyVersion ?? source.ConcurrencyVersion,
            source.ExpectedItems,
            currentLink ?? source.CurrentLink,
            answers ?? source.AnswerVersions,
            documents ?? source.Documents,
            attempts ?? source.UploadAttempts,
            submissions ?? source.Submissions,
            replaceAssociation ? association : source.ReviewAssociation);

    private sealed record SessionState(
        TrustedFirmId Firm,
        Guid CaseId,
        Guid SessionId,
        IntakeTokenDigest Digest,
        DateTimeOffset AbsoluteExpiresAtUtc,
        DateTimeOffset IdleExpiresAtUtc,
        DateTimeOffset? RevokedAtUtc);
}
