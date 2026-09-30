using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TaxAI.Portal.Tests.Production;

/// <summary>
/// Spec 016 T02 — private networking and deny-default egress (FR-05: AC-049 to
/// AC-058; DEP-06 to DEP-09).
/// <para>
/// Two executable proofs. The offline plan check refuses every mutation of the
/// network, private endpoint, private DNS, firewall and allowlist posture. The
/// network probe's evaluator decides DEP-06 to DEP-09 from observations; here
/// it is driven with synthetic observation files - a passing positive control
/// and one failing observation per rule, including a probe whose own positive
/// control was not observed. A supplied file is always reported as evaluation
/// only, never deployed proof, and live collection without explicit authority
/// never runs.
/// </para>
/// </summary>
public sealed class NetworkTemplateTests
{
    private const string Network = "modules/network.bicep";
    private const string Egress = "modules/egress.bicep";
    private const string Endpoints = "modules/private-endpoints.bicep";
    private const string Dns = "modules/private-dns.bicep";
    private const string Main = "main.bicep";
    private const string Allowlist = "contracts/dependency-allowlist.json";

    private static readonly string[] Dependencies = { "SourceStorage", "Sql", "KeyVault", "DocumentIntelligence" };

    public static TheoryData<string, string, string, string> NetworkMutations() => new()
    {
        // AC-058 — the default route goes to the firewall, or nowhere.
        { Network, "nextHopType: 'VirtualAppliance'", "nextHopType: 'Internet'", "DefaultRouteBypass" },
        { Network, "nextHopType: 'None'", "nextHopType: 'Internet'", "DefaultRouteBypass" },
        { Network, "disableBgpRoutePropagation: true", "disableBgpRoutePropagation: false", "DefaultRouteBypass" },
        { Network, "defaultOutboundAccess: false", "defaultOutboundAccess: true", "DefaultRouteBypass" },
        { Network, "          routeTable: {\n            id: egressRoutes.id\n          }\n", string.Empty, "DefaultRouteBypass" },
        { Main, "(isPrimary || activating) && profileInputsConsistent ? 'Firewall' : 'Blackhole'", "'Blackhole'", "OutboundNotDenyDefault" },
        // Spec 016 T06: the recovery host exists only by explicit activation.
        { Main, "var activating = !isPrimary && activation != null", "var activating = !isPrimary", "RecoveryActivationUnguarded" },
        { Main, "module recoveryPortal 'modules/app-service.bicep' = if (activating)", "module recoveryPortal 'modules/app-service.bicep' = if (!isPrimary)", "RecoveryActivationUnguarded" },
        { Main, "fail('The primary profile activates no recovery host.')", "true", "RecoveryActivationUnguarded" },
        { Main, "fail('The recovery host deploys into its own Canada East resource group.')", "true", "RecoveryActivationUnguarded" },
        { Egress, "var zones = location == 'canadacentral' ? [", "var zones = location == 'canadaeast' ? [", "ZoneRedundancyDisabled" },

        // AC-057/AC-058 — outbound is deny by default.
        { Network, "name: 'DenyAllOtherOutbound'\n        properties: {\n          priority: 4000\n          direction: 'Outbound'\n          access: 'Deny'", "name: 'DenyAllOtherOutbound'\n        properties: {\n          priority: 4000\n          direction: 'Outbound'\n          access: 'Allow'", "OutboundNotDenyDefault" },
        { Network, "destinationAddressPrefix: 'Internet'\n          destinationPortRanges: [\n            '80'\n            '443'\n          ]", "destinationAddressPrefix: 'Internet'\n          destinationPortRanges: [\n            '22'\n            '80'\n            '443'\n          ]", "OutboundNotDenyDefault" },
        { Network, "destinationAddressPrefix: privateEndpointSubnetPrefix\n          destinationPortRanges", "destinationAddressPrefix: 'Internet'\n          destinationPortRanges", "OutboundNotDenyDefault" },
        { Network, "direction: 'Outbound'\n          access: 'Allow'\n          protocol: 'Tcp'\n          sourceAddressPrefix: appIntegrationSubnetPrefix\n          sourcePortRange: '*'\n          destinationAddressPrefix: privateEndpointSubnetPrefix", "direction: 'Outbound'\n          access: 'Allow'\n          protocol: '*'\n          sourceAddressPrefix: appIntegrationSubnetPrefix\n          sourcePortRange: '*'\n          destinationAddressPrefix: privateEndpointSubnetPrefix", "OutboundNotDenyDefault" },

        // AC-022 — inbound to every subnet is deny by default; only the gateway listens publicly.
        { Network, "name: 'DenyAllOtherInbound'\n        properties: {\n          priority: 4000\n          direction: 'Inbound'\n          access: 'Deny'", "name: 'DenyAllOtherInbound'\n        properties: {\n          priority: 4000\n          direction: 'Inbound'\n          access: 'Allow'", "InboundNotDenyDefault" },
        { Network, "sourceAddressPrefix: gatewaySubnetPrefix", "sourceAddressPrefix: 'Internet'", "InboundNotDenyDefault" },
        { Network, "privateEndpointNetworkPolicies: 'Enabled'", "privateEndpointNetworkPolicies: 'Disabled'", "PrivateEndpointPolicyDisabled" },

        // AC-057 — the firewall admits only the reviewed allowlist, without inspection.
        { Egress, "threatIntelMode: 'Deny'", "threatIntelMode: 'Alert'", "OutboundNotDenyDefault" },
        { Egress, "terminateTLS: false", "terminateTLS: true", "TrafficInspectionConfigured" },
        { Egress, "tier: 'Standard'\n    }\n    threatIntelMode", "tier: 'Premium'\n    }\n    threatIntelMode", "TrafficInspectionConfigured" },
        { Egress, "          targetFqdns: [", "          fqdnTags: [\n            'AppServiceEnvironment'\n          ]\n          targetFqdns: [", "OutboundNotDenyDefault" },
        { Egress, "          sourceAddresses: [\n            appIntegrationSubnetPrefix\n          ]", "          sourceAddresses: [\n            '*'\n          ]", "OutboundNotDenyDefault" },
        { Egress, "loadJsonContent('../contracts/dependency-allowlist.json')", "json('{\"publicDestinations\":[]}')", "OutboundNotDenyDefault" },
        { Egress, "          ruleType: 'ApplicationRule'", "          ruleType: 'NetworkRule'", "OutboundNotDenyDefault" },
        { Egress, "output firewallPrivateAddress", "resource logs 'Microsoft.Insights/diagnosticSettings@2021-05-01' = {\n  name: 'logs'\n}\n\noutput firewallPrivateAddress", "UnsafeDiagnosticEnabled" },
        { Main, "module egress 'modules/egress.bicep' = if (isPrimary)", "module egress 'modules/egress.bicep' = if (false)", "OutboundNotDenyDefault" },

        // AC-049 to AC-052 — every binding and private DNS zone is required.
        { Endpoints, "groupId: 'account'", "groupId: 'portal'", "PrivateBindingMissing" },
        { Endpoints, "@minLength(1)\nparam documentIntelligenceResourceId string", "param documentIntelligenceResourceId string = ''", "PrivateBindingMissing" },
        { Endpoints, "privateDnsZoneId: zoneIds[binding.zone]", "privateDnsZoneId: ''", "PrivateBindingMissing" },
        { Endpoints, "groupId: 'blob'\n  zone: 'SourceStorage'\n  resourceId: accountId", "groupId: 'blob'\n  zone: 'KeyVault'\n  resourceId: accountId", "PrivateBindingMissing" },
        { Dns, "registrationEnabled: false", "registrationEnabled: true", "PrivateDnsNotBound" },
        { Dns, "  KeyVault: 'privatelink.vaultcore.azure.net'\n", string.Empty, "PrivateDnsNotBound" },
        { Main, "module privateEndpoints 'modules/private-endpoints.bicep' = if (isPrimary)", "module privateEndpoints 'modules/private-endpoints.bicep' = if (false)", "PrivateBindingMissing" },
        { Endpoints, "  properties: {\n    subnet: {", "  properties: {\n    publicNetworkAccess: 'Enabled'\n    subnet: {", "PublicEndpointConfigured" },

        // The reviewed allowlist cannot be widened.
        { Allowlist, "\"fqdn\": \"crl.microsoft.com\"", "\"fqdn\": \"*.microsoft.com\"", "AllowlistDestinationNotExact" },
        { Allowlist, "\"fqdn\": \"login.microsoftonline.com\"", "\"fqdn\": \"login.example.com\"", "AllowlistDestinationNotApproved" },
        { Allowlist, "\"fqdn\": \"crl.microsoft.com\", \"protocol\": \"Http\", \"port\": 80", "\"fqdn\": \"crl.microsoft.com\", \"protocol\": \"Https\", \"port\": 443", "AllowlistDestinationNotApproved" },
        { Allowlist, "\"fqdn\": \"ocsp.digicert.com\", \"protocol\": \"Http\", \"port\": 80 }", "\"fqdn\": \"ocsp.digicert.com\", \"protocol\": \"Http\", \"port\": 80, \"path\": \"/\" }", "AllowlistDestinationNotApproved" },
        { Allowlist, "\"fqdn\": \"crl3.digicert.com\"", "\"fqdn\": \"taxaiprodsrc.blob.core.windows.net\"", "PrivateDependencyPubliclyAllowlisted" },
        { Allowlist, "    { \"name\": \"KeyVault\", \"groupId\": \"vault\", \"zone\": \"privatelink.vaultcore.azure.net\", \"port\": 443 },\n", string.Empty, "PrivateBindingMissing" },
    };

    [Theory]
    [MemberData(nameof(NetworkMutations))]
    public void EveryNetworkSecurityMutationIsRefused(string file, string original, string mutated, string expectedCode)
    {
        var (exitCode, output) = InfrastructurePlanHarness.RunPolicyWithMutation(file, original, mutated);

        Assert.True(exitCode == 1, output);
        Assert.Contains($"{expectedCode} ({file})", output, StringComparison.Ordinal);
        InfrastructurePlanHarness.AssertEchoesNoValue(output);
    }

    [Fact] // AC-049 to AC-052: exactly the four dependencies plus the Portal, each with its zone.
    public void ThePrivateBindingsZonesAndAllowlistDescribeTheSameFourDependencies()
    {
        using var allowlist = JsonDocument.Parse(InfrastructurePlanHarness.Read(Allowlist));
        var dependencies = allowlist.RootElement.GetProperty("privateDependencies").EnumerateArray()
            .Select(item => (Name: item.GetProperty("name").GetString()!, GroupId: item.GetProperty("groupId").GetString()!))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(Dependencies.OrderBy(name => name, StringComparer.Ordinal), dependencies.Select(item => item.Name));

        var endpoints = InfrastructurePlanHarness.Read(Endpoints);
        var bindings = Regex.Matches(endpoints, @"name: '(?<name>\w+)'\s+groupId: '(?<group>\w+)'")
            .Select(match => (Name: match.Groups["name"].Value, GroupId: match.Groups["group"].Value))
            .ToArray();
        Assert.Equal(5, bindings.Length);
        foreach (var dependency in dependencies)
        {
            Assert.Contains(dependency, bindings);
        }
        Assert.Contains(("Portal", "sites"), bindings);

        var dns = InfrastructurePlanHarness.Read(Dns);
        foreach (var name in Dependencies.Append("Portal"))
        {
            Assert.Matches(new Regex($@"(?m)^\s+{name}: '"), dns);
        }
    }

    [Fact] // Spec 016 T06: activation is explicit, recovery-only and binds the same inventory.
    public void TheCanadaEastRecoveryHostIsDeployedOnlyByExplicitActivation()
    {
        var main = InfrastructurePlanHarness.Read(Main);

        foreach (var (module, file) in new[]
                 {
                     ("recoveryEgress", "egress"), ("recoveryRuntime", "runtime-configuration"),
                     ("recoveryPortal", "app-service"), ("recoveryEndpoints", "private-endpoints"),
                     ("recoveryIngress", "application-gateway"),
                 })
        {
            Assert.Matches(new Regex($@"module {module} 'modules/{file}\.bicep' = if \(activating\)"), main);
        }

        Assert.Contains("location: recoveryRegion", main, StringComparison.Ordinal);
        Assert.Contains("profile: 'CanadaEastRecovery'", main, StringComparison.Ordinal);
        Assert.Contains("sqlServerName: recoverySqlServerName", main, StringComparison.Ordinal);

        // No second Document Intelligence resource and no public data path:
        // the recovery host binds the one Canada Central resource privately.
        Assert.Single(Regex.Matches(main, @"module \w+ 'modules/document-intelligence\.bicep'"));
        Assert.Contains("'Microsoft.CognitiveServices/accounts', recoveryInputs!.runtime.documentIntelligenceName", main, StringComparison.Ordinal);
        Assert.DoesNotContain("publicNetworkAccess: 'Enabled'", main, StringComparison.Ordinal);
    }

    [Fact] // FR-05: the recovery profile is a passive foundation only.
    public void TheRecoveryProfileDeploysNoActiveTrafficOrAlternateProcessor()
    {
        var main = InfrastructurePlanHarness.Read(Main);

        foreach (var module in new[] { "egress", "runtimeConfiguration", "portal", "privateEndpoints", "ingress" })
        {
            Assert.Matches(new Regex($@"module {module} '[^']+' = if \(isPrimary\)"), main);
        }
        Assert.Matches(new Regex(@"module regionalNetwork 'modules/network\.bicep' = \{"), main);
        Assert.Contains("fail('The recovery profile deploys no primary topology.')", main, StringComparison.Ordinal);

        var recovery = InfrastructurePlanHarness.Read("parameters/recovery.example.bicepparam");
        Assert.Contains("param profile = 'recovery'", recovery, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"(?m)^param primary\b"), recovery);
        Assert.DoesNotContain("documentIntelligence", recovery, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------ network probe

    [Fact] // positive control: every rule satisfied by the supplied observations.
    public void AFullyCompliantObservationSetPassesButIsLabelledEvaluationOnly()
    {
        var (exitCode, output) = Evaluate(Compliant());

        Assert.True(exitCode == 0, output);
        foreach (var gate in new[] { "DEP-06", "DEP-07", "DEP-08", "DEP-09" })
        {
            Assert.Contains($"{gate} PASS", output, StringComparison.Ordinal);
        }
        Assert.Contains("SUPPLIED OBSERVATIONS (evaluation only - not deployed proof)", output, StringComparison.Ordinal);
        Assert.DoesNotContain("OBSERVED LIVE", output, StringComparison.Ordinal);
    }

    [Fact] // A file cannot promote itself to live evidence.
    public void AnObservationFileClaimingLiveCollectionIsStillEvaluationOnly()
    {
        var observations = Compliant();
        observations["collectedBy"] = "Live";

        var (exitCode, output) = Evaluate(observations);

        Assert.Equal(0, exitCode);
        Assert.Contains("SUPPLIED OBSERVATIONS", output, StringComparison.Ordinal);
        Assert.DoesNotContain("OBSERVED LIVE", output, StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string> ProbeFailures() => new()
    {
        // DEP-06 / AC-049-052: every worker, every dependency, private endpoint only.
        { "one-worker", "DEP-06", "FewerThanTwoWorkersObserved" },
        { "blob-public-resolution", "DEP-06", "ResolvesOutsidePrivateEndpoint (SourceStorage)" },
        { "sql-unresolved-on-second-worker", "DEP-06", "ResolutionMissing (Sql)" },
        { "vault-foreign-private-address", "DEP-06", "ResolvesOutsidePrivateEndpoint (KeyVault)" },
        { "di-endpoint-unknown", "DEP-06", "PrivateEndpointAddressUnknown (DocumentIntelligence)" },

        // DEP-07 / AC-053-056: disabled and actually denied.
        { "blob-public-enabled", "DEP-07", "PublicNetworkAccessNotDisabled (SourceStorage)" },
        { "sql-public-reachable", "DEP-07", "PublicEndpointReachable (Sql)" },
        { "vault-public-reachable", "DEP-07", "PublicEndpointReachable (KeyVault)" },
        { "di-public-enabled", "DEP-07", "PublicNetworkAccessNotDisabled (DocumentIntelligence)" },

        // DEP-08 / AC-058: application and configuration routing, deny-default.
        { "route-all-off", "DEP-08", "ApplicationRoutingNotForced" },
        { "content-share-off", "DEP-08", "ConfigurationRoutingNotForced (contentShare)" },
        { "image-pull-off", "DEP-08", "ConfigurationRoutingNotForced (imagePull)" },
        { "default-route-internet", "DEP-08", "DefaultRouteBypass" },
        { "default-route-wrong-firewall", "DEP-08", "DefaultRouteBypass" },
        { "extra-internet-route", "DEP-08", "InternetRouteBypass" },
        { "unapproved-egress-allowed", "DEP-08", "UnapprovedEgressAllowed" },
        { "unapproved-egress-not-attempted", "DEP-08", "UnapprovedEgressNotAttempted" },
        { "approved-control-not-observed", "DEP-08", "PositiveControlNotObserved" },

        // DEP-09 / AC-057: non-delivery proven only with a working detector.
        { "synthetic-delivered", "DEP-09", "SyntheticPayloadDelivered" },
        { "detector-not-proven", "DEP-09", "ReceiveDetectorNotProven" },
        { "not-sent-from-every-worker", "DEP-09", "SyntheticTransmissionNotAttemptedFromEveryWorker" },
    };

    [Theory]
    [MemberData(nameof(ProbeFailures))]
    public void EveryFailingObservationIsRefusedUnderItsGate(string scenario, string gate, string expectedCode)
    {
        var observations = Compliant();
        Apply(observations, scenario);

        var (exitCode, output) = Evaluate(observations);

        Assert.True(exitCode == 1, output);
        Assert.Contains($"{gate} REFUSED", output, StringComparison.Ordinal);
        Assert.Contains($"- {expectedCode}", output, StringComparison.Ordinal);
        foreach (var other in new[] { "DEP-06", "DEP-07", "DEP-08", "DEP-09" }.Where(candidate => candidate != gate))
        {
            if (!(scenario == "one-worker" && other is "DEP-08" or "DEP-09"))
            {
                Assert.Contains($"{other} PASS", output, StringComparison.Ordinal);
            }
        }
        InfrastructurePlanHarness.AssertEchoesNoValue(output);
    }

    [Fact] // A public probe that could not run is unexecuted, never a pass.
    public void AnUnexecutedPublicProbeIsReportedUnexecutedNotPassed()
    {
        var observations = Compliant();
        observations["publicProbe"]!["Sql"] = "Unexecuted";

        var (exitCode, output) = Evaluate(observations);

        Assert.Equal(3, exitCode);
        Assert.Contains("DEP-07 UNEXECUTED", output, StringComparison.Ordinal);
        Assert.DoesNotContain("DEP-07 PASS", output, StringComparison.Ordinal);
    }

    [Fact] // Live collection needs explicit target and synthetic-probe authority.
    public void LiveCollectionWithoutExplicitAuthorityNeverRuns()
    {
        var (exitCode, output) = InfrastructurePlanHarness.Run(
            InfrastructurePlanHarness.NetworkScript,
            "-Mode", "Live",
            "-SubscriptionId", "00000000-0000-0000-0000-000000000000",
            "-ResourceGroup", "synthetic",
            "-SiteName", "synthetic");

        Assert.Equal(2, exitCode);
        Assert.Contains("-ConfirmSyntheticProbeAuthority", output, StringComparison.Ordinal);
        Assert.DoesNotContain("PASS", output, StringComparison.Ordinal);
    }

    [Fact] // An unreadable observation file cannot pass.
    public void AnUnreadableObservationFileCannotRun()
    {
        var path = Path.Combine(Path.GetTempPath(), $"taxai-t02-observations-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "not json", new UTF8Encoding(false));
        try
        {
            var (exitCode, output) = InfrastructurePlanHarness.Run(
                InfrastructurePlanHarness.NetworkScript, "-ObservationPath", path);

            Assert.Equal(2, exitCode);
            Assert.DoesNotContain("PASS", output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact] // The probe's synthetic marker is random and never taxpayer-derived.
    public void TheLiveProbeUsesARandomSyntheticMarkerAndAnUnapprovedReceiver()
    {
        var script = File.ReadAllText(InfrastructurePlanHarness.NetworkScript);

        Assert.Equal(2, Regex.Matches(script, @"RandomNumberGenerator\]::GetBytes\(32\)").Count);
        Assert.Contains("the synthetic receiver must be an unapproved destination", script, StringComparison.Ordinal);
        Assert.Contains("ARRAffinity=$Instance", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"(?i)\bsin\b|taxpayer bytes are sent|Get-Content .*\.pdf"), script);
    }

    private static JsonObject Compliant()
    {
        var endpoint = new Dictionary<string, string>
        {
            ["SourceStorage"] = "10.40.1.68",
            ["Sql"] = "10.40.1.69",
            ["KeyVault"] = "10.40.1.70",
            ["DocumentIntelligence"] = "10.40.1.71",
        };

        JsonObject Worker(string instance) => new()
        {
            ["instance"] = instance,
            ["resolutions"] = new JsonObject(Dependencies.Select(name =>
                KeyValuePair.Create<string, JsonNode?>(name, new JsonArray(endpoint[name])))),
            ["approvedEgress"] = new JsonObject { ["connected"] = true },
            ["unapprovedEgress"] = new JsonObject { ["attempted"] = true, ["connected"] = false },
        };

        return new JsonObject
        {
            ["collectedBy"] = "Synthetic",
            ["workers"] = new JsonArray(Worker("a"), Worker("b")),
            ["privateEndpointAddresses"] = new JsonObject(Dependencies.Select(name =>
                KeyValuePair.Create<string, JsonNode?>(name, new JsonArray(endpoint[name])))),
            ["publicNetworkAccess"] = new JsonObject(Dependencies.Select(name =>
                KeyValuePair.Create<string, JsonNode?>(name, "Disabled"))),
            ["publicProbe"] = new JsonObject(Dependencies.Select(name =>
                KeyValuePair.Create<string, JsonNode?>(name, "Denied"))),
            ["routes"] = new JsonObject
            {
                ["applicationRouteAll"] = true,
                ["configurationRouting"] = new JsonObject
                {
                    ["contentShare"] = true,
                    ["imagePull"] = true,
                    ["backupRestore"] = true,
                },
                ["defaultRoute"] = new JsonObject
                {
                    ["nextHopType"] = "VirtualAppliance",
                    ["nextHopIpAddress"] = "10.40.1.132",
                },
                ["firewallPrivateAddress"] = "10.40.1.132",
                ["otherInternetRoutes"] = 0,
            },
            ["syntheticEgress"] = new JsonObject
            {
                ["positiveControlDetected"] = true,
                ["workerMarkersSent"] = new JsonArray("a", "b"),
                ["workerMarkerDetected"] = false,
            },
        };
    }

    private static void Apply(JsonObject observations, string scenario)
    {
        var workers = observations["workers"]!.AsArray();
        var routes = observations["routes"]!.AsObject();
        var synthetic = observations["syntheticEgress"]!.AsObject();

        switch (scenario)
        {
            case "one-worker":
                workers.RemoveAt(1);
                synthetic["workerMarkersSent"] = new JsonArray("a");
                break;
            case "blob-public-resolution":
                workers[0]!["resolutions"]!["SourceStorage"] = new JsonArray("20.38.1.1");
                break;
            case "sql-unresolved-on-second-worker":
                workers[1]!["resolutions"]!["Sql"] = new JsonArray();
                break;
            case "vault-foreign-private-address":
                workers[1]!["resolutions"]!["KeyVault"] = new JsonArray("10.99.0.4");
                break;
            case "di-endpoint-unknown":
                observations["privateEndpointAddresses"]!["DocumentIntelligence"] = new JsonArray();
                break;
            case "blob-public-enabled":
                observations["publicNetworkAccess"]!["SourceStorage"] = "Enabled";
                break;
            case "sql-public-reachable":
                observations["publicProbe"]!["Sql"] = "Reachable";
                break;
            case "vault-public-reachable":
                observations["publicProbe"]!["KeyVault"] = "Reachable";
                break;
            case "di-public-enabled":
                observations["publicNetworkAccess"]!["DocumentIntelligence"] = "Enabled";
                break;
            case "route-all-off":
                routes["applicationRouteAll"] = false;
                break;
            case "content-share-off":
                routes["configurationRouting"]!["contentShare"] = false;
                break;
            case "image-pull-off":
                routes["configurationRouting"]!["imagePull"] = false;
                break;
            case "default-route-internet":
                routes["defaultRoute"]!["nextHopType"] = "Internet";
                break;
            case "default-route-wrong-firewall":
                routes["defaultRoute"]!["nextHopIpAddress"] = "10.40.1.133";
                break;
            case "extra-internet-route":
                routes["otherInternetRoutes"] = 1;
                break;
            case "unapproved-egress-allowed":
                workers[1]!["unapprovedEgress"]!["connected"] = true;
                break;
            case "unapproved-egress-not-attempted":
                workers[0]!["unapprovedEgress"]!["attempted"] = false;
                break;
            case "approved-control-not-observed":
                workers[0]!["approvedEgress"]!["connected"] = false;
                break;
            case "synthetic-delivered":
                synthetic["workerMarkerDetected"] = true;
                break;
            case "detector-not-proven":
                synthetic["positiveControlDetected"] = false;
                break;
            case "not-sent-from-every-worker":
                synthetic["workerMarkersSent"] = new JsonArray("a");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
    }

    private static (int ExitCode, string Output) Evaluate(JsonObject observations)
    {
        var path = Path.Combine(Path.GetTempPath(), $"taxai-t02-observations-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, observations.ToJsonString(), new UTF8Encoding(false));
        try
        {
            return InfrastructurePlanHarness.Run(InfrastructurePlanHarness.NetworkScript, "-ObservationPath", path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
