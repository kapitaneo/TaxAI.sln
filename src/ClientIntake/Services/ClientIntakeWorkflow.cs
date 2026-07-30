using ClientIntake.Contracts;
using ClientIntake.Models;

namespace ClientIntake.Services;

/// <summary>
/// The client side of the intake workflow.
/// <para>
/// <b>T01 freezes this signature set and implements nothing.</b> Every
/// constructor and method throws <see cref="NotImplementedException"/>; T02
/// replaces the bodies without changing a signature. Nothing in the portal
/// resolves or invokes this type in T01.
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
    /// <summary>Frozen in T01; implemented in T02.</summary>
    public ClientIntakeWorkflow(
        IIntakeStore store,
        IIntakeContentStore contentStore,
        IIntakeSecretSource secretSource,
        IntakeWorkflowOptions options) =>
        throw new NotImplementedException();

    public Task<IntakeResult<IntakeLinkInspection>> InspectLinkAsync(
        IntakeSecret credential,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IntakeResult<RedeemedIntakeSession>> RedeemLinkAsync(
        IntakeRedemptionProof proof,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IntakeResult<ClientIntakeView>> GetAsync(
        IntakeSecret sessionCredential,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IntakeResult<ClientIntakeView>> SaveAnswersAsync(
        IntakeSecret sessionCredential,
        SaveIntakeAnswersCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IntakeResult<ClientIntakeView>> UploadAsync(
        IntakeSecret sessionCredential,
        IntakeUploadCommand command,
        Stream content,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IntakeResult<ClientIntakeView>> SubmitAsync(
        IntakeSecret sessionCredential,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
