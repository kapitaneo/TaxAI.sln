using ClientIntake.Models;
using Tenancy.Models;

namespace ClientIntake.Contracts;

/// <summary>
/// Everything an authenticated accountant may do to an intake case.
/// <para>
/// Every member takes a <see cref="TrustedFirmId"/> as its first argument, and
/// none has an overload, optional argument, or default that omits it. The firm
/// comes from the request's authorization state, never from a route, query,
/// form, body, header, or cookie — that is why no command record in this module
/// has a firm member.
/// </para>
/// <para>
/// The actor reference and the current instant are supplied by the caller for
/// the same reason: the workflow reads no clock, no environment, no current
/// user, and no HTTP context, so its behaviour is a function of its arguments
/// and is testable without a host.
/// </para>
/// <para>
/// A missing case and a case belonging to another firm produce the same
/// <see cref="IntakeResultCode.NotFound"/>. Nothing distinguishes them, so a
/// caller cannot learn that a case exists by being refused differently.
/// </para>
/// </summary>
public interface IAccountantIntakeWorkflow
{
    /// <summary>
    /// Validates the display name, creates the case, participant, checklist, and
    /// first bootstrap link, and commits them with their audit events in one
    /// transaction. The raw credential is returned exactly once and is never
    /// recoverable afterwards.
    /// </summary>
    Task<IntakeResult<CreatedIntakeCase>> CreateAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        CreateIntakeCaseCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Projects one firm-scoped progress view. Read-only: it advances no session
    /// activity and changes no application state.
    /// </summary>
    Task<IntakeResult<AccountantIntakeView>> GetProgressAsync(
        TrustedFirmId firm,
        Guid caseId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every currently active link and every session derived from it,
    /// then issues exactly one replacement. History is retained; nothing is
    /// deleted.
    /// </summary>
    Task<IntakeResult<IssuedIntakeLink>> ReissueLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid accountantActorReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Performs the same invalidation as a reissue, without a replacement.
    /// </summary>
    Task<IntakeResult<AccountantIntakeView>> RevokeLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid accountantActorReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Associates one exact existing review record with this case.
    /// <para>
    /// The candidate is loaded through the existing firm-scoped review contract
    /// and must already name this same case. The association is immutable:
    /// replacement is refused, and no review record is ever created or saved
    /// here.
    /// </para>
    /// </summary>
    Task<IntakeResult<AccountantIntakeView>> AssociateReviewAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid recordId,
        Guid accountantActorReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
