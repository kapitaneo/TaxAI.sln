using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace Persistence.Repositories;

/// <summary>
/// The durable, shared implementation of the server-side session store.
/// <para>
/// Every operation reads or writes the database directly. No process-local
/// cache is authoritative, so a session created or revoked through one
/// application instance is immediately observed by every other instance.
/// </para>
/// <para>
/// Expiry is evaluated by callers on every request. Removing expired rows is a
/// separate, explicit privileged command; this type starts no timer, hosted
/// service, or scheduler.
/// </para>
/// </summary>
internal sealed class SqlAuthenticationSessionStore : IAuthenticationSessionStore
{
    private readonly TaxAiDbContext _context;

    public SqlAuthenticationSessionStore(TaxAiDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<AuthenticationSession> CreateAsync(
        Guid userId,
        Guid? activeFirmId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var boundaries = SessionLifetimePolicy.CreateBoundaries(createdAtUtc);
        if (boundaries.IsFailure)
        {
            throw new ArgumentException(
                boundaries.Errors[0].Message,
                nameof(createdAtUtc));
        }

        var session = Materialize(
            Guid.NewGuid(),
            userId,
            activeFirmId,
            createdAtUtc,
            createdAtUtc,
            boundaries.Value!.IdleExpiresAtUtc,
            boundaries.Value.AbsoluteExpiresAtUtc,
            revokedAtUtc: null);

        _context.Set<AuthenticationSession>().Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        return session;
    }

    public Task<AuthenticationSession?> FindAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        _context.Set<AuthenticationSession>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.SessionId == sessionId &&
                    candidate.RevokedAtUtc == null,
                cancellationToken);

    public async Task<AuthenticationSession?> TouchAsync(
        AuthenticationSession session,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var current = await _context.Set<AuthenticationSession>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.SessionId == session.SessionId,
                cancellationToken);
        if (current is null)
        {
            return null;
        }

        var boundaries = SessionLifetimePolicy.Touch(current, atUtc);
        if (boundaries.IsFailure)
        {
            return null;
        }

        var lastSeenAtUtc = atUtc > current.LastSeenAtUtc
            ? atUtc
            : current.LastSeenAtUtc;
        var affected = await _context.Set<AuthenticationSession>()
            .Where(candidate =>
                candidate.SessionId == current.SessionId &&
                candidate.RevokedAtUtc == null &&
                candidate.LastSeenAtUtc == current.LastSeenAtUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        candidate => candidate.LastSeenAtUtc,
                        lastSeenAtUtc)
                    .SetProperty(
                        candidate => candidate.IdleExpiresAtUtc,
                        boundaries.Value!.IdleExpiresAtUtc),
                cancellationToken);

        if (affected == 0)
        {
            // A concurrent touch may have won the optimistic update, or another
            // instance may have revoked/deleted the row. A fresh read
            // distinguishes those states without creating anything.
            return await FindAsync(session.SessionId, cancellationToken);
        }

        return await FindAsync(session.SessionId, cancellationToken);
    }

    public async Task RevokeAsync(
        AuthenticationSession session,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureUtc(atUtc);

        await _context.Set<AuthenticationSession>()
            .Where(candidate =>
                candidate.SessionId == session.SessionId &&
                candidate.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    candidate => candidate.RevokedAtUtc,
                    (DateTimeOffset?)atUtc),
                cancellationToken);
    }

    /// <summary>
    /// Explicit privileged maintenance command. Deletes only rows whose idle or
    /// absolute boundary has been reached; no background mechanism invokes it.
    /// </summary>
    internal async Task<int> RemoveExpiredAsync(
        DateTimeOffset evaluationInstantUtc,
        CancellationToken cancellationToken)
    {
        EnsureUtc(evaluationInstantUtc);

        var sessions = await _context.Set<AuthenticationSession>()
            .AsNoTracking()
            .Select(session => new
            {
                session.SessionId,
                session.IdleExpiresAtUtc,
                session.AbsoluteExpiresAtUtc,
            })
            .ToListAsync(cancellationToken);
        var expiredIds = sessions
            .Where(session =>
                evaluationInstantUtc >= session.IdleExpiresAtUtc ||
                evaluationInstantUtc >= session.AbsoluteExpiresAtUtc)
            .Select(session => session.SessionId)
            .ToArray();

        if (expiredIds.Length == 0)
        {
            return 0;
        }

        return await _context.Set<AuthenticationSession>()
            .Where(session => expiredIds.Contains(session.SessionId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Fixed type identity only; never session or database state.</summary>
    public override string ToString() => nameof(SqlAuthenticationSessionStore);

    private static void EnsureUtc(DateTimeOffset instant)
    {
        var error = UtcInstantGuard.Validate(
            instant,
            TenancyFields.EvaluationInstant);
        if (error is not null)
        {
            throw new ArgumentException(error.Message, nameof(instant));
        }
    }

    private static AuthenticationSession Materialize(
        Guid sessionId,
        Guid userId,
        Guid? activeFirmId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset lastSeenAtUtc,
        DateTimeOffset idleExpiresAtUtc,
        DateTimeOffset absoluteExpiresAtUtc,
        DateTimeOffset? revokedAtUtc) =>
        (AuthenticationSession)Activator.CreateInstance(
            typeof(AuthenticationSession),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                sessionId,
                userId,
                activeFirmId,
                createdAtUtc,
                lastSeenAtUtc,
                idleExpiresAtUtc,
                absoluteExpiresAtUtc,
                revokedAtUtc,
            ],
            culture: null)!;
}
