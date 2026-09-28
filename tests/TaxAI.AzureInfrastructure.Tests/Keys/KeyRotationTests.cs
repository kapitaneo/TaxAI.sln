using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TaxAI.AzureInfrastructure.Keys;

namespace TaxAI.AzureInfrastructure.Tests.Keys;

/// <summary>
/// Spec 016 T03 — key rotation, retirement and emergency revocation (FR-04;
/// AC-044, AC-045, AC-046; DEP-14).
/// <para>
/// The retirement rules are proved one refusal at a time against a complete,
/// current, unreferenced inventory that is the only permitted case, so a guard
/// that refused everything could not pass. The executable rotation drill is then
/// run for real, offline, against this build's compiled guard; its live mode is
/// proved never to report a pass without explicit authority.
/// </para>
/// </summary>
public sealed class KeyRotationTests
{
    private const string KeyId = "https://kv-taxai-prod-cc.vault.azure.net/keys/source-envelope";
    private static readonly string Old = $"{KeyId}/{new string('1', 32)}";
    private static readonly string Active = $"{KeyId}/{new string('2', 32)}";
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly KeyVersionUsageGuard _guard = new();

    private static KeyUsageEvidence Evidence(
        KeyUsageSource source,
        bool complete = true,
        DateTimeOffset? observed = null,
        params string[] references) =>
        new(source, complete, observed ?? Now.AddHours(-1), references);

    private static KeyUsageEvidence[] Unreferenced() => new[]
    {
        Evidence(KeyUsageSource.RetainedCiphertext),
        Evidence(KeyUsageSource.DataProtectionKeyRing),
        Evidence(KeyUsageSource.RecoverableBackup),
    };

    private KeyGuardDecision Retire(IEnumerable<KeyUsageEvidence> evidence, string? candidate = null, string? active = null) =>
        _guard.EvaluateRetirement(new KeyRetirementRequest(candidate ?? Old, active ?? Active, evidence, Now));

    // --------------------------------------------------------- retirement

    [Fact] // The one permitted case: rewrapped, complete, current, unreferenced.
    public void AnUnreferencedOldVersionWithCompleteCurrentEvidenceMayBeRetired()
    {
        var decision = Retire(Unreferenced());

        Assert.True(decision.IsPermitted);
        Assert.Empty(decision.Refusals);
    }

    public static TheoryData<KeyUsageSource> Sources() => new()
    {
        KeyUsageSource.RetainedCiphertext,
        KeyUsageSource.DataProtectionKeyRing,
        KeyUsageSource.RecoverableBackup,
    };

    [Theory] // AC-046: any source still referencing the version refuses.
    [MemberData(nameof(Sources))]
    public void AVersionStillReferencedByAnySourceIsNotRetired(KeyUsageSource source)
    {
        var evidence = Unreferenced()
            .Select(item => item.Source == source ? Evidence(source, references: Old) : item);

        var decision = Retire(evidence);

        Assert.False(decision.IsPermitted);
        Assert.Equal(new[] { KeyGuardRefusal.VersionStillRequired }, decision.Refusals);
    }

    [Fact] // A reference in another spelling of the same identifier still counts.
    public void AReferenceIsMatchedCaseInsensitivelyOnHostAndVersion()
    {
        var shouted = Old.Replace("kv-taxai-prod-cc", "KV-TAXAI-PROD-CC", StringComparison.Ordinal);
        var evidence = Unreferenced()
            .Select(item => item.Source == KeyUsageSource.RetainedCiphertext
                ? Evidence(item.Source, references: shouted)
                : item);

        Assert.Contains(KeyGuardRefusal.VersionStillRequired, Retire(evidence).Refusals);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void AMissingSourceIsIncompleteNotEmpty(KeyUsageSource missing)
    {
        var decision = Retire(Unreferenced().Where(item => item.Source != missing));

        Assert.Equal(new[] { KeyGuardRefusal.UsageEvidenceIncomplete }, decision.Refusals);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void AnIncompleteSourceRefuses(KeyUsageSource incomplete)
    {
        var evidence = Unreferenced()
            .Select(item => item.Source == incomplete ? Evidence(incomplete, complete: false) : item);

        Assert.Equal(new[] { KeyGuardRefusal.UsageEvidenceIncomplete }, Retire(evidence).Refusals);
    }

    [Fact]
    public void ADuplicatedSourceIsAmbiguousAndRefuses()
    {
        var evidence = Unreferenced().Append(Evidence(KeyUsageSource.RecoverableBackup));

        Assert.Contains(KeyGuardRefusal.UsageEvidenceIncomplete, Retire(evidence).Refusals);
    }

    [Fact]
    public void AnUnknownSourceRefuses()
    {
        var evidence = Unreferenced().Append(Evidence((KeyUsageSource)99));

        Assert.Contains(KeyGuardRefusal.UsageEvidenceIncomplete, Retire(evidence).Refusals);
    }

    [Fact]
    public void AMalformedReferenceMakesTheInventoryIncomplete()
    {
        var evidence = Unreferenced()
            .Select(item => item.Source == KeyUsageSource.RecoverableBackup
                ? Evidence(item.Source, references: "not-a-key-version")
                : item);

        Assert.Contains(KeyGuardRefusal.UsageEvidenceIncomplete, Retire(evidence).Refusals);
    }

    public static TheoryData<int> StaleAges() => new() { 25, 48, 24 * 30 };

    [Theory]
    [MemberData(nameof(StaleAges))]
    public void StaleEvidenceRefuses(int hoursOld)
    {
        var evidence = Unreferenced()
            .Select(item => item.Source == KeyUsageSource.DataProtectionKeyRing
                ? Evidence(item.Source, observed: Now.AddHours(-hoursOld))
                : item);

        Assert.Equal(new[] { KeyGuardRefusal.UsageEvidenceStale }, Retire(evidence).Refusals);
    }

    [Fact]
    public void EvidenceExactlyAtTheMaximumAgeIsCurrent()
    {
        var evidence = Unreferenced()
            .Select(item => Evidence(item.Source, observed: Now - KeyVersionUsageGuard.EvidenceMaximumAge));

        Assert.True(Retire(evidence).IsPermitted);
    }

    [Fact]
    public void FutureDatedEvidenceRefuses()
    {
        var evidence = Unreferenced()
            .Select(item => item.Source == KeyUsageSource.RetainedCiphertext
                ? Evidence(item.Source, observed: Now.AddMinutes(1))
                : item);

        Assert.Equal(new[] { KeyGuardRefusal.UsageEvidenceStale }, Retire(evidence).Refusals);
    }

    [Fact]
    public void TheActiveVersionIsNeverRetired()
    {
        Assert.Equal(new[] { KeyGuardRefusal.ActiveVersion }, Retire(Unreferenced(), candidate: Active).Refusals);
    }

    public static TheoryData<string, string> MalformedPairs() => new()
    {
        { "not-a-key", Active },
        { Old, "not-a-key" },
        { KeyId, Active },
        { "https://kv-taxai-prod-cc.vault.azure.net/keys/data-protection/" + new string('1', 32), Active },
        { "https://kv-other.vault.azure.net/keys/source-envelope/" + new string('1', 32), Active },
    };

    [Theory] // The candidate and the active version must name the same key.
    [MemberData(nameof(MalformedPairs))]
    public void AMalformedOrMismatchedVersionPairRefuses(string candidate, string active)
    {
        Assert.Equal(
            new[] { KeyGuardRefusal.KeyVersionMalformed },
            Retire(Unreferenced(), candidate, active).Refusals);
    }

    [Fact]
    public void EveryApplicableRefusalIsReportedTogether()
    {
        var evidence = new[]
        {
            Evidence(KeyUsageSource.RetainedCiphertext, complete: false, references: Old),
            Evidence(KeyUsageSource.DataProtectionKeyRing, observed: Now.AddDays(-2)),
        };

        Assert.Equal(
            new[]
            {
                KeyGuardRefusal.VersionStillRequired,
                KeyGuardRefusal.UsageEvidenceIncomplete,
                KeyGuardRefusal.UsageEvidenceStale,
            }.OrderBy(refusal => refusal),
            Retire(evidence).Refusals);
    }

    [Fact]
    public void RequiredSourcesAreExactlyCiphertextKeyRingAndBackups()
    {
        Assert.Equal(Enum.GetValues<KeyUsageSource>(), KeyVersionUsageGuard.RequiredSources);
    }

    // ------------------------------------------------ rotation target (AC-044)

    [Theory]
    [InlineData("P90D", true)]
    [InlineData("P89D", false)]
    [InlineData("P91D", false)]
    [InlineData("P365D", false)]
    [InlineData("P3M", false)]
    [InlineData("PT2160H", false)]
    [InlineData("p90d", false)]
    [InlineData(" P90D", false)]
    [InlineData("P-90D", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheExactNinetyDayRotationTargetIsApproved(string? rotateAfterCreate, bool approved)
    {
        Assert.Equal(approved, KeyVersionUsageGuard.MeetsRotationTarget(rotateAfterCreate));
        Assert.Equal(TimeSpan.FromDays(90), KeyVersionUsageGuard.RotationTarget);
    }

    // ------------------------------------------- emergency revocation

    private static EmergencyRevocationRequest Revocation(
        PrivilegedElevation elevation = PrivilegedElevation.JustInTime,
        RevocationJustification justification = RevocationJustification.SuspectedKeyCompromise,
        string? reference = "INC-0001",
        Guid? requester = null,
        Guid? reviewer = null,
        TimeSpan? window = null,
        DateTimeOffset? evaluatedAt = null)
    {
        var start = Now.AddMinutes(-10);
        return new EmergencyRevocationRequest(
            elevation,
            justification,
            reference,
            requester ?? Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            reviewer ?? Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
            start,
            start + (window ?? TimeSpan.FromHours(4)),
            evaluatedAt ?? Now);
    }

    [Fact]
    public void AJustInTimeJustifiedIndependentlyReviewedBoundedRevocationIsAuthorized()
    {
        Assert.True(_guard.EvaluateEmergencyRevocation(Revocation()).IsPermitted);
    }

    public static TheoryData<string, KeyGuardRefusal> RevocationRefusals() => new()
    {
        { "standing access", KeyGuardRefusal.ElevationNotJustInTime },
        { "no elevation", KeyGuardRefusal.ElevationNotJustInTime },
        { "no justification", KeyGuardRefusal.JustificationMissing },
        { "unknown justification", KeyGuardRefusal.JustificationMissing },
        { "no reference", KeyGuardRefusal.JustificationMissing },
        { "self review", KeyGuardRefusal.IndependentReviewMissing },
        { "no reviewer", KeyGuardRefusal.IndependentReviewMissing },
        { "no requester", KeyGuardRefusal.IndependentReviewMissing },
        { "window too long", KeyGuardRefusal.ElevationWindowNotBounded },
        { "empty window", KeyGuardRefusal.ElevationWindowNotBounded },
        { "before the window", KeyGuardRefusal.OutsideElevationWindow },
        { "after the window", KeyGuardRefusal.OutsideElevationWindow },
    };

    [Theory]
    [MemberData(nameof(RevocationRefusals))]
    public void EachEmergencyRevocationRuleRefusesIndependently(string scenario, KeyGuardRefusal expected)
    {
        var requester = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var request = scenario switch
        {
            "standing access" => Revocation(elevation: PrivilegedElevation.Standing),
            "no elevation" => Revocation(elevation: PrivilegedElevation.None),
            "no justification" => Revocation(justification: RevocationJustification.None),
            "unknown justification" => Revocation(justification: (RevocationJustification)42),
            "no reference" => Revocation(reference: "  "),
            "self review" => Revocation(reviewer: requester),
            "no reviewer" => Revocation(reviewer: Guid.Empty),
            "no requester" => Revocation(requester: Guid.Empty),
            "window too long" => Revocation(window: KeyVersionUsageGuard.EmergencyWindowMaximum + TimeSpan.FromMinutes(1)),
            "empty window" => Revocation(window: TimeSpan.Zero),
            "before the window" => Revocation(evaluatedAt: Now.AddHours(-1)),
            "after the window" => Revocation(evaluatedAt: Now.AddHours(5)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        Assert.Equal(new[] { expected }, _guard.EvaluateEmergencyRevocation(request).Refusals);
    }

    [Fact]
    public void TheWindowEndIsExclusive()
    {
        var request = Revocation(evaluatedAt: Now.AddMinutes(-10) + TimeSpan.FromHours(4));

        Assert.Equal(
            new[] { KeyGuardRefusal.OutsideElevationWindow },
            _guard.EvaluateEmergencyRevocation(request).Refusals);
    }

    [Fact]
    public void DecisionsAndRequestsAreStructurallyRendered()
    {
        Assert.Equal(nameof(KeyGuardDecision), Retire(Unreferenced()).ToString());
        Assert.Equal(nameof(KeyRetirementRequest), new KeyRetirementRequest(Old, Active, Unreferenced(), Now).ToString());
        Assert.Equal(nameof(KeyUsageEvidence), Unreferenced()[0].ToString());
        Assert.Equal(nameof(EmergencyRevocationRequest), Revocation().ToString());
        Assert.Equal(nameof(KeyVersionUsageGuard), _guard.ToString());
        Assert.DoesNotContain("kv-taxai", Retire(Unreferenced()).ToString(), StringComparison.Ordinal);
    }

    // ------------------------------- rotation over the controlled vault (AC-045)

    [Fact]
    public async Task EveryRetainedVersionStillUnwrapsAfterRepeatedRotations()
    {
        var vault = new ControlledKeyVault();
        vault.AddKey("source-envelope");
        var wrapper = vault.Wrapper("source-envelope");
        var retained = new List<(byte[] Key, WrappedContentKey Wrapped)>();

        for (var rotation = 0; rotation < 3; rotation++)
        {
            var contentKey = RandomNumberGenerator.GetBytes(32);
            retained.Add((contentKey, await wrapper.WrapAsync(contentKey, CancellationToken.None)));
            vault.Rotate("source-envelope");
        }

        Assert.Equal(3, retained.Select(item => item.Wrapped.KeyVersionId).Distinct().Count());
        foreach (var (contentKey, wrapped) in retained)
        {
            Assert.Equal(contentKey, await wrapper.UnwrapAsync(wrapped, CancellationToken.None));
        }
    }

    // ----------------------------------------------- the executable drill

    [Fact] // DEP-14 offline: the drill itself, against this build's guard.
    public void TheOfflineRotationDrillHoldsEveryCheckAndClaimsNoDeploymentEvidence()
    {
        var result = RunDrill();

        Assert.True(result.ExitCode == 0, result.Output);
        foreach (var check in new[]
                 {
                     "OldCiphertextDecryptableAfterRotation",
                     "StillRequiredVersionRetirementRefused",
                     "BackupReferencedVersionRetirementRefused",
                     "IncompleteEvidenceRetirementRefused",
                     "MissingEvidenceRetirementRefused",
                     "StaleEvidenceRetirementRefused",
                     "ActiveVersionRetirementRefused",
                     "UnusedVersionRetirementPermittedAfterRewrap",
                     "RotationTargetNinetyDays",
                     "StandingAccessRevocationRefused",
                     "UnjustifiedRevocationRefused",
                     "SelfReviewedRevocationRefused",
                     "UnboundedElevationRefused",
                 })
        {
            Assert.Contains($"{check}: HELD", result.Output, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("FAILED", result.Output, StringComparison.Ordinal);
        Assert.Contains("not deployment evidence", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("vault.azure.net", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // Live without explicit authority is unexecuted - never a pass.
    public void TheLiveDrillWithoutExplicitAuthorityIsUnexecuted()
    {
        var withoutAuthority = RunDrill(
            "-Mode", "Live",
            "-SubscriptionId", "22222222-2222-4222-8222-222222222222",
            "-KeyVaultName", "kv-taxai-prod-cc",
            "-KeyName", "source-envelope",
            "-UsageEvidencePath", "inventory.json");
        var bare = RunDrill("-Mode", "Live");

        Assert.Equal(3, withoutAuthority.ExitCode);
        Assert.Equal(3, bare.ExitCode);
        Assert.Contains("UNEXECUTED", withoutAuthority.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("HELD", withoutAuthority.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDrillRefusesToRunWithoutTheCompiledGuard()
    {
        var result = RunDrillWith(Path.Combine(Path.GetTempPath(), "missing-taxai-guard.dll"));

        Assert.Equal(2, result.ExitCode);
        Assert.DoesNotContain("HELD", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDrillNeverDisablesDeletesOrPurgesAKey()
    {
        var script = File.ReadAllText(DrillPath);
        var executable = string.Join(
            "\n",
            script.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

        foreach (var forbidden in new[] { "key', 'delete", "key', 'purge", "'set-attributes'", "--enabled false", "secret', 'delete", "secret', 'set" })
        {
            Assert.DoesNotContain(forbidden, executable, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("'keyvault', 'key', 'rotate'", executable, StringComparison.Ordinal);
        Assert.Contains("KeyVersionUsageGuard", executable, StringComparison.Ordinal);
    }

    private static readonly string DrillPath = Path.Combine(FindRepositoryRoot(), "scripts", "azure", "Invoke-KeyRotationDrill.ps1");

    private static (int ExitCode, string Output) RunDrill(params string[] arguments) =>
        RunDrillWith(typeof(KeyVersionUsageGuard).Assembly.Location, arguments);

    private static (int ExitCode, string Output) RunDrillWith(string assemblyPath, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", DrillPath, "-AssemblyPath", assemblyPath }
                     .Concat(arguments))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)), "The drill did not finish.");

        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the repository root containing TaxAI.sln.");
    }
}
