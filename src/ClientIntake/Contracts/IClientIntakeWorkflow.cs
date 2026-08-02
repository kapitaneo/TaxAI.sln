using ClientIntake.Models;

namespace ClientIntake.Contracts;

/// <summary>
/// Everything an unauthenticated client may do, holding only a credential.
/// <para>
/// This is the module's one narrow bootstrap exception, and it is narrow on
/// purpose. No member takes a firm, case, participant, or document identifier:
/// scope is <b>derived</b> from a transiently digested credential and returned by
/// the store, never accepted from the caller. A client therefore has nothing to
/// enumerate and no identifier to tamper with.
/// </para>
/// <para>
/// Link inspection is separated from redemption because a GET must stay
/// read-only — a link prefetcher must not be able to spend an invitation. The
/// atomic spend happens in <see cref="RedeemLinkAsync"/>, behind an antiforgery
/// -protected POST carrying a proof derived from the digest rather than the raw
/// credential.
/// </para>
/// <para>
/// Every failure — malformed, unknown, expired, revoked, spent, replayed,
/// foreign, or inconsistent — is the same
/// <see cref="IntakeResultCode.NotFound"/>.
/// </para>
/// </summary>
public interface IClientIntakeWorkflow
{
    /// <summary>
    /// Validates a bootstrap credential and returns the tax year plus a
    /// redemption proof. Read-only: it spends nothing and creates no session.
    /// </summary>
    Task<IntakeResult<IntakeLinkInspection>> InspectLinkAsync(
        IntakeSecret credential,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically marks one still-active, unspent, unexpired link as spent and
    /// creates the one session derived from it. A losing replay returns the same
    /// <see cref="IntakeResultCode.NotFound"/> as an unknown link.
    /// </summary>
    Task<IntakeResult<RedeemedIntakeSession>> RedeemLinkAsync(
        IntakeRedemptionProof proof,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Projects the client view under a live session. Read-only, and
    /// deliberately does not renew the idle boundary — a GET must not keep a
    /// session alive.
    /// </summary>
    Task<IntakeResult<ClientIntakeView>> GetAsync(
        IntakeSecret sessionCredential,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends the next answer version for each changed category. Null, duplicate
    /// category, unrequested category, <c>Other</c>, and undefined values are
    /// refused; nothing is overwritten.
    /// </summary>
    Task<IntakeResult<ClientIntakeView>> SaveAnswersAsync(
        IntakeSecret sessionCredential,
        SaveIntakeAnswersCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Streams one already-validated upload to the content store and appends its
    /// immutable metadata.
    /// <para>
    /// The content arrives as a <see cref="Stream"/> and is never buffered into a
    /// byte array, a form collection, or a temporary file inside the web root.
    /// The category and ordinal on the command are untrusted presentation values;
    /// the server resolves the expected item within the session-derived case.
    /// </para>
    /// </summary>
    Task<IntakeResult<ClientIntakeView>> UploadAsync(
        IntakeSecret sessionCredential,
        IntakeUploadCommand command,
        Stream content,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends one immutable submission from the server-resolved current answer
    /// and document versions, then derives readiness by replaying exactly those
    /// references through the existing <c>TaxCase</c> behaviour.
    /// </summary>
    Task<IntakeResult<ClientIntakeView>> SubmitAsync(
        IntakeSecret sessionCredential,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
