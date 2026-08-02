using ClientIntake.Contracts;
using ClientIntake.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace ClientIntake.Services;

/// <summary>
/// The accountant side of the intake workflow.
/// <para>
/// <b>T01 freezes this signature set and implements nothing.</b> Every
/// constructor and method throws <see cref="NotImplementedException"/> so that
/// T02 and T03 compile against one approved contract without any task inventing
/// a second shape for it. Nothing in the portal resolves or invokes this type in
/// T01, so no request can reach a stub.
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
    /// <summary>
    /// Frozen in T01; implemented in T02. It throws rather than storing its
    /// arguments, because a half-constructed workflow that accepts a call and
    /// then fails deeper is harder to diagnose than one that refuses at the door.
    /// </summary>
    public AccountantIntakeWorkflow(
        IIntakeStore store,
        IIntakeCredentialSource credentialSource,
        IReviewRecordRepository reviewRecords,
        IntakeWorkflowOptions options) =>
        throw new NotImplementedException();

    public Task<IntakeResult<CreatedIntakeCase>> CreateAsync(
        TrustedFirmId firm,
        Guid accountantActorReference,
        CreateIntakeCaseCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IntakeResult<AccountantIntakeView>> GetProgressAsync(
        TrustedFirmId firm,
        Guid caseId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IntakeResult<IssuedIntakeLink>> ReissueLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid accountantActorReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IntakeResult<AccountantIntakeView>> RevokeLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid accountantActorReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IntakeResult<AccountantIntakeView>> AssociateReviewAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid recordId,
        Guid accountantActorReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
