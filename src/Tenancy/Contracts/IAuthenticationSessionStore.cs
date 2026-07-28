using Tenancy.Models;

namespace Tenancy.Contracts;

/// <summary>
/// The durable, shared server-side session store.
/// <para>
/// It holds no process-local authoritative state, so a session created by one
/// application instance is immediately visible to another and a revocation
/// performed by one is immediately effective on the rest. An in-memory store
/// would make "signed out" a fact known to one machine.
/// </para>
/// <para>
/// <see cref="FindAsync"/> is the single identifier-keyed lookup in this module
/// that takes no trusted firm — it is the operation that establishes one, and it
/// returns the session's active firm rather than accepting it. Touch and revoke
/// take an already-resolved session precisely so that no second firm-omitting
/// lookup exists. See <see cref="FirmScopeExemptions"/>.
/// </para>
/// </summary>
public interface IAuthenticationSessionStore
{
    /// <summary>Creates exactly one session row for a completed sign-in.</summary>
    Task<AuthenticationSession> CreateAsync(
        Guid userId,
        Guid? activeFirmId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a session from the opaque key the cookie carried. An unknown,
    /// deleted, or revoked key yields null — it never creates, resurrects, or
    /// re-issues a session, so a copied pre-logout cookie can do neither.
    /// </summary>
    Task<AuthenticationSession?> FindAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records activity: advances the idle boundary and never the absolute one,
    /// which is fixed at creation.
    /// </summary>
    Task<AuthenticationSession?> TouchAsync(
        AuthenticationSession session,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// The security-bearing act of signing out. Clearing the cookie is a
    /// convenience and is never relied on alone.
    /// </summary>
    Task RevokeAsync(
        AuthenticationSession session,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}
