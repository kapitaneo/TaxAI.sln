using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaxAI.Portal.Tests.Production;

/// <summary>
/// Spec 016 T06 — the executable recovery drill, the offline configuration
/// check's recovery rules and the operator runbook (DEP-17 to DEP-20).
/// Offline runs are simulations and never deployment evidence; every Live mode
/// refuses as unexecuted without explicit authority.
/// </summary>
public sealed class RecoveryToolingTests
{
    private static readonly string DrillScript = Path.Combine(
        InfrastructurePlanHarness.RepositoryRoot, "scripts", "azure", "Invoke-RecoveryDrill.ps1");

    private static readonly string CheckScript = Path.Combine(
        InfrastructurePlanHarness.RepositoryRoot, "scripts", "azure", "Test-ProductionConfiguration.ps1");

    private static readonly string Runbook = Path.Combine(
        InfrastructurePlanHarness.RepositoryRoot, "docs", "operations", "canadian-recovery.md");

    [Fact]
    public void TheOfflineDrillHoldsEveryDecisionAndSaysItIsASimulation()
    {
        var (exitCode, output) = InfrastructurePlanHarness.Run(DrillScript);

        Assert.True(exitCode == 0, output);
        Assert.Contains("SIMULATED OFFLINE DRILL", output, StringComparison.Ordinal);
        Assert.Contains("not deployment evidence", output, StringComparison.Ordinal);
        Assert.DoesNotContain("FAILED", output, StringComparison.Ordinal);
        foreach (var check in new[]
                 {
                     "MissingControlHistoryQuarantines",
                     "UnavailableLedgerQuarantines",
                     "StaleEvidenceQuarantines",
                     "MissingIntervalQuarantines",
                     "RollbackBeyondTheBackupWindowQuarantines",
                     "MembershipUncertaintyQuarantines",
                     "LedgerOutlivesTheRecoverableWindow",
                 })
        {
            Assert.Contains($"{check}: HELD", output, StringComparison.Ordinal);
        }

        InfrastructurePlanHarness.AssertEchoesNoValue(output);
    }

    [Theory]
    [InlineData("Posture")]
    [InlineData("SqlRestore")]
    [InlineData("RegionalFailover")]
    [InlineData("Attest")]
    public void EveryLiveModeWithoutAuthorityIsUnexecutedNeverPassed(string mode)
    {
        var (exitCode, output) = InfrastructurePlanHarness.Run(DrillScript, "-Mode", mode);

        Assert.Equal(3, exitCode);
        Assert.Contains("UNEXECUTED", output, StringComparison.Ordinal);
        Assert.Contains("never passed", output, StringComparison.Ordinal);
    }

    [Fact] // A destructive drill cannot run without explicit change authority.
    public void AMutatingDrillWithTargetsButNoChangeAuthorityIsUnexecuted()
    {
        var (exitCode, output) = InfrastructurePlanHarness.Run(
            DrillScript, "-Mode", "SqlRestore", "-SubscriptionId", "22222222-2222-4222-8222-222222222222",
            "-ResourceGroup", "rg-synthetic", "-SqlServerName", "sql-synthetic", "-SqlDatabaseName", "taxai");

        Assert.Equal(3, exitCode);
        Assert.Contains("-ConfirmChangeAuthority", output, StringComparison.Ordinal);
    }

    [Fact] // The drill never destroys, unlocks, overwrites or uses a shared secret.
    public void TheDrillIsCreateOnlyAndIdentityOnly()
    {
        var script = File.ReadAllText(DrillScript);

        foreach (var forbidden in new[] { "account-key", "sas-token", "connection-string", "--auth-mode', 'key", "immutability-policy', 'delete", "' 'delete'", "'db', 'delete'", "'rename'" })
        {
            Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("'--auth-mode', 'login'", script, StringComparison.Ordinal);
        Assert.Contains("'--overwrite', 'false'", script, StringComparison.Ordinal);
        Assert.Contains("[Tenancy.Models.RecoveryReleasePolicy]::EvaluatePreconditions", script, StringComparison.Ordinal);
        Assert.Contains("[TaxAI.AzureInfrastructure.Control.AzureRecoveryControlStore]::AttestationNameOf", script, StringComparison.Ordinal);
    }

    // ---------------------------------------- offline configuration check

    [Fact]
    public void TheOfflineCheckAcceptsTheRecoverySettings()
    {
        var (exitCode, output) = RunCheck(AppSettings());

        Assert.True(exitCode == 0, output);
    }

    public static TheoryData<string, string, string> RecoveryRefusals() => new()
    {
        { "TaxAi__Production__Recovery__ControlLedger__Endpoint", "https://sttaxaisrccc.blob.core.windows.net/", "StorageBindingInvalid" },
        { "TaxAi__Production__Recovery__ControlLedgerReplica__Region", "eastus", "ApprovedConstantNotMet" },
        { "TaxAi__Production__Recovery__Profile", "CanadaEastRecovery", "ApprovedConstantNotMet" },
        { "TaxAi__Production__Recovery__ControlLedgerRetentionDays", "42", "ApprovedConstantNotMet" },
        { "TaxAi__Production__Recovery__SqlPointInTimeRestoreDays", "2555", "ApprovedConstantNotMet" },
    };

    [Theory]
    [MemberData(nameof(RecoveryRefusals))]
    public void TheOfflineCheckRefusesEachUnsafeRecoverySetting(string key, string value, string refusal)
    {
        var settings = AppSettings();
        settings[key] = value;

        var (exitCode, output) = RunCheck(settings);

        Assert.Equal(1, exitCode);
        Assert.Contains(refusal, output, StringComparison.Ordinal);
        Assert.DoesNotContain(value, output.Replace(key, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- runbook

    [Fact]
    public void TheRunbookCoversEveryRecoveryPathAndItsPendingGapHonestly()
    {
        var runbook = File.ReadAllText(Runbook);

        foreach (var section in new[]
                 {
                     "Zone failure", "Point-in-time SQL restore", "Canada East regional recovery",
                     "Authorization attestation", "Quarantine reasons", "Pending plan amendment",
                 })
        {
            Assert.Contains(section, runbook, StringComparison.Ordinal);
        }

        foreach (var reason in Enum.GetNames<Tenancy.Models.RecoveryQuarantineReason>())
        {
            Assert.Contains(reason, runbook, StringComparison.Ordinal);
        }

        Assert.Contains("never", runbook, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\b\d{3}-?\d{3}-?\d{3}\b"), runbook);
    }

    // ------------------------------------------------------------- helpers

    private static Dictionary<string, string?> AppSettings()
    {
        var settings = ProductionStartupTests.ProductionProfile()
            .ToDictionary(pair => pair.Key.Replace(":", "__", StringComparison.Ordinal), pair => pair.Value);
        settings["TaxAi__Production__EvidenceManifest"] = "{\"schemaVersion\":1}";
        settings["TaxAi__Extraction__Provider"] = "AzureDocumentIntelligence";
        settings["TaxAi__Extraction__ContentReader"] = "AzureBlobSourceReader";
        settings["ConnectionStrings__TaxAiDatabase"] = ProductionStartupTests.ProductionConnectionString;
        return settings;
    }

    private static (int ExitCode, string Output) RunCheck(Dictionary<string, string?> settings)
    {
        var path = Path.Combine(Path.GetTempPath(), $"taxai-t06-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(settings));
        try
        {
            return InfrastructurePlanHarness.Run(CheckScript, "-SettingsPath", path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
