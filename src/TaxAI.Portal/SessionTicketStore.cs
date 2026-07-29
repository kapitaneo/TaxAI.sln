using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace TaxAI.Portal;

/// <summary>
/// The seam that turns the browser cookie into a lookup rather than a payload.
/// <para>
/// <b>No serialized ticket is ever stored.</b> The conventional
/// <see cref="ITicketStore"/> keeps the whole authentication ticket and hands it
/// back on retrieve; this one keeps a row of structural columns and rebuilds the
/// principal from them. That single decision is what makes "the cookie carries no
/// claims payload, no firm, no user, and no token" provable by reading the schema
/// instead of by trusting a serializer — and it is why a firm claim and the
/// session record cannot drift apart: the claim is re-derived from the record on
/// every request, so there is no second copy to go stale.
/// </para>
/// <para>
/// Everything here fails <b>closed</b>. An unknown key, a revoked session, a
/// session past either boundary, and an unavailable store all produce the same
/// answer — no principal — and none of them creates, resurrects, or re-issues a
/// row. A copied pre-logout cookie is therefore worth nothing the moment the row
/// is revoked, on every application instance at once.
/// </para>
/// <para>
/// It handles opaque GUIDs and instants only: no name, no email, no taxpayer
/// value, no monetary value, and no identifier that is echoed back to a caller.
/// </para>
/// </summary>
internal sealed class SessionTicketStore : ITicketStore
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// A scope factory rather than the store itself: the cookie options hold one
    /// instance for the process, and capturing a scoped store in it would outlive
    /// the request that created it.
    /// </summary>
    /// <remarks>
    /// Public because the container constructs it, on a type that is internal —
    /// so nothing outside this assembly can name it, and the portal's only
    /// exported type is still <c>Program</c>.
    /// </remarks>
    public SessionTicketStore(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Creates the one session row for a completed sign-in and returns its
    /// identifier as the cookie's key.
    /// <para>
    /// The creation instant is the one the cookie handler already stamped on the
    /// ticket, not a fresh reading: the session and the cookie it belongs to must
    /// agree about when they began.
    /// </para>
    /// </summary>
    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, CancellationToken.None);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var userId = RequiredIdentifier(ticket.Principal, PortalIdentity.UserClaimType);
        var activeFirmId = OptionalIdentifier(ticket.Principal, PortalIdentity.FirmClaimType);
        var createdAtUtc = AsUtc(ticket.Properties?.IssuedUtc) ?? CurrentInstant();

        using var scope = _scopeFactory.CreateScope();

        var session = await Store(scope)
            .CreateAsync(userId, activeFirmId, createdAtUtc, cancellationToken);

        return PortalIdentity.Canonical(session.SessionId);
    }

    /// <summary>
    /// Resolves the row the cookie's key names, refuses it if the policy says so,
    /// and rebuilds the principal from the record.
    /// </summary>
    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        if (!TryParseKey(key, out var sessionId))
        {
            return null;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var session = await Store(scope).FindAsync(sessionId, cancellationToken);

            if (session is null)
            {
                return null;
            }

            // The policy decides, not the cookie's own expiry: a cookie's expiry
            // is a value the client holds, and a client that keeps presenting an
            // expired one must still be refused.
            return SessionLifetimePolicy.Evaluate(session, CurrentInstant()).IsUsable
                ? BuildTicket(session)
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // An unavailable store must not degrade into an authenticated
            // request. The exception itself is deliberately not surfaced: it
            // would carry a connection string or a server name into whatever
            // reads it, and the caller learns only that they are not signed in.
            return null;
        }
    }

    /// <summary>
    /// Records activity. It advances the idle boundary only — the absolute one
    /// is fixed at creation, because a sliding maximum is not a maximum.
    /// </summary>
    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, CancellationToken.None);

    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (!TryParseKey(key, out var sessionId))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var store = Store(scope);

        var session = await store.FindAsync(sessionId, cancellationToken);

        // A renewal of something that no longer exists is a creation, and a
        // creation here would resurrect a session that was revoked a moment ago.
        if (session is null)
        {
            return;
        }

        await store.TouchAsync(
            session,
            AsUtc(ticket.Properties?.IssuedUtc) ?? CurrentInstant(),
            cancellationToken);
    }

    /// <summary>
    /// Sign-out. Revoking the row is the security-bearing act; clearing the
    /// cookie is the convenience the handler performs afterwards, and is never
    /// relied on alone.
    /// </summary>
    public Task RemoveAsync(string key) =>
        RemoveAsync(key, CancellationToken.None);

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (!TryParseKey(key, out var sessionId))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var store = Store(scope);

        var session = await store.FindAsync(sessionId, cancellationToken);

        if (session is null)
        {
            return;
        }

        await store.RevokeAsync(session, CurrentInstant(), cancellationToken);
    }

    /// <summary>
    /// The principal, rebuilt from the record's own columns. Nothing survives
    /// from the cookie except the key that found the row.
    /// </summary>
    private static AuthenticationTicket BuildTicket(AuthenticationSession session)
    {
        var identity = new ClaimsIdentity(PortalIdentity.CookieScheme);

        identity.AddClaim(
            new Claim(PortalIdentity.UserClaimType, PortalIdentity.Canonical(session.UserId)));

        // Absent when the user holds zero or several active memberships. The
        // absence is the answer, not a gap to fill in.
        if (session.ActiveFirmId.HasValue)
        {
            identity.AddClaim(
                new Claim(PortalIdentity.FirmClaimType, PortalIdentity.Canonical(session.ActiveFirmId.Value)));
        }

        var properties = new AuthenticationProperties
        {
            IssuedUtc = session.LastSeenAtUtc,
            ExpiresUtc = session.IdleExpiresAtUtc,
            AllowRefresh = true,
            IsPersistent = false,
        };

        return new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            properties,
            PortalIdentity.CookieScheme);
    }

    private static IAuthenticationSessionStore Store(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();

    /// <summary>
    /// The host's only clock reading, in one place.
    /// <para>
    /// It has to live here: the session contract takes every instant as an
    /// explicit parameter precisely so that no clock is read inside the domain,
    /// which leaves the adapter to supply one. Zero offset by construction —
    /// the guard on the other side of the contract rejects anything else rather
    /// than normalizing it.
    /// </para>
    /// </summary>
    private static DateTimeOffset CurrentInstant() => DateTimeOffset.UtcNow;

    /// <summary>
    /// Zero-offset form of a supplied instant. The same point in time, expressed
    /// the one way the contract accepts.
    /// </summary>
    private static DateTimeOffset? AsUtc(DateTimeOffset? instant) => instant?.ToUniversalTime();

    private static bool TryParseKey(string? key, out Guid sessionId) =>
        Guid.TryParseExact(key, "D", out sessionId) && sessionId != Guid.Empty;

    private static Guid RequiredIdentifier(ClaimsPrincipal? principal, string claimType)
    {
        var identifier = OptionalIdentifier(principal, claimType);

        // Reached only if something signed a principal in without going through
        // the one resolution path. There is no safe default for "which user",
        // so this refuses rather than inventing one.
        return identifier
            ?? throw new InvalidOperationException(PortalIdentity.UnresolvedPrincipalMessage);
    }

    private static Guid? OptionalIdentifier(ClaimsPrincipal? principal, string claimType)
    {
        var value = principal?.FindFirst(claimType)?.Value;

        // Exact canonical form only. A claim that is present but unparseable is
        // treated as absent, so a malformed firm claim can never be read as a
        // firm.
        return Guid.TryParseExact(value, "D", out var identifier) && identifier != Guid.Empty
            ? identifier
            : null;
    }

    /// <summary>Fixed type identity only; never a key, a user, or a firm.</summary>
    public override string ToString() => nameof(SessionTicketStore);
}
