using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace Tenancy.Tests.Production;

/// <summary>
/// Spec 016 T01 — the release-evidence admission policy (AC-151, AC-152,
/// AC-168, AC-169).
/// <para>
/// A complete synthetic manifest is the positive control that proves the
/// policy can admit at all; every other case removes or spoils exactly one
/// thing. The evidence is synthetic and describes no real resource: it proves
/// the rule, not a deployment.
/// </para>
/// </summary>
public sealed class ProductionEvidencePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Subscription = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly ProductionReleaseBinding Release =
        new("1.0.0+synthetic", "0123abcd", Subscription);

    private static ProductionEvidenceItem Item(
        ProductionEvidenceSubject subject,
        ProductionEvidenceStatus status = ProductionEvidenceStatus.Approved,
        DateTimeOffset? observed = null,
        DateTimeOffset? expires = null) =>
        new(subject, status, observed ?? Now.AddDays(-1), expires ?? Now.AddDays(30));

    private static ProviderAssurance Assurance(
        ProviderAssuranceSubject subject,
        ProductionEvidenceStatus status = ProductionEvidenceStatus.Approved) =>
        new(subject, status, Now.AddDays(-1), Now.AddDays(30));

    private static ProductionApproval Approval(
        ProductionApprovalRole role,
        ProductionReleaseBinding? release = null,
        DateTimeOffset? at = null) =>
        new(role, at ?? Now.AddHours(-1), release ?? Release);

    private static ProductionEvidenceManifest Manifest(
        IEnumerable<ProductionEvidenceItem>? evidence = null,
        IEnumerable<ProviderAssurance>? assurances = null,
        IEnumerable<ProductionApproval>? approvals = null,
        ProductionReleaseBinding? release = null) =>
        new(
            release ?? Release,
            evidence ?? ProductionEvidencePolicy.RequiredEvidence.Select(subject => Item(subject)),
            assurances ?? ProductionEvidencePolicy.RequiredAssurances.Select(subject => Assurance(subject)),
            approvals ?? ProductionEvidencePolicy.RequiredApprovals.Select(role => Approval(role)));

    [Fact] // AC-152 positive control
    public void AllPassingEvidenceBothAssurancesAndBothApprovalsForTheExactReleaseAdmit()
    {
        var result = ProductionEvidencePolicy.Evaluate(Manifest(), Release, Now);

        Assert.True(result.IsAdmitted);
        Assert.Empty(result.Refusals);
    }

    [Fact]
    public void TheRequiredSetsAreTheFullClosedSets()
    {
        Assert.Equal(24, ProductionEvidencePolicy.RequiredEvidenceCount);
        Assert.Equal(24, ProductionEvidencePolicy.RequiredEvidence.Count);
        Assert.Equal(2, ProductionEvidencePolicy.RequiredAssurances.Count);
        Assert.Equal(2, ProductionEvidencePolicy.RequiredApprovals.Count);
    }

    [Fact]
    public void AMissingManifestRefuses()
    {
        Assert.Equal(
            new[] { ProductionAdmissionRefusal.EvidenceManifestMissing },
            ProductionEvidencePolicy.Evaluate(null, Release, Now).Refusals);
    }

    public static TheoryData<ProductionEvidenceSubject> EverySubject()
    {
        var data = new TheoryData<ProductionEvidenceSubject>();
        foreach (var subject in Enum.GetValues<ProductionEvidenceSubject>())
        {
            data.Add(subject);
        }

        return data;
    }

    [Theory] // AC-151: remove one mandatory item — every one, individually.
    [MemberData(nameof(EverySubject))]
    public void RemovingAnyOneMandatoryEvidenceItemRefuses(ProductionEvidenceSubject removed)
    {
        var manifest = Manifest(
            evidence: ProductionEvidencePolicy.RequiredEvidence
                .Where(subject => subject != removed)
                .Select(subject => Item(subject)));

        var result = ProductionEvidencePolicy.Evaluate(manifest, Release, Now);

        Assert.Equal(new[] { ProductionAdmissionRefusal.MandatoryEvidenceIncomplete }, result.Refusals);
    }

    [Theory]
    [InlineData(ProductionEvidenceStatus.Absent)]
    [InlineData(ProductionEvidenceStatus.Invalid)]
    [InlineData(ProductionEvidenceStatus.Unavailable)]
    [InlineData(ProductionEvidenceStatus.Pending)]
    public void AnyNonApprovedStateRefuses(ProductionEvidenceStatus status)
    {
        var manifest = Manifest(evidence: ProductionEvidencePolicy.RequiredEvidence.Select(subject =>
            subject == ProductionEvidenceSubject.DefaultDenyOutbound ? Item(subject, status) : Item(subject)));

        Assert.Contains(
            ProductionAdmissionRefusal.MandatoryEvidenceIncomplete,
            ProductionEvidencePolicy.Evaluate(manifest, Release, Now).Refusals);
    }

    [Fact]
    public void ExpiredOrFutureObservationsRefuse()
    {
        var expired = Manifest(evidence: ProductionEvidencePolicy.RequiredEvidence.Select(subject =>
            subject == ProductionEvidenceSubject.SqlRestoreDrill
                ? Item(subject, expires: Now)
                : Item(subject)));
        var future = Manifest(evidence: ProductionEvidencePolicy.RequiredEvidence.Select(subject =>
            subject == ProductionEvidenceSubject.SqlRestoreDrill
                ? Item(subject, observed: Now.AddTicks(1))
                : Item(subject)));
        var nonUtc = Manifest(evidence: ProductionEvidencePolicy.RequiredEvidence.Select(subject =>
            subject == ProductionEvidenceSubject.SqlRestoreDrill
                ? Item(subject, observed: new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.FromHours(-5)))
                : Item(subject)));

        foreach (var manifest in new[] { expired, future, nonUtc })
        {
            Assert.Equal(
                new[] { ProductionAdmissionRefusal.MandatoryEvidenceIncomplete },
                ProductionEvidencePolicy.Evaluate(manifest, Release, Now).Refusals);
        }
    }

    [Fact]
    public void DuplicatedEvidenceIsInvalidEvenWhenEveryCopyIsApproved()
    {
        var manifest = Manifest(evidence: ProductionEvidencePolicy.RequiredEvidence
            .Select(subject => Item(subject))
            .Append(Item(ProductionEvidenceSubject.ThreatModelApproval)));

        Assert.Contains(
            ProductionAdmissionRefusal.EvidenceManifestInvalid,
            ProductionEvidencePolicy.Evaluate(manifest, Release, Now).Refusals);
    }

    [Theory] // AC-168, AC-169: each provider assurance independently refuses.
    [InlineData(ProviderAssuranceSubject.InputCustomerManagedKeyCoverage, ProductionAdmissionRefusal.ProviderInputKeyAssuranceUnresolved)]
    [InlineData(ProviderAssuranceSubject.RetentionDeletionClock, ProductionAdmissionRefusal.ProviderRetentionClockAssuranceUnresolved)]
    public void EachUnresolvedProviderAssuranceIndependentlyRefuses(
        ProviderAssuranceSubject unresolved,
        ProductionAdmissionRefusal expected)
    {
        foreach (var status in new[]
                 {
                     ProductionEvidenceStatus.Pending,
                     ProductionEvidenceStatus.Unavailable,
                     ProductionEvidenceStatus.Invalid,
                 })
        {
            var manifest = Manifest(assurances: ProductionEvidencePolicy.RequiredAssurances.Select(subject =>
                subject == unresolved ? Assurance(subject, status) : Assurance(subject)));

            Assert.Equal(new[] { expected }, ProductionEvidencePolicy.Evaluate(manifest, Release, Now).Refusals);
        }

        var omitted = Manifest(assurances: ProductionEvidencePolicy.RequiredAssurances
            .Where(subject => subject != unresolved)
            .Select(subject => Assurance(subject)));

        Assert.Equal(new[] { expected }, ProductionEvidencePolicy.Evaluate(omitted, Release, Now).Refusals);
    }

    [Fact] // AC-168 and AC-169 are not satisfied by the DEP-05 row alone.
    public void AnApprovedDep05RowDoesNotStandInForEitherAssurance()
    {
        var manifest = Manifest(assurances: Array.Empty<ProviderAssurance>());

        Assert.Equal(
            new[]
            {
                ProductionAdmissionRefusal.ProviderInputKeyAssuranceUnresolved,
                ProductionAdmissionRefusal.ProviderRetentionClockAssuranceUnresolved,
            },
            ProductionEvidencePolicy.Evaluate(manifest, Release, Now).Refusals);
    }

    [Theory]
    [InlineData(ProductionApprovalRole.ProductOwner)]
    [InlineData(ProductionApprovalRole.SecurityPrivacy)]
    public void EachMissingRoleApprovalRefuses(ProductionApprovalRole missing)
    {
        var manifest = Manifest(approvals: ProductionEvidencePolicy.RequiredApprovals
            .Where(role => role != missing)
            .Select(role => Approval(role)));

        Assert.Equal(
            new[] { ProductionAdmissionRefusal.ReleaseApprovalMissing },
            ProductionEvidencePolicy.Evaluate(manifest, Release, Now).Refusals);
    }

    [Fact] // AC-152: only the exact approved configuration passes.
    public void AnApprovalForADifferentReleaseOrAFutureApprovalRefuses()
    {
        var otherDigest = new ProductionReleaseBinding("1.0.0+synthetic", "ffff", Subscription);
        var otherVersion = new ProductionReleaseBinding("1.0.1", "0123abcd", Subscription);

        foreach (var approvals in new[]
                 {
                     new[] { Approval(ProductionApprovalRole.ProductOwner), Approval(ProductionApprovalRole.SecurityPrivacy, otherDigest) },
                     new[] { Approval(ProductionApprovalRole.ProductOwner, otherVersion), Approval(ProductionApprovalRole.SecurityPrivacy) },
                     new[] { Approval(ProductionApprovalRole.ProductOwner), Approval(ProductionApprovalRole.SecurityPrivacy, at: Now.AddMinutes(1)) },
                 })
        {
            Assert.Equal(
                new[] { ProductionAdmissionRefusal.ReleaseApprovalMissing },
                ProductionEvidencePolicy.Evaluate(Manifest(approvals: approvals), Release, Now).Refusals);
        }
    }

    [Fact] // Material drift: evidence issued for another build or configuration.
    public void AManifestIssuedForAnotherReleaseRefuses()
    {
        var drifted = new ProductionReleaseBinding("1.0.0+synthetic", "changed", Subscription);

        Assert.Contains(
            ProductionAdmissionRefusal.ReleaseBindingMismatch,
            ProductionEvidencePolicy.Evaluate(Manifest(), drifted, Now).Refusals);
    }

    [Fact]
    public void TheEvaluationInstantMustBeUtc()
    {
        Assert.Throws<ArgumentException>(() => ProductionEvidencePolicy.Evaluate(
            Manifest(),
            Release,
            new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.FromHours(-4))));
    }

    // ---------------------------------------------------- configuration digest

    [Fact]
    public void TheDigestIsDeterministicAndIgnoresOrderAndKeyCase()
    {
        var first = ProductionEvidencePolicy.ComputeConfigurationDigest(new Dictionary<string, string?>
        {
            ["TaxAi:Production:A"] = "1",
            ["TaxAi:Production:B"] = null,
        });
        var second = ProductionEvidencePolicy.ComputeConfigurationDigest(new[]
        {
            new KeyValuePair<string, string?>("taxai:production:b", null),
            new KeyValuePair<string, string?>("TAXAI:PRODUCTION:A", "1"),
        });

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Theory]
    [InlineData("TaxAi:Production:A", "2", "TaxAi:Production:B", null)]
    [InlineData("TaxAi:Production:A", "1", "TaxAi:Production:B", "")]
    [InlineData("TaxAi:Production:A", "1", "TaxAi:Production:C", null)]
    [InlineData("TaxAi:Production:A", "1=", "TaxAi:Production:B", null)]
    public void AnyKeyOrValueChangeChangesTheDigest(string keyA, string? valueA, string keyB, string? valueB)
    {
        var baseline = ProductionEvidencePolicy.ComputeConfigurationDigest(new Dictionary<string, string?>
        {
            ["TaxAi:Production:A"] = "1",
            ["TaxAi:Production:B"] = null,
        });
        var changed = ProductionEvidencePolicy.ComputeConfigurationDigest(new Dictionary<string, string?>
        {
            [keyA] = valueA,
            [keyB] = valueB,
        });

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void AnAmbiguousOrBlankKeyIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ProductionEvidencePolicy.ComputeConfigurationDigest(new[]
        {
            new KeyValuePair<string, string?>("A", "1"),
            new KeyValuePair<string, string?>("a", "2"),
        }));
        Assert.Throws<ArgumentException>(() => ProductionEvidencePolicy.ComputeConfigurationDigest(new[]
        {
            new KeyValuePair<string, string?>(" ", "1"),
        }));
    }
}
