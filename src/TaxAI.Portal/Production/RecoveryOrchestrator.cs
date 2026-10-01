using System.Globalization;
using ClientIntake.Contracts;
using ClientIntake.Models;
using TaxAI.AzureInfrastructure.Configuration;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.Portal.Production;

/// <summary>
/// Recovery orchestration for a production host (spec 016 T06, FR-10, FR-11;
/// AC-102, AC-110, AC-113 to AC-116, AC-165).
/// <para>
/// Every production host runs it at startup, after the admission gate has
/// verified the complete secure configuration, identity, keys and evidence, and
/// before the host serves any request. Until it releases the host, the host
/// does not start: a quarantined environment has no ingress at all.
/// </para>
/// <para>
/// The authoritative recovery epoch is the <b>database incarnation</b>. A
/// point-in-time restore, a copy and a geo-secondary taking over each create a
/// new incarnation, so no restored database inherits a previous release — even
/// one an operator forgot to declare. A host whose incarnation the independent
/// ledger already releases starts immediately. Otherwise, in order:
/// </para>
/// <list type="number">
/// <item>the ledger must be complete, fresh and valid, its history must reach
/// back past the recovered data, that data must lie within the legitimate
/// backup window, and a current authorization attestation newer than the data
/// must exist (<see cref="RecoveryReleasePolicy"/>);</item>
/// <item>every acknowledged terminal destruction is replayed inside its own
/// firm: resurrected source registrations are re-fenced and their objects
/// deleted through the scoped purger, and resurrected review records are
/// re-deleted unless other retained history still references them;</item>
/// <item>registry tombstones and orphaned staged objects of every attested or
/// replayed firm are cleaned up again;</item>
/// <item>every pre-recovery authentication session, client session and unspent
/// intake link is revoked — expired ones are left expired — and every active
/// membership the attestation does not list is deactivated;</item>
/// <item>only then is the release appended and acknowledged in the Canada East
/// copy.</item>
/// </list>
/// <para>
/// Any failure leaves the environment quarantined and names only closed
/// reasons. The orchestrator never skips a step to meet a recovery objective:
/// there is no option, flag or partial release (AC-165). A brand-new
/// environment writes the ledger's genesis only when the ledger is empty and
/// the database holds no taxpayer data, session or activity.
/// </para>
/// </summary>
internal sealed class RecoveryOrchestrator
{
    internal const string QuarantineMessage =
        "Production startup is quarantined: recovery reconciliation for this database has not completed.";

    /// <summary>The bounded page of registry tombstones cleaned per request.</summary>
    internal const int TombstonePageSize = 500;

    /// <summary>[ASSUMED] The bound on tombstone pages per firm in one reconciliation.</summary>
    internal const int MaximumTombstonePages = 200;

    private readonly IServiceScopeFactory _scopes;
    private readonly IRecoveryControlStore _ledger;
    private readonly TimeProvider _clock;
    private readonly AzureInfrastructureOptions _options;

    public RecoveryOrchestrator(
        IServiceScopeFactory scopes,
        IRecoveryControlStore ledger,
        TimeProvider clock,
        AzureInfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);

        _scopes = scopes;
        _ledger = ledger;
        _clock = clock;
        _options = options;
    }

    /// <summary>
    /// Runs orchestration within the validated operation deadline and throws
    /// the fixed quarantine message unless the incarnation is released.
    /// </summary>
    internal static void RequireRelease(IServiceProvider services, AzureInfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var orchestrator = services.GetService<RecoveryOrchestrator>() ??
                           throw new InvalidOperationException(QuarantineMessage);

        using var cancellation = new CancellationTokenSource(OperationDeadline(options));
        RecoveryOrchestration result;
        try
        {
            result = orchestrator.EnsureReleasedAsync(cancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(QuarantineMessage);
        }

        if (!result.IsReleased)
        {
            throw new InvalidOperationException(QuarantineMessage);
        }
    }

    /// <summary>
    /// Decides whether this host's database incarnation is released, performing
    /// the complete reconciliation first when it is not. Never throws for a
    /// dependency failure: that is a quarantine reason.
    /// </summary>
    internal async Task<RecoveryOrchestration> EnsureReleasedAsync(CancellationToken cancellationToken)
    {
        var startedAtUtc = Now();

        await using var scope = _scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var reconciler = provider.GetRequiredService<IRecoveryReconciler>();

        var incarnation = await reconciler.ReadIncarnationAsync(cancellationToken).ConfigureAwait(false);
        if (incarnation is null)
        {
            return RecoveryOrchestration.Quarantined(RecoveryQuarantineReason.DatabaseIncarnationUnavailable);
        }

        if (await IsReleasedAsync(incarnation, cancellationToken).ConfigureAwait(false))
        {
            return RecoveryOrchestration.AlreadyReleased;
        }

        var ledger = await _ledger.ReadAsync(cancellationToken).ConfigureAwait(false);
        var extent = await reconciler.ReadDataExtentAsync(cancellationToken).ConfigureAwait(false);

        if (RecoveryReleasePolicy.MayBootstrap(ledger, extent))
        {
            return await AppendReleaseAsync(
                    RecoveryControlRecord.Genesis(incarnation, Now()),
                    incarnation,
                    RecoveryOrchestration.Bootstrapped,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!ProductionConfigurationValidator.TryReadRecoveryWindows(
                _options,
                out var pointInTimeRestoreDays,
                out var backupChainDays,
                out var controlLedgerRetentionDays))
        {
            return RecoveryOrchestration.Quarantined(RecoveryQuarantineReason.SecurityConfigurationInvalid);
        }

        var preconditions = RecoveryReleasePolicy.EvaluatePreconditions(new RecoveryReleaseFacts(
            incarnation,
            ledger,
            extent,
            controlLedgerRetentionDays,
            pointInTimeRestoreDays,
            backupChainDays,
            Now()));
        if (preconditions.Count > 0)
        {
            return RecoveryOrchestration.Quarantined(preconditions.ToArray());
        }

        var reasons = new List<RecoveryQuarantineReason>();
        var registry = provider.GetService<ISourceRegistryStore>();
        var purger = provider.GetService<ISourceObjectPurger>();
        var attestation = ledger.Attestation!;

        // 1. Replay every acknowledged terminal destruction before any taxpayer
        //    read can happen, each inside the firm its fact names.
        var replayedFirms = new HashSet<Guid>();
        foreach (var record in ledger.Records.OrderBy(record => record.RecordedAtUtc))
        {
            switch (record.Kind)
            {
                case RecoveryControlRecordKind.TerminalSource:
                    replayedFirms.Add(record.Firm!.Value);
                    if (!await ReplaySourceAsync(reconciler, purger, record, cancellationToken).ConfigureAwait(false))
                    {
                        reasons.Add(RecoveryQuarantineReason.TerminalReplayIncomplete);
                    }

                    break;
                case RecoveryControlRecordKind.TerminalStructured:
                    replayedFirms.Add(record.Firm!.Value);
                    var structured = await reconciler
                        .ReplayTerminalStructuredAsync(record.Firm!, record.ResourceId, cancellationToken)
                        .ConfigureAwait(false);
                    if (structured is RecoveryReplayOutcome.Incomplete or RecoveryReplayOutcome.Blocked)
                    {
                        reasons.Add(RecoveryQuarantineReason.TerminalReplayIncomplete);
                    }

                    break;
            }
        }

        // 2. Registry tombstones and orphaned staged objects, per trusted firm.
        var firms = attestation.Memberships
            .Select(membership => membership.Firm.Value)
            .Concat(replayedFirms)
            .Distinct()
            .OrderBy(firm => firm);
        foreach (var firm in firms)
        {
            if (!await CleanTombstonesAsync(
                    registry,
                    purger,
                    TrustedFirmId.FromGuid(firm),
                    startedAtUtc,
                    cancellationToken).ConfigureAwait(false))
            {
                reasons.Add(RecoveryQuarantineReason.RegistryReplayIncomplete);
            }
        }

        // 3. No revocation lost by the restore may be undone, and no restored
        //    membership outlives its current authorization.
        try
        {
            _ = await reconciler.InvalidatePreRecoveryAccessAsync(startedAtUtc, cancellationToken)
                .ConfigureAwait(false);
            _ = await reconciler.ApplyAuthorizationAttestationAsync(attestation, Now(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException ||
                                        !cancellationToken.IsCancellationRequested)
        {
            reasons.Add(RecoveryQuarantineReason.AccessInvalidationIncomplete);
        }

        if (reasons.Count > 0)
        {
            return RecoveryOrchestration.Quarantined(reasons.ToArray());
        }

        return await AppendReleaseAsync(
                RecoveryControlRecord.Release(incarnation, Now()),
                incarnation,
                RecoveryOrchestration.Reconciled,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public override string ToString() => nameof(RecoveryOrchestrator);

    /// <summary>The validated operation deadline bounds the whole orchestration.</summary>
    internal static TimeSpan OperationDeadline(AzureInfrastructureOptions options) =>
        TimeSpan.TryParseExact(options.Resilience.OperationDeadline, "c", CultureInfo.InvariantCulture, out var deadline) &&
        deadline > TimeSpan.Zero &&
        deadline <= ProductionConfigurationValidator.OperationDeadlineBound
            ? deadline
            : TimeSpan.Zero;

    private async Task<bool> IsReleasedAsync(DatabaseIncarnation incarnation, CancellationToken cancellationToken)
    {
        try
        {
            return await _ledger.HasReleaseAsync(incarnation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException ||
                                        !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<RecoveryOrchestration> AppendReleaseAsync(
        RecoveryControlRecord record,
        DatabaseIncarnation incarnation,
        RecoveryOrchestration success,
        CancellationToken cancellationToken)
    {
        var outcome = await _ledger.AppendAsync(record, cancellationToken).ConfigureAwait(false);
        if (outcome == RecoveryControlAppendOutcome.Acknowledged)
        {
            return success;
        }

        // A concurrent instance may have released the same incarnation first.
        return outcome == RecoveryControlAppendOutcome.Conflict &&
               await IsReleasedAsync(incarnation, cancellationToken).ConfigureAwait(false)
            ? RecoveryOrchestration.AlreadyReleased
            : RecoveryOrchestration.Quarantined(RecoveryQuarantineReason.ReleaseUnacknowledged);
    }

    private async Task<bool> ReplaySourceAsync(
        IRecoveryReconciler reconciler,
        ISourceObjectPurger? purger,
        RecoveryControlRecord record,
        CancellationToken cancellationToken)
    {
        var fenced = await reconciler
            .ReplayTerminalSourceAsync(record.Firm!, record.CaseId, record.ResourceId, Now(), cancellationToken)
            .ConfigureAwait(false);
        if (fenced is RecoveryReplayOutcome.Incomplete or RecoveryReplayOutcome.Blocked || purger is null)
        {
            return false;
        }

        // The object is deleted again even when the registry already shows it
        // terminal: storage is not restored with the database, and a lagging
        // replica taking over can resurrect it. Deletion is idempotent.
        var deleted = await purger
            .PurgeAsync(record.Firm!, record.CaseId, record.ResourceId, cancellationToken)
            .ConfigureAwait(false);
        return deleted != SourceObjectPurgeOutcome.Unavailable;
    }

    private async Task<bool> CleanTombstonesAsync(
        ISourceRegistryStore? registry,
        ISourceObjectPurger? purger,
        TrustedFirmId firm,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        if (registry is null || purger is null)
        {
            return false;
        }

        for (var page = 0; page < MaximumTombstonePages; page++)
        {
            var work = await registry
                .ListUncommittedAsync(firm, startedAtUtc, TombstonePageSize, cancellationToken)
                .ConfigureAwait(false);
            var progressed = false;
            foreach (var registration in work)
            {
                var outcome = await purger
                    .PurgeAsync(firm, registration.CaseId, registration.ResourceId, cancellationToken)
                    .ConfigureAwait(false);
                if (outcome == SourceObjectPurgeOutcome.Unavailable)
                {
                    return false;
                }

                var written = await registry
                    .RecordPurgeAsync(firm, registration.CaseId, registration.ResourceId, outcome, Now(), cancellationToken)
                    .ConfigureAwait(false);
                if (written != SourceRegistryWriteCode.Success)
                {
                    return false;
                }

                progressed |= registration.State == SourceRegistryState.Staged || outcome == SourceObjectPurgeOutcome.Complete;
            }

            // Pending tombstones stay listed until replica deletion is proven, so
            // a page that only re-observed them is the end of this firm's work.
            if (work.Count < TombstonePageSize || !progressed)
            {
                return true;
            }
        }

        return false;
    }

    private DateTimeOffset Now() => _clock.GetUtcNow().ToUniversalTime();
}

/// <summary>The closed result of one orchestration. Structural only.</summary>
internal sealed class RecoveryOrchestration
{
    private RecoveryOrchestration(bool isReleased, bool reconciled, IReadOnlyList<RecoveryQuarantineReason> reasons)
    {
        IsReleased = isReleased;
        WasReconciled = reconciled;
        Reasons = reasons;
    }

    internal static RecoveryOrchestration AlreadyReleased { get; } = new(true, false, []);

    internal static RecoveryOrchestration Bootstrapped { get; } = new(true, false, []);

    internal static RecoveryOrchestration Reconciled { get; } = new(true, true, []);

    internal static RecoveryOrchestration Quarantined(params RecoveryQuarantineReason[] reasons) =>
        new(false, false, reasons.Distinct().OrderBy(reason => (int)reason).ToArray());

    internal bool IsReleased { get; }

    internal bool WasReconciled { get; }

    internal IReadOnlyList<RecoveryQuarantineReason> Reasons { get; }

    public override string ToString() => nameof(RecoveryOrchestration);
}
