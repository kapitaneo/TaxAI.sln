using ClientIntake.Models;
using ClientIntake.Services;
using ClientIntake.Tests.Support;
using TaxCases.Models;

namespace ClientIntake.Tests.EndToEnd;

public sealed class BackendAcceptanceTests
{
    [Fact]
    public async Task LinkAndSessionExpiryBoundariesAreExclusive()
    {
        var linkFixture = await SyntheticIntakeFixtures.CreateHarnessAsync();
        var expiredLink = await linkFixture.Client.InspectLinkAsync(
            linkFixture.Created.LinkCredential,
            SyntheticIntakeFixtures.Now.AddHours(1),
            CancellationToken.None);
        Assert.Equal(IntakeResultCode.NotFound, expiredLink.Code);

        var sessionFixture = await SyntheticIntakeFixtures.CreateHarnessAsync();
        var session = await sessionFixture.RedeemAsync();
        var expiredSession = await sessionFixture.Client.GetAsync(
            session.SessionCredential,
            SyntheticIntakeFixtures.Now.AddMinutes(15),
            CancellationToken.None);
        Assert.Equal(IntakeResultCode.NotFound, expiredSession.Code);
    }

    [Fact]
    public async Task ReissueAndRevokeInvalidateEveryPriorClientCapability()
    {
        var fixture = await SyntheticIntakeFixtures.CreateHarnessAsync();
        var firstSession = await fixture.RedeemAsync();

        var reissued = await fixture.Accountant.ReissueLinkAsync(
            SyntheticIntakeFixtures.Firm,
            fixture.Created.CaseId,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.Now.AddMinutes(1),
            CancellationToken.None);
        Assert.True(reissued.IsSuccess);
        Assert.Equal(
            IntakeResultCode.NotFound,
            (await fixture.Client.GetAsync(
                firstSession.SessionCredential,
                SyntheticIntakeFixtures.Now.AddMinutes(1),
                CancellationToken.None)).Code);
        Assert.Equal(
            IntakeResultCode.NotFound,
            (await fixture.Client.InspectLinkAsync(
                fixture.Created.LinkCredential,
                SyntheticIntakeFixtures.Now.AddMinutes(1),
                CancellationToken.None)).Code);

        var secondInspection = await fixture.Client.InspectLinkAsync(
            reissued.Value!.LinkCredential,
            SyntheticIntakeFixtures.Now.AddMinutes(1),
            CancellationToken.None);
        var secondSession = await fixture.Client.RedeemLinkAsync(
            secondInspection.Value!.Proof,
            SyntheticIntakeFixtures.Now.AddMinutes(1),
            CancellationToken.None);
        Assert.True(secondSession.IsSuccess);

        var revoked = await fixture.Accountant.RevokeLinkAsync(
            SyntheticIntakeFixtures.Firm,
            fixture.Created.CaseId,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.Now.AddMinutes(2),
            CancellationToken.None);
        Assert.True(revoked.IsSuccess);
        Assert.Equal(IntakeLinkState.Revoked, revoked.Value!.LinkState);
        Assert.Equal(
            IntakeResultCode.NotFound,
            (await fixture.Client.GetAsync(
                secondSession.Value!.SessionCredential,
                SyntheticIntakeFixtures.Now.AddMinutes(2),
                CancellationToken.None)).Code);
    }

    [Fact]
    public async Task IdenticalBytesInDifferentCasesAreNotDuplicates()
    {
        var store = new InMemoryIntakeStore();
        var source = new SyntheticIntakeFixtures.CredentialSource();
        var review = new SyntheticIntakeFixtures.ReviewRepository();
        var accountant = new AccountantIntakeWorkflow(
            store,
            source,
            review,
            SyntheticIntakeFixtures.Options());
        var content = new SyntheticIntakeFixtures.ContentStore();
        var client = new ClientIntakeWorkflow(
            store,
            content,
            source,
            SyntheticIntakeFixtures.Options());
        var first = await CreateAndRedeemAsync(accountant, client);
        var second = await CreateAndRedeemAsync(accountant, client);
        var bytes = SyntheticIntakeFixtures.Pdf();

        var firstUpload = await UploadAsync(
            client,
            first.Session,
            bytes,
            minute: 1);
        var secondUpload = await UploadAsync(
            client,
            second.Session,
            bytes,
            minute: 1);

        Assert.True(firstUpload.IsSuccess);
        Assert.True(secondUpload.IsSuccess);
        Assert.Equal(2, content.Count);
    }

    [Fact]
    public async Task ReviewAssociationRequiresTheExactExistingCaseAndNeverSaves()
    {
        var review = new SyntheticIntakeFixtures.ReviewRepository();
        var fixture = await SyntheticIntakeFixtures.CreateHarnessAsync(review: review);
        var recordId = Guid.NewGuid();

        review.Record = SyntheticIntakeFixtures.ReviewRecord(
            recordId,
            Guid.NewGuid(),
            taxYear: 2025);
        var wrongCase = await fixture.Accountant.AssociateReviewAsync(
            SyntheticIntakeFixtures.Firm,
            fixture.Created.CaseId,
            recordId,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.Now.AddMinutes(1),
            CancellationToken.None);
        Assert.Equal(IntakeResultCode.NotFound, wrongCase.Code);

        review.Record = SyntheticIntakeFixtures.ReviewRecord(
            recordId,
            fixture.Created.CaseId,
            taxYear: 2024);
        var wrongYear = await fixture.Accountant.AssociateReviewAsync(
            SyntheticIntakeFixtures.Firm,
            fixture.Created.CaseId,
            recordId,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.Now.AddMinutes(2),
            CancellationToken.None);
        Assert.Equal(IntakeResultCode.NotFound, wrongYear.Code);

        review.Record = SyntheticIntakeFixtures.ReviewRecord(
            recordId,
            fixture.Created.CaseId,
            taxYear: 2025);
        var associated = await fixture.Accountant.AssociateReviewAsync(
            SyntheticIntakeFixtures.Firm,
            fixture.Created.CaseId,
            recordId,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.Now.AddMinutes(3),
            CancellationToken.None);
        Assert.True(associated.IsSuccess);

        var replacement = await fixture.Accountant.AssociateReviewAsync(
            SyntheticIntakeFixtures.Firm,
            fixture.Created.CaseId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            SyntheticIntakeFixtures.Now.AddMinutes(4),
            CancellationToken.None);
        Assert.Equal(IntakeResultCode.Conflict, replacement.Code);
        Assert.Equal(3, review.FindCalls);
        Assert.Equal(0, review.SaveCalls);
        var stored = await fixture.Store.FindAsync(
            SyntheticIntakeFixtures.Firm,
            fixture.Created.CaseId,
            CancellationToken.None);
        Assert.Equal(recordId, stored!.ReviewAssociation!.RecordId);
    }

    private static async Task<(
        CreatedIntakeCase Created,
        RedeemedIntakeSession Session)> CreateAndRedeemAsync(
        AccountantIntakeWorkflow accountant,
        ClientIntakeWorkflow client)
    {
        var created = await accountant.CreateAsync(
            SyntheticIntakeFixtures.Firm,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.CreateCommand(t4: 1, medical: 0),
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);
        var inspection = await client.InspectLinkAsync(
            created.Value!.LinkCredential,
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);
        var session = await client.RedeemLinkAsync(
            inspection.Value!.Proof,
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);
        return (created.Value, session.Value!);
    }

    private static Task<IntakeResult<ClientIntakeView>> UploadAsync(
        ClientIntakeWorkflow client,
        RedeemedIntakeSession session,
        byte[] bytes,
        int minute) =>
        client.UploadAsync(
            session.SessionCredential,
            new IntakeUploadCommand(
                DocumentCategory.T4Slip,
                1,
                IntakeMediaKind.Pdf,
                bytes.Length),
            new MemoryStream(bytes),
            SyntheticIntakeFixtures.Now.AddMinutes(minute),
            CancellationToken.None);
}
