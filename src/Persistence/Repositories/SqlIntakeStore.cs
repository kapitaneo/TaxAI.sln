using System.Data;
using ClientIntake.Contracts;
using ClientIntake.Models;
using Microsoft.EntityFrameworkCore;
using Persistence.Entities;
using Tenancy.Models;

namespace Persistence.Repositories;

internal sealed class SqlIntakeStore : IIntakeStore
{
    private readonly TaxAiDbContext _context;

    public SqlIntakeStore(TaxAiDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<IntakeStoreWriteCode> CreateAsync(
        NewIntakeCase intakeCase,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intakeCase);
        if (!ValidNewCase(intakeCase))
        {
            return IntakeStoreWriteCode.Conflict;
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var stored = intakeCase.Case;
            _context.Add(new IntakeCaseEntity
            {
                FirmId = stored.Firm.Value,
                CaseId = stored.CaseId,
                PrimaryParticipantId = stored.PrimaryParticipantId,
                PrimaryTaxpayerDisplayName = stored.PrimaryTaxpayerDisplayName,
                TaxYear = stored.TaxYear,
                CaseStatus = stored.CaseStatus,
                QuestionnaireVersion = 1,
                ConcurrencyVersion = 0,
            });
            _context.AddRange(stored.ExpectedItems.Select(item =>
                new IntakeExpectedItemEntity
                {
                    FirmId = stored.Firm.Value,
                    CaseId = stored.CaseId,
                    ExpectedItemId = item.ExpectedItemId,
                    Category = item.Category,
                    Ordinal = item.Ordinal,
                }));
            _context.Add(LinkEntity(stored, stored.CurrentLink));
            _context.AddRange(intakeCase.AuditEvents.Select((audit, index) =>
                AuditEntity(stored.Firm.Value, stored.CaseId, audit, index + 1)));

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return IntakeStoreWriteCode.Success;
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction);
            return IntakeStoreWriteCode.Conflict;
        }
    }

    public async Task<IntakeLinkScope?> FindLinkScopeAsync(
        IntakeTokenDigest linkDigest,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(linkDigest);
        var bytes = linkDigest.Bytes.ToArray();
        var link = await _context.Set<IntakeLinkEntity>()
            .AsNoTracking()
            .Where(candidate =>
                candidate.CredentialDigest == bytes &&
                candidate.RevokedAtUtc == null &&
                candidate.SpentAtUtc == null &&
                _context.Set<IntakeCaseEntity>().Any(intakeCase =>
                    intakeCase.FirmId == candidate.FirmId &&
                    intakeCase.CaseId == candidate.CaseId &&
                    intakeCase.CaseStatus != TaxCases.Models.CaseStatus.Cancelled))
            .Select(candidate => new
            {
                candidate.FirmId,
                candidate.CaseId,
                candidate.ExpiresAtUtc,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return link is null || nowUtc >= link.ExpiresAtUtc
            ? null
            : new IntakeLinkScope(
                TrustedFirmId.FromGuid(link.FirmId),
                link.CaseId,
                linkDigest,
                link.ExpiresAtUtc);
    }

    public async Task<IntakeStoreWriteCode> RedeemLinkAsync(
        IntakeLinkScope linkScope,
        NewIntakeSession session,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(linkScope);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(auditEvent);
        if (session.SessionId == Guid.Empty ||
            session.CreatedAtUtc >= session.AbsoluteExpiresAtUtc ||
            session.IdleExpiresAtUtc > session.AbsoluteExpiresAtUtc ||
            session.CreatedAtUtc >= session.IdleExpiresAtUtc)
        {
            return IntakeStoreWriteCode.Conflict;
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var digest = linkScope.LinkDigest.Bytes.ToArray();
            var link = await _context.Set<IntakeLinkEntity>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.FirmId == linkScope.Firm.Value &&
                        candidate.CaseId == linkScope.CaseId &&
                        candidate.CredentialDigest == digest &&
                        candidate.ExpiresAtUtc == linkScope.ExpiresAtUtc,
                    cancellationToken);
            if (link is null)
            {
                await RollbackAsync(transaction);
                return IntakeStoreWriteCode.NotFound;
            }
            if (session.CreatedAtUtc >= link.ExpiresAtUtc)
            {
                await RollbackAsync(transaction);
                return IntakeStoreWriteCode.NotFound;
            }

            var affected = await _context.Set<IntakeLinkEntity>()
                .Where(candidate =>
                    candidate.FirmId == linkScope.Firm.Value &&
                    candidate.CaseId == linkScope.CaseId &&
                    candidate.LinkId == link.LinkId &&
                    candidate.CredentialDigest == digest &&
                    candidate.RevokedAtUtc == null &&
                    candidate.SpentAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        candidate => candidate.SpentAtUtc,
                        (DateTimeOffset?)session.CreatedAtUtc),
                    cancellationToken);
            if (affected != 1)
            {
                await RollbackAsync(transaction);
                return IntakeStoreWriteCode.NotFound;
            }

            var caseAffected = await _context.Set<IntakeCaseEntity>()
                .Where(candidate =>
                    candidate.FirmId == linkScope.Firm.Value &&
                    candidate.CaseId == linkScope.CaseId &&
                    candidate.CaseStatus != TaxCases.Models.CaseStatus.Cancelled)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        candidate => candidate.ConcurrencyVersion,
                        candidate => candidate.ConcurrencyVersion + 1),
                    cancellationToken);
            if (caseAffected != 1)
            {
                await RollbackAsync(transaction);
                return IntakeStoreWriteCode.NotFound;
            }

            _context.Add(new IntakeClientSessionEntity
            {
                FirmId = linkScope.Firm.Value,
                CaseId = linkScope.CaseId,
                SessionId = session.SessionId,
                LinkId = link.LinkId,
                SessionDigest = session.SessionDigest.Bytes.ToArray(),
                CreatedAtUtc = session.CreatedAtUtc,
                AbsoluteExpiresAtUtc = session.AbsoluteExpiresAtUtc,
                IdleExpiresAtUtc = session.IdleExpiresAtUtc,
            });
            _context.Add(await NextAuditAsync(
                linkScope.Firm.Value,
                linkScope.CaseId,
                auditEvent,
                cancellationToken));

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return IntakeStoreWriteCode.Success;
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction);
            return IntakeStoreWriteCode.NotFound;
        }
    }

    public async Task<IntakeClientScope?> FindClientScopeAsync(
        IntakeTokenDigest sessionDigest,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionDigest);
        var bytes = sessionDigest.Bytes.ToArray();
        var session = await _context.Set<IntakeClientSessionEntity>()
            .AsNoTracking()
            .Where(candidate =>
                candidate.SessionDigest == bytes &&
                candidate.RevokedAtUtc == null &&
                _context.Set<IntakeCaseEntity>().Any(intakeCase =>
                    intakeCase.FirmId == candidate.FirmId &&
                    intakeCase.CaseId == candidate.CaseId &&
                    intakeCase.CaseStatus != TaxCases.Models.CaseStatus.Cancelled))
            .Select(candidate => new
            {
                candidate.FirmId,
                candidate.CaseId,
                candidate.SessionId,
                candidate.LinkId,
                candidate.AbsoluteExpiresAtUtc,
                candidate.IdleExpiresAtUtc,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (session is null ||
            nowUtc >= session.IdleExpiresAtUtc ||
            nowUtc >= session.AbsoluteExpiresAtUtc)
        {
            return null;
        }

        var link = await _context.Set<IntakeLinkEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.FirmId == session.FirmId &&
                    candidate.CaseId == session.CaseId &&
                    candidate.LinkId == session.LinkId &&
                    candidate.RevokedAtUtc == null,
                cancellationToken);

        return link is null || nowUtc >= link.ExpiresAtUtc
            ? null
            : new IntakeClientScope(
                TrustedFirmId.FromGuid(session.FirmId),
                session.CaseId,
                session.SessionId,
                sessionDigest,
                session.AbsoluteExpiresAtUtc,
                session.IdleExpiresAtUtc,
                nowUtc);
    }

    public Task<StoredIntakeCase?> FindAsync(
        TrustedFirmId firm,
        Guid caseId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        return LoadAsync(
            firm.Value,
            caseId,
            cancellationToken);
    }

    public async Task<StoredIntakeCase?> FindForClientAsync(
        IntakeClientScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var storedCase = await ClientScopeIsLiveAsync(
                scope,
                renewedIdleExpiresAtUtc: null,
                cancellationToken)
            ? await LoadAsync(
                scope.Firm.Value,
                scope.CaseId,
                cancellationToken)
            : null;
        await transaction.CommitAsync(cancellationToken);
        return storedCase;
    }

    public async Task<IntakeStoreWriteCode> ReissueLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        IntakeLinkRecord replacement,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(auditEvents);

        await using var transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await AdvanceCaseAsync(
                    firm.Value,
                    caseId,
                    expectedConcurrencyVersion,
                    scope: null,
                    cancellationToken) != 1)
            {
                await RollbackAsync(transaction);
                return IntakeStoreWriteCode.NotFound;
            }

            await _context.Set<IntakeLinkEntity>()
                .Where(link =>
                    link.FirmId == firm.Value &&
                    link.CaseId == caseId &&
                    link.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        link => link.RevokedAtUtc,
                        (DateTimeOffset?)replacement.IssuedAtUtc),
                    cancellationToken);
            await _context.Set<IntakeClientSessionEntity>()
                .Where(session =>
                    session.FirmId == firm.Value &&
                    session.CaseId == caseId &&
                    session.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        session => session.RevokedAtUtc,
                        (DateTimeOffset?)replacement.IssuedAtUtc),
                    cancellationToken);

            _context.Add(new IntakeLinkEntity
            {
                FirmId = firm.Value,
                CaseId = caseId,
                LinkId = replacement.LinkId,
                CredentialDigest = replacement.CredentialDigest.Bytes.ToArray(),
                IssuedAtUtc = replacement.IssuedAtUtc,
                ExpiresAtUtc = replacement.ExpiresAtUtc,
            });
            foreach (var audit in await NextAuditsAsync(
                         firm.Value,
                         caseId,
                         auditEvents,
                         cancellationToken))
            {
                _context.Add(audit);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return IntakeStoreWriteCode.Success;
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction);
            return IntakeStoreWriteCode.Conflict;
        }
    }

    public async Task<IntakeStoreWriteCode> RevokeLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        DateTimeOffset revokedAtUtc,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        ArgumentNullException.ThrowIfNull(auditEvent);
        await using var transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await AdvanceCaseAsync(
                    firm.Value,
                    caseId,
                    expectedConcurrencyVersion,
                    scope: null,
                    cancellationToken) != 1)
            {
                await RollbackAsync(transaction);
                return IntakeStoreWriteCode.NotFound;
            }

            var affected = await _context.Set<IntakeLinkEntity>()
                .Where(link =>
                    link.FirmId == firm.Value &&
                    link.CaseId == caseId &&
                    link.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        link => link.RevokedAtUtc,
                        (DateTimeOffset?)revokedAtUtc),
                    cancellationToken);
            if (affected == 0)
            {
                await RollbackAsync(transaction);
                return IntakeStoreWriteCode.Conflict;
            }

            await _context.Set<IntakeClientSessionEntity>()
                .Where(session =>
                    session.FirmId == firm.Value &&
                    session.CaseId == caseId &&
                    session.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        session => session.RevokedAtUtc,
                        (DateTimeOffset?)revokedAtUtc),
                    cancellationToken);
            _context.Add(await NextAuditAsync(
                firm.Value,
                caseId,
                auditEvent,
                cancellationToken));

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return IntakeStoreWriteCode.Success;
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction);
            return IntakeStoreWriteCode.Conflict;
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
        ArgumentNullException.ThrowIfNull(answers);
        return AppendClientMutationAsync(
            scope,
            expectedConcurrencyVersion,
            renewedIdleExpiresAtUtc,
            auditEvents,
            () =>
            {
                _context.AddRange(answers.Select(answer =>
                    new IntakeAnswerVersionEntity
                    {
                        FirmId = scope.Firm.Value,
                        CaseId = scope.CaseId,
                        Category = answer.Category,
                        Version = answer.Version,
                        Answer = answer.Answer,
                        ActorKind = answer.ActorKind,
                        ActorReference = answer.ActorReference,
                        RecordedAtUtc = answer.RecordedAtUtc,
                    }));
            },
            duplicateOnFailure: false,
            cancellationToken);
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
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(auditEvent);
        return AppendClientMutationAsync(
            scope,
            expectedConcurrencyVersion,
            renewedIdleExpiresAtUtc,
            [auditEvent],
            () =>
            {
                _context.Add(new IntakeUploadAttemptEntity
                {
                    FirmId = scope.Firm.Value,
                    CaseId = scope.CaseId,
                    AttemptId = attempt.AttemptId,
                    ExpectedItemId = attempt.ExpectedItemId,
                    Outcome = attempt.Outcome,
                    AttemptedAtUtc = attempt.AttemptedAtUtc,
                });
                if (document is not null)
                {
                    _context.Add(new IntakeDocumentEntity
                    {
                        FirmId = scope.Firm.Value,
                        CaseId = scope.CaseId,
                        DocumentId = document.DocumentId,
                        ExpectedItemId = document.ExpectedItemId,
                        Category = document.Category,
                        Version = document.Version,
                        ContentKey = document.ContentKey,
                        MediaKind = document.MediaKind,
                        ByteCount = document.ByteCount,
                        Fingerprint = document.Fingerprint.ToArray(),
                        UploadedAtUtc = document.UploadedAtUtc,
                    });
                }
            },
            duplicateOnFailure: document is not null,
            cancellationToken,
            // Spec 016 T04: an accepted upload's staged source object is bound to
            // the committed document in the same transaction. A registration that
            // cleanup already took refuses the whole commit.
            bindAfterSave: document is null
                ? null
                : token => SqlSourceRegistryStore.BindAsync(
                    _context,
                    scope.Firm.Value,
                    scope.CaseId,
                    document.DocumentId,
                    document.ContentKey,
                    document.UploadedAtUtc,
                    token));
    }

    public Task<IntakeStoreWriteCode> AppendSubmissionAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IntakeSubmissionRecord submission,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return AppendClientMutationAsync(
            scope,
            expectedConcurrencyVersion,
            renewedIdleExpiresAtUtc,
            auditEvents,
            () =>
            {
                _context.Add(new IntakeSubmissionEntity
                {
                    FirmId = scope.Firm.Value,
                    CaseId = scope.CaseId,
                    Version = submission.Version,
                    ActorKind = submission.ActorKind,
                    ActorReference = submission.ActorReference,
                    SubmittedAtUtc = submission.SubmittedAtUtc,
                });
                _context.AddRange(submission.Answers.Select(reference =>
                    new IntakeSubmissionAnswerEntity
                    {
                        FirmId = scope.Firm.Value,
                        CaseId = scope.CaseId,
                        SubmissionVersion = submission.Version,
                        Category = reference.Category,
                        AnswerVersion = reference.AnswerVersion,
                    }));
                _context.AddRange(submission.Documents.Select(reference =>
                    new IntakeSubmissionDocumentEntity
                    {
                        FirmId = scope.Firm.Value,
                        CaseId = scope.CaseId,
                        SubmissionVersion = submission.Version,
                        DocumentId = reference.DocumentId,
                    }));
            },
            duplicateOnFailure: false,
            cancellationToken);
    }

    public async Task<IntakeStoreWriteCode> AssociateReviewAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        IntakeReviewAssociationRecord association,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(auditEvent);
        await using var transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await AdvanceCaseAsync(
                    firm.Value,
                    caseId,
                    expectedConcurrencyVersion,
                    scope: null,
                    cancellationToken) != 1)
            {
                await RollbackAsync(transaction);
                return IntakeStoreWriteCode.NotFound;
            }

            _context.Add(new IntakeReviewAssociationEntity
            {
                FirmId = firm.Value,
                CaseId = caseId,
                RecordId = association.RecordId,
                AssociatedAtUtc = association.AssociatedAtUtc,
            });
            _context.Add(await NextAuditAsync(
                firm.Value,
                caseId,
                auditEvent,
                cancellationToken));
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return IntakeStoreWriteCode.Success;
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction);
            return IntakeStoreWriteCode.Conflict;
        }
    }

    public override string ToString() => nameof(SqlIntakeStore);

    private async Task<IntakeStoreWriteCode> AppendClientMutationAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IReadOnlyList<IntakeAuditEventRecord> audits,
        Action addRows,
        bool duplicateOnFailure,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<bool>>? bindAfterSave = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(audits);
        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        try
        {
            if (!await ClientScopeIsLiveAsync(
                    scope,
                    renewedIdleExpiresAtUtc,
                    cancellationToken) ||
                await AdvanceCaseAsync(
                    scope.Firm.Value,
                    scope.CaseId,
                    expectedConcurrencyVersion,
                    scope,
                    cancellationToken) != 1 ||
                await RenewSessionAsync(
                    scope,
                    renewedIdleExpiresAtUtc,
                    cancellationToken) != 1)
            {
                await RollbackAsync(transaction);
                return IntakeStoreWriteCode.NotFound;
            }

            addRows();
            foreach (var audit in await NextAuditsAsync(
                         scope.Firm.Value,
                         scope.CaseId,
                         audits,
                         cancellationToken))
            {
                _context.Add(audit);
            }

            await _context.SaveChangesAsync(cancellationToken);
            if (bindAfterSave is not null &&
                !await bindAfterSave(cancellationToken))
            {
                await RollbackAsync(transaction);
                _context.ChangeTracker.Clear();
                return IntakeStoreWriteCode.Conflict;
            }

            await transaction.CommitAsync(cancellationToken);
            return IntakeStoreWriteCode.Success;
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction);
            return duplicateOnFailure
                ? IntakeStoreWriteCode.Duplicate
                : IntakeStoreWriteCode.Conflict;
        }
    }

    private async Task<StoredIntakeCase?> LoadAsync(
        Guid firmId,
        Guid caseId,
        CancellationToken cancellationToken)
    {
        var cases = _context.Set<IntakeCaseEntity>().AsNoTracking();

        var intakeCase = await cases.SingleOrDefaultAsync(
            candidate =>
                candidate.FirmId == firmId &&
                candidate.CaseId == caseId,
            cancellationToken);
        if (intakeCase is null)
        {
            return null;
        }

        var expectedItems = await _context.Set<IntakeExpectedItemEntity>()
            .AsNoTracking()
            .Where(entity => entity.FirmId == firmId && entity.CaseId == caseId)
            .OrderBy(entity => entity.Category)
            .ThenBy(entity => entity.Ordinal)
            .Select(entity => new IntakeExpectedItemRecord(
                entity.ExpectedItemId,
                entity.Category,
                entity.Ordinal))
            .ToListAsync(cancellationToken);
        var links = (await _context.Set<IntakeLinkEntity>()
            .AsNoTracking()
            .Where(entity => entity.FirmId == firmId && entity.CaseId == caseId)
            .ToListAsync(cancellationToken))
            .OrderByDescending(entity => entity.IssuedAtUtc)
            .ThenByDescending(entity => entity.LinkId)
            .ToList();
        var currentLink = links.FirstOrDefault();
        if (currentLink is null)
        {
            return null;
        }

        var answers = await _context.Set<IntakeAnswerVersionEntity>()
            .AsNoTracking()
            .Where(entity => entity.FirmId == firmId && entity.CaseId == caseId)
            .OrderBy(entity => entity.Category)
            .ThenBy(entity => entity.Version)
            .Select(entity => new IntakeAnswerVersionRecord(
                entity.Category,
                entity.Version,
                entity.Answer,
                entity.ActorKind,
                entity.ActorReference,
                entity.RecordedAtUtc))
            .ToListAsync(cancellationToken);
        var documents = await _context.Set<IntakeDocumentEntity>()
            .AsNoTracking()
            .Where(entity => entity.FirmId == firmId && entity.CaseId == caseId)
            .OrderBy(entity => entity.Category)
            .ThenBy(entity => entity.ExpectedItemId)
            .ThenBy(entity => entity.Version)
            .ToListAsync(cancellationToken);
        var attempts = (await _context.Set<IntakeUploadAttemptEntity>()
            .AsNoTracking()
            .Where(entity => entity.FirmId == firmId && entity.CaseId == caseId)
            .ToListAsync(cancellationToken))
            .OrderBy(entity => entity.AttemptedAtUtc)
            .ThenBy(entity => entity.AttemptId)
            .Select(entity => new IntakeUploadAttemptRecord(
                entity.AttemptId,
                entity.ExpectedItemId,
                entity.Outcome,
                entity.AttemptedAtUtc))
            .ToList();
        var submissions = await LoadSubmissionsAsync(
            firmId,
            caseId,
            cancellationToken);
        var association = await _context.Set<IntakeReviewAssociationEntity>()
            .AsNoTracking()
            .Where(entity => entity.FirmId == firmId && entity.CaseId == caseId)
            .Select(entity => new IntakeReviewAssociationRecord(
                entity.RecordId,
                entity.AssociatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        return new StoredIntakeCase(
            TrustedFirmId.FromGuid(firmId),
            caseId,
            intakeCase.PrimaryParticipantId,
            intakeCase.PrimaryTaxpayerDisplayName,
            intakeCase.TaxYear,
            intakeCase.CaseStatus,
            intakeCase.ConcurrencyVersion,
            expectedItems,
            new IntakeLinkRecord(
                currentLink.LinkId,
                new IntakeTokenDigest(currentLink.CredentialDigest),
                currentLink.IssuedAtUtc,
                currentLink.ExpiresAtUtc,
                currentLink.SpentAtUtc,
                currentLink.RevokedAtUtc),
            answers,
            documents.Select(entity => new IntakeDocumentRecord(
                entity.DocumentId,
                entity.ExpectedItemId,
                entity.Category,
                entity.Version,
                entity.ContentKey,
                entity.MediaKind,
                entity.ByteCount,
                entity.Fingerprint,
                entity.UploadedAtUtc)).ToArray(),
            attempts,
            submissions,
            association);
    }

    private async Task<IReadOnlyList<IntakeSubmissionRecord>> LoadSubmissionsAsync(
        Guid firmId,
        Guid caseId,
        CancellationToken cancellationToken)
    {
        var submissions = await _context.Set<IntakeSubmissionEntity>()
            .AsNoTracking()
            .Where(entity => entity.FirmId == firmId && entity.CaseId == caseId)
            .OrderBy(entity => entity.Version)
            .ToListAsync(cancellationToken);
        var answers = await _context.Set<IntakeSubmissionAnswerEntity>()
            .AsNoTracking()
            .Where(entity => entity.FirmId == firmId && entity.CaseId == caseId)
            .ToListAsync(cancellationToken);
        var documents = await _context.Set<IntakeSubmissionDocumentEntity>()
            .AsNoTracking()
            .Where(entity => entity.FirmId == firmId && entity.CaseId == caseId)
            .ToListAsync(cancellationToken);

        return Array.AsReadOnly(submissions.Select(submission =>
            new IntakeSubmissionRecord(
                submission.Version,
                answers
                    .Where(reference =>
                        reference.SubmissionVersion == submission.Version)
                    .OrderBy(reference => reference.Category)
                    .Select(reference => new IntakeSubmissionAnswerReference(
                        reference.Category,
                        reference.AnswerVersion))
                    .ToArray(),
                documents
                    .Where(reference =>
                        reference.SubmissionVersion == submission.Version)
                    .OrderBy(reference => reference.DocumentId)
                    .Select(reference => new IntakeSubmissionDocumentReference(
                        reference.DocumentId))
                    .ToArray(),
                submission.ActorKind,
                submission.ActorReference,
                submission.SubmittedAtUtc)).ToArray());
    }

    private async Task<int> AdvanceCaseAsync(
        Guid firmId,
        Guid caseId,
        long expectedVersion,
        IntakeClientScope? scope,
        CancellationToken cancellationToken)
    {
        var query = _context.Set<IntakeCaseEntity>().Where(candidate =>
            candidate.FirmId == firmId &&
            candidate.CaseId == caseId &&
            candidate.ConcurrencyVersion == expectedVersion &&
            candidate.CaseStatus != TaxCases.Models.CaseStatus.Cancelled);
        if (scope is not null)
        {
            var digest = scope.SessionDigest.Bytes.ToArray();
            query = query.Where(candidate =>
                _context.Set<IntakeClientSessionEntity>().Any(session =>
                    session.FirmId == candidate.FirmId &&
                    session.CaseId == candidate.CaseId &&
                    session.SessionId == scope.SessionId &&
                    session.SessionDigest == digest &&
                    session.RevokedAtUtc == null));
        }

        return await query.ExecuteUpdateAsync(
            setters => setters.SetProperty(
                candidate => candidate.ConcurrencyVersion,
                candidate => candidate.ConcurrencyVersion + 1),
            cancellationToken);
    }

    private Task<int> RenewSessionAsync(
        IntakeClientScope scope,
        DateTimeOffset renewedIdleExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var digest = scope.SessionDigest.Bytes.ToArray();
        return _context.Set<IntakeClientSessionEntity>()
            .Where(session =>
                session.FirmId == scope.Firm.Value &&
                session.CaseId == scope.CaseId &&
                session.SessionId == scope.SessionId &&
                session.SessionDigest == digest &&
                session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    session => session.IdleExpiresAtUtc,
                    renewedIdleExpiresAtUtc),
                cancellationToken);
    }

    private async Task<bool> ClientScopeIsLiveAsync(
        IntakeClientScope scope,
        DateTimeOffset? renewedIdleExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var digest = scope.SessionDigest.Bytes.ToArray();
        var session = await _context.Set<IntakeClientSessionEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.FirmId == scope.Firm.Value &&
                    candidate.CaseId == scope.CaseId &&
                    candidate.SessionId == scope.SessionId &&
                    candidate.SessionDigest == digest &&
                    candidate.RevokedAtUtc == null &&
                    _context.Set<IntakeCaseEntity>().Any(intakeCase =>
                        intakeCase.FirmId == candidate.FirmId &&
                        intakeCase.CaseId == candidate.CaseId &&
                        intakeCase.CaseStatus != TaxCases.Models.CaseStatus.Cancelled),
                cancellationToken);
        if (session is null ||
            scope.EvaluationAtUtc >= session.IdleExpiresAtUtc ||
            scope.EvaluationAtUtc >= session.AbsoluteExpiresAtUtc ||
            renewedIdleExpiresAtUtc > session.AbsoluteExpiresAtUtc)
        {
            return false;
        }

        var linkExpiry = await _context.Set<IntakeLinkEntity>()
            .AsNoTracking()
            .Where(candidate =>
                candidate.FirmId == scope.Firm.Value &&
                candidate.CaseId == scope.CaseId &&
                candidate.LinkId == session.LinkId &&
                candidate.RevokedAtUtc == null)
            .Select(candidate => (DateTimeOffset?)candidate.ExpiresAtUtc)
            .SingleOrDefaultAsync(cancellationToken);

        return linkExpiry is not null &&
            scope.EvaluationAtUtc < linkExpiry.Value;
    }

    private async Task<IntakeAuditEventEntity> NextAuditAsync(
        Guid firmId,
        Guid caseId,
        IntakeAuditEventRecord audit,
        CancellationToken cancellationToken)
    {
        var maximum = await _context.Set<IntakeAuditEventEntity>()
            .Where(entity => entity.FirmId == firmId && entity.CaseId == caseId)
            .Select(entity => (long?)entity.Sequence)
            .MaxAsync(cancellationToken) ?? 0;
        return AuditEntity(firmId, caseId, audit, checked(maximum + 1));
    }

    private async Task<IReadOnlyList<IntakeAuditEventEntity>> NextAuditsAsync(
        Guid firmId,
        Guid caseId,
        IReadOnlyList<IntakeAuditEventRecord> audits,
        CancellationToken cancellationToken)
    {
        var maximum = await _context.Set<IntakeAuditEventEntity>()
            .Where(entity => entity.FirmId == firmId && entity.CaseId == caseId)
            .Select(entity => (long?)entity.Sequence)
            .MaxAsync(cancellationToken) ?? 0;
        return Array.AsReadOnly(audits.Select((audit, index) =>
            AuditEntity(firmId, caseId, audit, checked(maximum + index + 1)))
            .ToArray());
    }

    private static IntakeLinkEntity LinkEntity(
        StoredIntakeCase storedCase,
        IntakeLinkRecord link) =>
        new()
        {
            FirmId = storedCase.Firm.Value,
            CaseId = storedCase.CaseId,
            LinkId = link.LinkId,
            CredentialDigest = link.CredentialDigest.Bytes.ToArray(),
            IssuedAtUtc = link.IssuedAtUtc,
            ExpiresAtUtc = link.ExpiresAtUtc,
            SpentAtUtc = link.SpentAtUtc,
            RevokedAtUtc = link.RevokedAtUtc,
        };

    private static IntakeAuditEventEntity AuditEntity(
        Guid firmId,
        Guid caseId,
        IntakeAuditEventRecord audit,
        long sequence) =>
        new()
        {
            FirmId = firmId,
            CaseId = caseId,
            EventId = audit.EventId,
            Sequence = sequence,
            Kind = audit.Kind,
            ActorKind = audit.ActorKind,
            ActorReference = audit.ActorReference,
            ExpectedItemId = audit.ExpectedItemId,
            OccurredAtUtc = audit.OccurredAtUtc,
        };

    private static bool ValidNewCase(NewIntakeCase intakeCase) =>
        intakeCase.Case.Firm is not null &&
        intakeCase.Case.CaseId != Guid.Empty &&
        intakeCase.Case.PrimaryParticipantId != Guid.Empty &&
        intakeCase.Case.ExpectedItems.Count > 0 &&
        intakeCase.Case.CurrentLink.LinkId != Guid.Empty &&
        intakeCase.Case.CurrentLink.CredentialDigest.Bytes.Count == 32 &&
        intakeCase.AuditEvents.Count > 0;

    private async Task RollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        _context.ChangeTracker.Clear();
    }
}
