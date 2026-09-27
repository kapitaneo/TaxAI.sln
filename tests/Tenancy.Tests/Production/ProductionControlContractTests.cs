using System.Reflection;
using System.Text.Json;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Tenancy.Tests.Production;

/// <summary>
/// Spec 016 T01 — the production-control vocabulary and evidence shapes that
/// Tenancy owns (AC-151, AC-152, AC-168, AC-169).
/// <para>
/// These types decide whether real taxpayer data may be admitted, so their
/// shape is pinned: closed enumerations with no affirmative default, sealed
/// defensively-copied classes, structural rendering, and serialization
/// omission of opaque infrastructure identifiers.
/// </para>
/// </summary>
public sealed class ProductionControlContractTests
{
    [Fact]
    public void ThereAreExactlyTwentyFourEvidenceSubjectsNumberedAsTheirDepIds()
    {
        var values = Enum.GetValues<ProductionEvidenceSubject>().Select(value => (int)value).ToArray();

        Assert.Equal(Enumerable.Range(1, 24), values);
        Assert.False(Enum.IsDefined((ProductionEvidenceSubject)0));
        Assert.Equal(5, (int)ProductionEvidenceSubject.ProcessorAssuranceApproval);
        Assert.Equal(23, (int)ProductionEvidenceSubject.StartupConfigurationRefusals);
        Assert.Equal(24, (int)ProductionEvidenceSubject.ReleaseRoleApproval);
    }

    [Fact]
    public void TheZeroEvidenceStatusIsAbsentNotApproved()
    {
        Assert.Equal(ProductionEvidenceStatus.Absent, default(ProductionEvidenceStatus));
        Assert.Equal(
            new[] { "Absent", "Invalid", "Unavailable", "Pending", "Approved" },
            Enum.GetNames<ProductionEvidenceStatus>());
    }

    [Fact]
    public void TheZeroIdentityOutcomeIsUnverified()
    {
        Assert.Equal(ManagedIdentityBindingOutcome.Unverified, default(ManagedIdentityBindingOutcome));
    }

    [Fact]
    public void BothProviderAssurancesAndBothRolesAreDistinctClosedMembers()
    {
        Assert.Equal(
            new[] { "InputCustomerManagedKeyCoverage", "RetentionDeletionClock" },
            Enum.GetNames<ProviderAssuranceSubject>());
        Assert.Equal(
            new[] { "ProductOwner", "SecurityPrivacy" },
            Enum.GetNames<ProductionApprovalRole>());
        Assert.False(Enum.IsDefined((ProviderAssuranceSubject)0));
        Assert.False(Enum.IsDefined((ProductionApprovalRole)0));
    }

    [Fact]
    public void TheRefusalVocabularyHasNoAdmittingMember()
    {
        var names = Enum.GetNames<ProductionAdmissionRefusal>();

        Assert.Equal(40, names.Length);
        Assert.False(Enum.IsDefined((ProductionAdmissionRefusal)0));
        Assert.DoesNotContain(names, name =>
            name.Contains("None", StringComparison.Ordinal) ||
            name.Contains("Admit", StringComparison.Ordinal) ||
            name.Contains("Pass", StringComparison.Ordinal) ||
            name.Contains("Bypass", StringComparison.Ordinal) ||
            name.Contains("Override", StringComparison.Ordinal));
        Assert.Equal(
            names.Length,
            Enum.GetValues<ProductionAdmissionRefusal>().Select(value => (int)value).Distinct().Count());
    }

    [Fact]
    public void ReadinessIsAdmittedOnlyWhenNoRefusalRemainsAndIsOrderedAndDistinct()
    {
        var admitted = ProductionReadiness.FromRefusals(Array.Empty<ProductionAdmissionRefusal>());
        var refused = ProductionReadiness.FromRefusals(new[]
        {
            ProductionAdmissionRefusal.ReleaseApprovalMissing,
            ProductionAdmissionRefusal.EnvironmentNotProduction,
            ProductionAdmissionRefusal.ReleaseApprovalMissing,
        });

        Assert.True(admitted.IsAdmitted);
        Assert.False(refused.IsAdmitted);
        Assert.Equal(
            new[]
            {
                ProductionAdmissionRefusal.EnvironmentNotProduction,
                ProductionAdmissionRefusal.ReleaseApprovalMissing,
            },
            refused.Refusals);
        Assert.Equal(
            refused.Refusals,
            ProductionReadiness.Combine(admitted, refused).Refusals);
    }

    [Fact]
    public void ReadinessRefusesAnUndefinedRefusalCode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductionReadiness.FromRefusals(new[] { (ProductionAdmissionRefusal)999 }));
    }

    [Fact]
    public void ReadinessExposesADefensiveCopy()
    {
        var readiness = ProductionReadiness.FromRefusals(
            new[] { ProductionAdmissionRefusal.EvidenceManifestMissing });

        var exposed = Assert.IsType<ProductionAdmissionRefusal[]>(readiness.Refusals);
        exposed[0] = ProductionAdmissionRefusal.ReleaseApprovalMissing;

        Assert.Equal(
            new[] { ProductionAdmissionRefusal.EvidenceManifestMissing },
            readiness.Refusals);
    }

    [Fact]
    public void TheManifestCopiesItsInputsAndRefusesNullEntries()
    {
        var release = new ProductionReleaseBinding("1.0.0", "digest", Guid.NewGuid());
        var evidence = new List<ProductionEvidenceItem>();
        var manifest = new ProductionEvidenceManifest(
            release,
            evidence,
            Array.Empty<ProviderAssurance>(),
            Array.Empty<ProductionApproval>());

        evidence.Add(new ProductionEvidenceItem(
            ProductionEvidenceSubject.ThreatModelApproval,
            ProductionEvidenceStatus.Approved,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.MaxValue));

        Assert.Empty(manifest.Evidence);
        Assert.Throws<ArgumentException>(() => new ProductionEvidenceManifest(
            release,
            new ProductionEvidenceItem[] { null! },
            Array.Empty<ProviderAssurance>(),
            Array.Empty<ProductionApproval>()));
    }

    [Theory]
    [InlineData("", "digest")]
    [InlineData("1.0.0", " ")]
    public void AReleaseBindingRequiresAVersionAndADigest(string version, string digest)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ProductionReleaseBinding(version, digest, Guid.NewGuid()));
    }

    [Fact]
    public void AReleaseBindingMatchesOnlyAllThreePartsOrdinally()
    {
        var subscription = Guid.NewGuid();
        var binding = new ProductionReleaseBinding("1.0.0", "abc", subscription);

        Assert.True(binding.Matches(new ProductionReleaseBinding("1.0.0", "abc", subscription)));
        Assert.False(binding.Matches(new ProductionReleaseBinding("1.0.1", "abc", subscription)));
        Assert.False(binding.Matches(new ProductionReleaseBinding("1.0.0", "ABC", subscription)));
        Assert.False(binding.Matches(new ProductionReleaseBinding("1.0.0", "abc", Guid.NewGuid())));
    }

    [Fact]
    public void AManagedIdentityBindingRefusesEmptyIdentifiers()
    {
        Assert.Throws<ArgumentException>(() =>
            new ManagedIdentityBinding(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "/r"));
        Assert.Throws<ArgumentException>(() =>
            new ManagedIdentityBinding(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "/r"));
        Assert.Throws<ArgumentException>(() =>
            new ManagedIdentityBinding(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "/r"));
        Assert.ThrowsAny<ArgumentException>(() =>
            new ManagedIdentityBinding(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " "));
    }

    [Fact]
    public void OpaqueInfrastructureIdentifiersAreOmittedFromSerializationAndRendering()
    {
        var tenant = Guid.NewGuid();
        var client = Guid.NewGuid();
        var principal = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var identity = new ManagedIdentityBinding(tenant, client, principal, "/planted/resource");
        var release = new ProductionReleaseBinding("1.0.0", "digest", subscription);

        var rendered = string.Join(
            "|",
            JsonSerializer.Serialize(identity),
            JsonSerializer.Serialize(release),
            identity.ToString(),
            release.ToString());

        foreach (var planted in new[]
                 {
                     tenant.ToString(), client.ToString(), principal.ToString(),
                     subscription.ToString(), "/planted/resource",
                 })
        {
            Assert.DoesNotContain(planted, rendered, StringComparison.OrdinalIgnoreCase);
        }

        // Positive control: the non-opaque parts do serialize.
        Assert.Contains("1.0.0", JsonSerializer.Serialize(release), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryProductionControlInstanceTypeIsASealedNonRecordClassWithStructuralToString()
    {
        var types = new[]
        {
            typeof(ProductionEvidenceItem), typeof(ProviderAssurance), typeof(ProductionReadiness),
            typeof(ProductionReleaseBinding), typeof(ProductionApproval),
            typeof(ProductionEvidenceManifest), typeof(ManagedIdentityBinding),
        };

        foreach (var type in types)
        {
            Assert.True(type.IsSealed, type.Name);
            Assert.Null(type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance));
            Assert.Equal(type, type.GetMethod(nameof(ToString), Type.EmptyTypes)!.DeclaringType);
            Assert.All(
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
                property => Assert.Null(property.SetMethod?.IsPublic == true ? property.SetMethod : null));
        }
    }

    [Fact]
    public void TheIdentityProbePortReturnsOnlyTheClosedOutcome()
    {
        var method = Assert.Single(typeof(IManagedIdentityBindingProbe).GetMethods());

        Assert.Equal(typeof(Task<ManagedIdentityBindingOutcome>), method.ReturnType);
        Assert.Equal(
            new[] { typeof(ManagedIdentityBinding), typeof(CancellationToken) },
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }
}
