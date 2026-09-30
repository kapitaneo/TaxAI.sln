using System.Text;
using TaxAI.AzureInfrastructure.Control;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Tests.Control;

/// <summary>
/// Spec 016 T06 — the Canadian release-evidence reader (FR-14): evidence for
/// exactly one binding, withdrawal that wins over publication, and no substitute
/// read from an unrelated binding. The real reader runs over the controlled
/// in-memory control accounts.
/// </summary>
public sealed class ProductionEvidenceReaderTests
{
    private static readonly ProductionReleaseBinding Release = new(
        "1.0.0+synthetic",
        new string('d', 64),
        Guid.Parse("22222222-2222-4222-8222-222222222222"));

    private const string Manifest = """{"schemaVersion":1}""";

    private static void Publish(ControlledControlAccounts accounts, string prefix, ProductionReleaseBinding release, string body, params string[] hosts)
    {
        foreach (var host in hosts.Length == 0 ? new[] { ControlledControlAccounts.CentralHost, ControlledControlAccounts.EastHost } : hosts)
        {
            accounts.Put(
                host,
                AzureProductionEvidenceReader.EvidenceContainerName,
                prefix + AzureProductionEvidenceReader.BindingKey(release) + ".json",
                Encoding.UTF8.GetBytes(body));
        }
    }

    [Fact]
    public async Task EvidencePublishedForTheExactBindingIsPresent()
    {
        var accounts = new ControlledControlAccounts();
        Publish(accounts, "release/", Release, Manifest);

        var document = await accounts.Reader().ReadAsync(Release, CancellationToken.None);

        Assert.Equal(ProductionEvidenceDocumentStatus.Present, document.Status);
        Assert.Equal(Manifest, document.ManifestText);
        Assert.Equal("ProductionEvidenceDocument(Present)", document.ToString());
    }

    [Fact] // Material drift: evidence for one binding never answers for another.
    public async Task EvidenceForAnotherBindingIsAbsent()
    {
        var accounts = new ControlledControlAccounts();
        Publish(accounts, "release/", new ProductionReleaseBinding("1.0.1+synthetic", Release.ConfigurationDigest, Release.SubscriptionId), Manifest);
        Publish(accounts, "release/", new ProductionReleaseBinding(Release.ArtifactVersion, new string('e', 64), Release.SubscriptionId), Manifest);

        var document = await accounts.Reader().ReadAsync(Release, CancellationToken.None);

        Assert.Equal(ProductionEvidenceDocumentStatus.Absent, document.Status);
        Assert.Null(document.ManifestText);
    }

    [Theory] // Later invalidation withdraws authorization, in either copy.
    [InlineData(ControlledControlAccounts.CentralHost)]
    [InlineData(ControlledControlAccounts.EastHost)]
    public async Task AWithdrawalInEitherCopyWithdrawsTheEvidence(string host)
    {
        var accounts = new ControlledControlAccounts();
        Publish(accounts, "release/", Release, Manifest);
        Publish(accounts, "withdrawn/", Release, "{}", host);

        Assert.Equal(
            ProductionEvidenceDocumentStatus.Withdrawn,
            (await accounts.Reader().ReadAsync(Release, CancellationToken.None)).Status);
    }

    [Fact] // One reachable Canadian copy is enough to read; none is unavailable.
    public async Task OneUnreachableCopyStillReadsAndTwoAreUnavailable()
    {
        var accounts = new ControlledControlAccounts();
        Publish(accounts, "release/", Release, Manifest);
        accounts.Unreachable.Add(ControlledControlAccounts.CentralHost);

        Assert.Equal(ProductionEvidenceDocumentStatus.Present, (await accounts.Reader().ReadAsync(Release, CancellationToken.None)).Status);

        accounts.Unreachable.Add(ControlledControlAccounts.EastHost);
        Assert.Equal(ProductionEvidenceDocumentStatus.Unavailable, (await accounts.Reader().ReadAsync(Release, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task CopiesThatDisagreeAreInvalidNotEitherOne()
    {
        var accounts = new ControlledControlAccounts();
        Publish(accounts, "release/", Release, Manifest, ControlledControlAccounts.CentralHost);
        Publish(accounts, "release/", Release, """{"schemaVersion":1,"other":true}""", ControlledControlAccounts.EastHost);

        Assert.Equal(ProductionEvidenceDocumentStatus.Invalid, (await accounts.Reader().ReadAsync(Release, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task AnOversizedOrUndecodableDocumentIsInvalid()
    {
        var oversized = new ControlledControlAccounts();
        Publish(oversized, "release/", Release, new string('x', AzureProductionEvidenceReader.MaximumManifestBytes + 1));
        Assert.Equal(ProductionEvidenceDocumentStatus.Invalid, (await oversized.Reader().ReadAsync(Release, CancellationToken.None)).Status);

        var undecodable = new ControlledControlAccounts();
        foreach (var host in new[] { ControlledControlAccounts.CentralHost, ControlledControlAccounts.EastHost })
        {
            undecodable.Put(host, AzureProductionEvidenceReader.EvidenceContainerName,
                "release/" + AzureProductionEvidenceReader.BindingKey(Release) + ".json", new byte[] { 0xff, 0xfe, 0xfd });
        }

        Assert.Equal(ProductionEvidenceDocumentStatus.Invalid, (await undecodable.Reader().ReadAsync(Release, CancellationToken.None)).Status);
    }

    [Fact] // The binding key is opaque: it names no version, digest or subscription.
    public void TheBindingKeyIsAnOpaqueHashOfAllThreeParts()
    {
        var key = AzureProductionEvidenceReader.BindingKey(Release);

        Assert.Matches("^[0-9a-f]{64}$", key);
        Assert.DoesNotContain("synthetic", key, StringComparison.Ordinal);
        Assert.NotEqual(key, AzureProductionEvidenceReader.BindingKey(
            new ProductionReleaseBinding(Release.ArtifactVersion, Release.ConfigurationDigest, Guid.NewGuid())));
    }

    [Fact] // Read-only: the reader never writes a Canadian evidence object.
    public async Task TheReaderNeverWrites()
    {
        var accounts = new ControlledControlAccounts();
        Publish(accounts, "release/", Release, Manifest);

        await accounts.Reader().ReadAsync(Release, CancellationToken.None);

        Assert.DoesNotContain(accounts.Log, entry => entry.Method is "PUT" or "DELETE");
        Assert.Equal(nameof(AzureProductionEvidenceReader), accounts.Reader().ToString());
    }
}
