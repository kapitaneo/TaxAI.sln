using System.Text.RegularExpressions;

namespace TaxAI.Portal.Tests.Production;

/// <summary>
/// Spec 016 T06 — the recovery infrastructure-as-code (FR-10, FR-11; AC-103,
/// AC-105, AC-106, AC-108, AC-109, AC-111, AC-112).
/// <para>
/// Structural assertions over the reviewed templates; the pinned Bicep
/// compile and lint of every module and parameter file is the offline plan
/// check the existing host template suite runs.
/// </para>
/// </summary>
public sealed class RecoveryTemplateTests
{
    private static string ControlStorage => InfrastructurePlanHarness.Read("modules/control-storage.bicep");

    private static string Recovery => InfrastructurePlanHarness.Read("modules/recovery.bicep");

    private static string Sql => InfrastructurePlanHarness.Read("modules/sql.bicep");

    private static string Storage => InfrastructurePlanHarness.Read("modules/storage.bicep");

    private static string Main => InfrastructurePlanHarness.Read("main.bicep");

    // ------------------------------------------------ AC-111, AC-112: SQL backups

    [Fact]
    public void SqlPointInTimeRetentionIsThe35DayMaximumWithNoLongTermArchive()
    {
        Assert.Contains("retentionDays: 35", Sql, StringComparison.Ordinal);
        Assert.Contains("diffBackupIntervalInHours: 12", Sql, StringComparison.Ordinal);
        foreach (var period in new[] { "weeklyRetention", "monthlyRetention", "yearlyRetention" })
        {
            Assert.Contains($"{period}: 'PT0S'", Sql, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(new Regex(@"Retention: 'P\d"), Sql);
        Assert.Contains("requestedBackupStorageRedundancy: 'GeoZone'", Sql, StringComparison.Ordinal);
        Assert.Contains("zoneRedundant: true", Sql, StringComparison.Ordinal);
    }

    // ---------------------------------------- AC-109: manual Canadian failover

    [Fact]
    public void CanadaEastSqlRecoveryIsPassiveAndItsFailoverIsManual()
    {
        Assert.Contains("@allowed([\n  'canadaeast'\n])\nparam location string", Recovery, StringComparison.Ordinal);
        Assert.Contains("failoverPolicy: 'Manual'", Recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("failoverPolicy: 'Automatic'", Recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("failoverWithDataLossGracePeriodMinutes", Recovery, StringComparison.Ordinal);
        Assert.Contains("failoverPolicy: 'Disabled'", Recovery, StringComparison.Ordinal);
    }

    [Fact] // The secondary keeps the primary's security posture exactly.
    public void TheCanadaEastServerKeepsThePrimarySecurityPosture()
    {
        foreach (var control in new[]
                 {
                     "azureADOnlyAuthentication: true",
                     "publicNetworkAccess: 'Disabled'",
                     "restrictOutboundNetworkAccess: 'Enabled'",
                     "minimalTlsVersion: '1.2'",
                     "keyId: tdeKeyUriWithVersion",
                     "autoRotationEnabled: true",
                 })
        {
            Assert.Contains(control, Recovery, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("administratorLoginPassword", Recovery, StringComparison.Ordinal);
    }

    [Fact] // Recovery never deploys an alternate processor or hosting by itself.
    public void RecoveryDeploysNoDocumentIntelligenceHostingOrPublicPath()
    {
        foreach (var template in new[] { Recovery, ControlStorage })
        {
            Assert.DoesNotContain("Microsoft.CognitiveServices", template, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.Web/", template, StringComparison.Ordinal);
            Assert.DoesNotContain("applicationGateways", template, StringComparison.Ordinal);
            Assert.DoesNotContain("publicNetworkAccess: 'Enabled'", template, StringComparison.Ordinal);
        }
    }

    // ---------------------------------- AC-103: the independent control ledger

    [Fact]
    public void TheLedgerIsTwoCanadianAccountsImmutableLongerThanTheRecoverableWindow()
    {
        Assert.Contains("@allowed([\n  'canadacentral'\n])\nparam location string", ControlStorage, StringComparison.Ordinal);
        Assert.Contains("@allowed([\n  'canadaeast'\n])\nparam recoveryLocation string", ControlStorage, StringComparison.Ordinal);
        Assert.Contains("sku: 'Standard_ZRS'", ControlStorage, StringComparison.Ordinal);
        Assert.Contains("location: recoveryLocation", ControlStorage, StringComparison.Ordinal);

        var days = int.Parse(Regex.Match(ControlStorage, @"var ledgerImmutabilityDays = (?<d>\d+)").Groups["d"].Value);
        Assert.True(days > 35 + 7, "The ledger must outlive the 42-day recoverable window.");
        Assert.Equal(3, Regex.Matches(ControlStorage, "immutabilityPeriodSinceCreationInDays: ledgerImmutabilityDays").Count);
        Assert.Equal(3, Regex.Matches(ControlStorage, "allowProtectedAppendWrites: false").Count);
        Assert.DoesNotContain("allowProtectedAppendWrites: true", ControlStorage, StringComparison.Ordinal);
        Assert.DoesNotContain("objectReplicationPolicies", ControlStorage, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLedgerAccountsArePrivateEntraOnlyAndDenyByDefault()
    {
        foreach (var control in new[]
                 {
                     "allowSharedKeyAccess: false",
                     "allowBlobPublicAccess: false",
                     "allowCrossTenantReplication: false",
                     "defaultToOAuthAuthentication: true",
                     "minimumTlsVersion: 'TLS1_2'",
                     "supportsHttpsTrafficOnly: true",
                     "publicNetworkAccess: 'Disabled'",
                     "defaultAction: 'Deny'",
                     "bypass: 'None'",
                     "requireInfrastructureEncryption: true",
                 })
        {
            Assert.Contains(control, ControlStorage, StringComparison.Ordinal);
        }

        Assert.All(
            Regex.Matches(ControlStorage, @"publicAccess: '(?<v>[^']+)'").Select(match => match.Groups["v"].Value),
            value => Assert.Equal("None", value));
        Assert.Contains("privateDnsZoneId: blobZoneId", ControlStorage, StringComparison.Ordinal);
    }

    [Fact] // Least privilege: create-only ledger, read-only attestations and evidence.
    public void ThePortalMayWriteTheLedgerButOnlyReadAttestationsAndEvidence()
    {
        Assert.Contains("scope: ledgers[index]", ControlStorage, StringComparison.Ordinal);
        Assert.Contains("scope: attestations[index]", ControlStorage, StringComparison.Ordinal);
        Assert.Contains("scope: evidence[index]", ControlStorage, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(ControlStorage, @"\bscope: ").Count);
        Assert.Single(Regex.Matches(ControlStorage, "roleDefinitionId: subscriptionResourceId\\('Microsoft.Authorization/roleDefinitions', blobDataContributor\\)"));
        Assert.Equal(2, Regex.Matches(ControlStorage, "roleDefinitionId: subscriptionResourceId\\('Microsoft.Authorization/roleDefinitions', blobDataReader\\)").Count);
        Assert.Contains("ba92f5b4-2d11-453d-a403-e96b0029c9fe", ControlStorage, StringComparison.Ordinal);
        Assert.Contains("2a2b9908-6ea1-4ae2-8e65-a410df84e7d1", ControlStorage, StringComparison.Ordinal);
        Assert.DoesNotContain("b7e6dc6d-f1e8-4753-8033-0f276bb0955b", ControlStorage, StringComparison.Ordinal);
    }

    [Fact] // The containers are the ones the application names.
    public void TheTemplateContainersAreExactlyTheApplicationContainers()
    {
        foreach (var name in TaxAI.AzureInfrastructure.Configuration.ApprovedResourceProbe.ControlContainerNames)
        {
            Assert.Contains($"'{name}'", ControlStorage, StringComparison.Ordinal);
        }
    }

    // -------------------------------------------------- wiring and source store

    [Fact]
    public void TheRecoveryModulesArePrimaryOnlyAndNameTheRuntimeLedger()
    {
        Assert.Contains("module controlStorage 'modules/control-storage.bicep' = if (isPrimary)", Main, StringComparison.Ordinal);
        Assert.Contains("module sqlRecovery 'modules/recovery.bicep' = if (isPrimary)", Main, StringComparison.Ordinal);
        Assert.Contains("controlLedgerAccountName: controlLedgerCentralName", Main, StringComparison.Ordinal);
        Assert.Contains("controlLedgerReplicaAccountName: controlLedgerEastName", Main, StringComparison.Ordinal);
        Assert.Contains("centralAccountName: controlLedgerCentralName", Main, StringComparison.Ordinal);
        Assert.Contains("eastAccountName: controlLedgerEastName", Main, StringComparison.Ordinal);

        var runtime = InfrastructurePlanHarness.Read("modules/runtime-configuration.bicep");
        Assert.Contains("'${production}Recovery__Profile': 'Primary'", runtime, StringComparison.Ordinal);
        Assert.Contains("'${production}Recovery__ControlLedgerReplica__Region': recoveryRegion", runtime, StringComparison.Ordinal);
        Assert.Contains("'${production}Recovery__SqlPointInTimeRestoreDays': '35'", runtime, StringComparison.Ordinal);
    }

    [Fact] // AC-110: failover-capable, and still no retained source copy of any kind.
    public void TheSourceStoreIsFailoverCapableWithoutAnyShadowCopy()
    {
        Assert.Contains("dnsEndpointType: 'Standard'", Storage, StringComparison.Ordinal);
        Assert.Contains("name: 'Standard_GZRS'", Storage, StringComparison.Ordinal);
        Assert.Contains("isVersioningEnabled: false", Storage, StringComparison.Ordinal);
        Assert.DoesNotContain("objectReplicationPolicies", Storage, StringComparison.Ordinal);
        Assert.DoesNotContain("immutabilityPolicies", Storage, StringComparison.Ordinal);
    }

    [Fact] // The recovery parameter file stays passive.
    public void TheRecoveryProfileStillTakesNoPrimaryInputs()
    {
        var parameters = InfrastructurePlanHarness.Read("parameters/recovery.example.bicepparam");

        Assert.Contains("param profile = 'recovery'", parameters, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"(?m)^param primary\b"), parameters);
        Assert.Contains("pending plan-amendment item", parameters, StringComparison.Ordinal);
    }
}
