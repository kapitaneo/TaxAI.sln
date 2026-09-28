using Azure.Security.KeyVault.Keys;
using Microsoft.AspNetCore.DataProtection;
using TaxAI.AzureInfrastructure.Configuration;
using TaxAI.AzureInfrastructure.Tests.Keys;
using TaxAI.AzureInfrastructure.Tests.Protection;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Tests.Configuration;

/// <summary>
/// Spec 016 T03 — the approved-resource probe (FR-04, FR-14; AC-042, AC-043,
/// AC-047).
/// <para>
/// The key posture rules are proved one violation at a time against an approved
/// key. The probe itself then runs over the controlled Key Vault and a real
/// shared key ring: the approved topology passes, and each finding is produced
/// independently by changing exactly one thing. Every finding maps onto the
/// closed admission-refusal vocabulary.
/// </para>
/// </summary>
public sealed class ApprovedResourceProbeTests
{
    private const string SourceKey = "source-envelope";
    private const string ProtectionKey = "data-protection";

    // ---------------------------------------------------------- key posture

    private static KeyVaultKey Key(
        bool enabled = true,
        string keyType = "RSA-HSM",
        int size = 3072,
        string recoveryLevel = "Recoverable",
        bool? exportable = false,
        params string[] operations)
    {
        var vault = new ControlledKeyVault();
        vault.AddKey("probe", size, keyType, enabled, recoveryLevel, exportable, operations.Length == 0 ? null : operations);
        var client = new KeyClient(new Uri($"https://{ControlledKeyVault.Host}/"), new StaticCredential(ControlledKeyVault.ApprovedToken), vault.KeyOptions());
        return client.GetKey("probe").Value;
    }

    [Fact]
    public void AnEnabledNonExportablePurgeProtectedRsaWrappingKeyIsApproved()
    {
        Assert.Empty(ApprovedResourceProbe.AssessKey(Key()));
        Assert.Empty(ApprovedResourceProbe.AssessKey(Key(keyType: "RSA", recoveryLevel: "CustomizedRecoverable")));
        Assert.Empty(ApprovedResourceProbe.AssessKey(Key(size: 4096)));
    }

    public static TheoryData<string, KeyPostureViolation> PostureViolations() => new()
    {
        { "disabled", KeyPostureViolation.Disabled },
        { "elliptic curve", KeyPostureViolation.TypeNotApproved },
        { "2048-bit", KeyPostureViolation.SizeBelowMinimum },
        { "no unwrap", KeyPostureViolation.WrapOperationsMissing },
        { "no wrap", KeyPostureViolation.WrapOperationsMissing },
        { "soft delete without purge protection", KeyPostureViolation.PurgeProtectionDisabled },
        { "purgeable", KeyPostureViolation.PurgeProtectionDisabled },
        { "customized but purgeable", KeyPostureViolation.PurgeProtectionDisabled },
        { "exportable", KeyPostureViolation.Exportable },
        { "exportability unreported", KeyPostureViolation.Exportable },
    };

    [Theory] // AC-043: soft delete with purge protection; no plaintext export.
    [MemberData(nameof(PostureViolations))]
    public void EachKeyPostureRuleRefusesIndependently(string scenario, KeyPostureViolation expected)
    {
        var key = scenario switch
        {
            "disabled" => Key(enabled: false),
            "elliptic curve" => Key(keyType: "EC-HSM"),
            "2048-bit" => Key(size: 2048),
            "no unwrap" => Key(operations: new[] { "wrapKey", "encrypt" }),
            "no wrap" => Key(operations: new[] { "unwrapKey", "decrypt" }),
            "soft delete without purge protection" => Key(recoveryLevel: "Recoverable+Purgeable"),
            "purgeable" => Key(recoveryLevel: "Purgeable"),
            "customized but purgeable" => Key(recoveryLevel: "CustomizedRecoverable+Purgeable"),
            "exportable" => Key(exportable: true),
            "exportability unreported" => Key(exportable: null),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        Assert.Equal(new[] { expected }, ApprovedResourceProbe.AssessKey(key));
    }

    // ------------------------------------------------------- the probe itself

    private sealed class Topology
    {
        public Topology()
        {
            Environment.Vault.AddKey(SourceKey);
        }

        public DataProtectionTests.SharedEnvironment Environment { get; } = new();

        public ControlledKeyVault Vault => Environment.Vault;

        public AzureInfrastructureOptions Options { get; set; } = new()
        {
            KeyVault = new PrivateDependencySettings { Endpoint = $"https://{ControlledKeyVault.Host}/" },
            SourceEnvelope = new SourceEnvelopeSettings { KeyUri = $"https://{ControlledKeyVault.Host}/keys/{SourceKey}" },
            DataProtection = DataProtectionTests.Profile().DataProtection,
        };

        public string Token { get; set; } = ControlledKeyVault.ApprovedToken;

        public IDataProtectionProvider? KeyRing { get; set; }

        public async Task<ApprovedResourceProbeResult> ProbeAsync()
        {
            using var worker = Environment.Worker();
            var probe = new ApprovedResourceProbe(
                Options,
                new StaticCredential(Token),
                KeyRing ?? worker.GetService(typeof(IDataProtectionProvider)) as IDataProtectionProvider ?? throw new InvalidOperationException(),
                Vault.KeyOptions(),
                Vault.CryptographyOptions());
            return await probe.ProbeAsync(CancellationToken.None);
        }
    }

    [Fact] // The positive control: every probe holds against the approved topology.
    public async Task TheApprovedTopologyHasNoFinding()
    {
        var topology = new Topology();

        var result = await topology.ProbeAsync();

        Assert.True(result.IsApproved);
        Assert.Empty(result.Findings);
        Assert.Empty(result.Refusals);
        Assert.Contains(topology.Vault.Requests, request => request.EndsWith($"/keys/{SourceKey}", StringComparison.Ordinal));
        Assert.Contains(topology.Vault.Requests, request => request.EndsWith($"/keys/{ProtectionKey}", StringComparison.Ordinal));
        Assert.Contains(topology.Vault.Requests, request => request.Contains($"/keys/{SourceKey}/", StringComparison.Ordinal) && request.EndsWith("/unwrapkey", StringComparison.OrdinalIgnoreCase));
        Assert.Single(topology.Environment.Blobs.Blobs);
    }

    [Fact]
    public async Task OnlyDataPlaneOperationsAreUsed()
    {
        var topology = new Topology();

        _ = await topology.ProbeAsync();

        Assert.All(topology.Vault.Requests, request => Assert.Matches(
            @"^(GET /keys/[a-z-]+(/[0-9a-f]{32})?|POST /keys/[a-z-]+/[0-9a-f]{32}/(wrapKey|unwrapKey))$",
            request));
        Assert.DoesNotContain(topology.Vault.Requests, request => request.Contains("rotationpolicy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(topology.Vault.Requests, request => request.Contains("/secrets/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMissingSourceEnvelopeKeyRefusesSourceEnvelopeEncryption()
    {
        var topology = new Topology
        {
            Options = new AzureInfrastructureOptions
            {
                KeyVault = new PrivateDependencySettings { Endpoint = $"https://{ControlledKeyVault.Host}/" },
                SourceEnvelope = new SourceEnvelopeSettings { KeyUri = $"https://{ControlledKeyVault.Host}/keys/absent-key" },
                DataProtection = DataProtectionTests.Profile().DataProtection,
            },
        };

        var result = await topology.ProbeAsync();

        Assert.Equal(new[] { ApprovedResourceFinding.SourceEnvelopeKeyNotApproved }, result.Findings);
        Assert.Equal(new[] { ProductionAdmissionRefusal.SourceEnvelopeEncryptionMissing }, result.Refusals);
    }

    [Fact]
    public async Task APurgeableSourceEnvelopeKeyIsNotApproved()
    {
        var topology = new Topology();
        topology.Vault.AddKey("purgeable-source", recoveryLevel: "Recoverable+Purgeable");
        topology.Options = new AzureInfrastructureOptions
        {
            KeyVault = topology.Options.KeyVault,
            SourceEnvelope = new SourceEnvelopeSettings { KeyUri = $"https://{ControlledKeyVault.Host}/keys/purgeable-source" },
            DataProtection = topology.Options.DataProtection,
        };

        var result = await topology.ProbeAsync();

        Assert.Equal(new[] { ApprovedResourceFinding.SourceEnvelopeKeyNotApproved }, result.Findings);
    }

    [Fact]
    public async Task ADataProtectionKeyThatCannotWrapIsRefused()
    {
        var topology = new Topology();
        topology.Vault.Disable(ProtectionKey, topology.Environment.DataProtectionKeyVersion);

        var result = await topology.ProbeAsync();

        Assert.Contains(ApprovedResourceFinding.DataProtectionKeyNotApproved, result.Findings);
        Assert.Contains(ProductionAdmissionRefusal.DataProtectionConfigurationMissing, result.Refusals);
    }

    [Fact]
    public async Task AWrongIdentityCannotReadEitherKey()
    {
        var topology = new Topology { Token = "synthetic-foreign-identity-token" };

        var result = await topology.ProbeAsync();

        Assert.Contains(ApprovedResourceFinding.SourceEnvelopeKeyNotApproved, result.Findings);
        Assert.Contains(ApprovedResourceFinding.DataProtectionKeyNotApproved, result.Findings);
        Assert.DoesNotContain(ApprovedResourceFinding.KeyRingRoundTripFailed, result.Findings);
    }

    [Fact] // AC-047 at admission: the shared key ring must actually round-trip.
    public async Task AKeyRingThatCannotRoundTripIsRefused()
    {
        var topology = new Topology { KeyRing = new FailingKeyRing() };

        var result = await topology.ProbeAsync();

        Assert.Equal(new[] { ApprovedResourceFinding.KeyRingRoundTripFailed }, result.Findings);
        Assert.Equal(new[] { ProductionAdmissionRefusal.DurableDataProtectionMissing }, result.Refusals);
    }

    public static TheoryData<string> UnusableConfigurations() => new()
    {
        "vault endpoint missing",
        "vault endpoint over http",
        "vault endpoint with a path",
        "source key in another vault",
        "source key versioned",
        "protection key missing",
    };

    [Theory]
    [MemberData(nameof(UnusableConfigurations))]
    public async Task AnUnusableConfigurationIsRefusedWithoutAnyCall(string scenario)
    {
        var topology = new Topology();
        var options = topology.Options;
        var endpoint = scenario switch
        {
            "vault endpoint missing" => null,
            "vault endpoint over http" => $"http://{ControlledKeyVault.Host}/",
            "vault endpoint with a path" => $"https://{ControlledKeyVault.Host}/keys/",
            _ => options.KeyVault.Endpoint,
        };
        var sourceKey = scenario switch
        {
            "source key in another vault" => $"https://kv-other.vault.azure.net/keys/{SourceKey}",
            "source key versioned" => $"https://{ControlledKeyVault.Host}/keys/{SourceKey}/{new string('a', 32)}",
            _ => options.SourceEnvelope.KeyUri,
        };
        topology.Options = new AzureInfrastructureOptions
        {
            KeyVault = new PrivateDependencySettings { Endpoint = endpoint },
            SourceEnvelope = new SourceEnvelopeSettings { KeyUri = sourceKey },
            DataProtection = scenario == "protection key missing"
                ? new DataProtectionSettings()
                : options.DataProtection,
        };

        var result = await topology.ProbeAsync();

        Assert.Equal(new[] { ApprovedResourceFinding.ConfigurationUnusable }, result.Findings);
        Assert.Equal(new[] { ProductionAdmissionRefusal.KeyVaultBindingInvalid }, result.Refusals);
        Assert.Empty(topology.Vault.Requests);
    }

    // --------------------------------------------------------- the result

    [Fact]
    public void EveryFindingMapsOntoTheClosedRefusalVocabulary()
    {
        var all = new ApprovedResourceProbeResult(Enum.GetValues<ApprovedResourceFinding>());

        Assert.Equal(
            new[]
            {
                ProductionAdmissionRefusal.KeyVaultBindingInvalid,
                ProductionAdmissionRefusal.SourceEnvelopeEncryptionMissing,
                ProductionAdmissionRefusal.DataProtectionConfigurationMissing,
                ProductionAdmissionRefusal.DurableDataProtectionMissing,
            }.OrderBy(refusal => refusal),
            all.Refusals);
        Assert.False(all.IsApproved);
    }

    [Fact]
    public void AnUnverifiableProbeIsNeverApproved()
    {
        Assert.False(ApprovedResourceProbeResult.Unverifiable.IsApproved);
        Assert.Equal(new[] { ProductionAdmissionRefusal.KeyVaultBindingInvalid }, ApprovedResourceProbeResult.Unverifiable.Refusals);
    }

    [Fact]
    public void AnUndefinedFindingCannotBeRecorded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApprovedResourceProbeResult(new[] { (ApprovedResourceFinding)99 }));
    }

    [Fact]
    public void ResultsAndTheProbeAreStructurallyRendered()
    {
        Assert.Equal(nameof(ApprovedResourceProbeResult), ApprovedResourceProbeResult.Unverifiable.ToString());
        Assert.Equal(
            nameof(ApprovedResourceProbe),
            new ApprovedResourceProbe(new AzureInfrastructureOptions(), new StaticCredential("t"), new FailingKeyRing()).ToString());
    }

    private sealed class FailingKeyRing : IDataProtectionProvider, IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;

        public byte[] Protect(byte[] plaintext) =>
            throw new System.Security.Cryptography.CryptographicException("synthetic key ring failure");

        public byte[] Unprotect(byte[] protectedData) =>
            throw new System.Security.Cryptography.CryptographicException("synthetic key ring failure");
    }
}
