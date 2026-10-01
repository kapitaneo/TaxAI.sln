using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace TaxAI.Portal.Tests.Production;

/// <summary>
/// Spec 016 T02 — the hosting and ingress templates (FR-02: AC-018 to AC-024,
/// AC-154).
/// <para>
/// The real offline plan check, <c>scripts/azure/Test-ProductionPlan.ps1</c>,
/// is executed against the tracked templates (positive control) and against a
/// temporary copy in which exactly one security value is mutated. Each mutation
/// must be refused with its closed code, and no refusal may echo a configured
/// value. The compile stage is exercised by the evidence run with the pinned
/// toolchain; the solution gate stays offline and never downloads a tool.
/// </para>
/// <para>
/// This is template proof only. Running capacity, placement, listener, bypass,
/// backend-handshake and through-gateway questionnaire behaviour are deployed
/// observations recorded as pending in <c>evidence/T02.md</c>.
/// </para>
/// </summary>
public sealed class HostTemplateTests
{
    private const string AppService = "modules/app-service.bicep";
    private const string Gateway = "modules/application-gateway.bicep";
    private const string Main = "main.bicep";
    private const string ProductionExample = "parameters/production.example.bicepparam";
    private const string RecoveryExample = "parameters/recovery.example.bicepparam";

    [Fact] // positive control for every mutation below
    public void TheTrackedPlanPassesThePolicyStage()
    {
        var (exitCode, output) = InfrastructurePlanHarness.RunPolicy(InfrastructurePlanHarness.InfraRoot);

        Assert.True(exitCode == 0, output);
        Assert.Contains("Policy stage passed.", output, StringComparison.Ordinal);
        Assert.Contains("not deployment evidence", output, StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string, string> HostingMutations() => new()
    {
        // AC-018 — P1v3 only.
        { AppService, "name: 'P1v3'", "name: 'P0v3'", "PlanSkuNotP1v3" },
        { AppService, "tier: 'PremiumV3'", "tier: 'Standard'", "PlanSkuNotP1v3" },

        // AC-019 — zone-redundant placement with at least two workers.
        // Spec 016 T06: zone capability is an explicit input stated per deployment
        // role in main.bicep (primary zonal, recovery non-zonal), never a region-name test.
        { AppService, "zoneRedundant: zoneRedundant", "zoneRedundant: false", "ZoneRedundancyDisabled" },
        { AppService, "param zoneRedundant bool", "var zoneRedundant = false", "ZoneRedundancyDisabled" },
        { AppService, "param zoneRedundant bool", "var zoneRedundant = location == 'canadacentral'", "ZoneRedundancyDisabled" },
        { Main, "  primary: true\n  recovery: false", "  primary: false\n  recovery: false", "ZoneRedundancyDisabled" },
        { Main, "  primary: true\n  recovery: false", "  primary: true\n  recovery: true", "ZoneRedundancyDisabled" },
        { Main, "zoneRedundant: zoneCapability.primary\n    namePrefix: namePrefix\n    siteName: primaryInputs", "zoneRedundant: zoneCapability.recovery\n    namePrefix: namePrefix\n    siteName: primaryInputs", "ZoneRedundancyDisabled" },
        { Main, "zoneRedundant: zoneCapability.primary\n    namePrefix: namePrefix\n    siteName: primaryInputs", "zoneRedundant: false\n    namePrefix: namePrefix\n    siteName: primaryInputs", "ZoneRedundancyDisabled" },
        { AppService, "  'canadacentral'\n  'canadaeast'\n])\nparam location string", "  'canadacentral'\n  'canadaeast'\n  'eastus'\n])\nparam location string", "RegionNotApproved" },
        { AppService, "perSiteScaling: false", "perSiteScaling: true", "PerSiteScalingEnabled" },
        { AppService, "@minValue(2)\n@maxValue(10)\nparam minimumInstanceCount int", "@minValue(1)\n@maxValue(10)\nparam minimumInstanceCount int", "MinimumWorkersBelowTwo" },
        { AppService, "capacity: minimumInstanceCount", "capacity: 1", "MinimumWorkersBelowTwo" },

        // AC-020 — no autoscale rule can lower running capacity below two.
        { AppService, "minimum: string(minimumInstanceCount)", "minimum: '1'", "AutoscaleMinimumBelowTwo" },
        { AppService, "@minValue(2)\n@maxValue(10)\nparam maximumInstanceCount int", "@minValue(1)\n@maxValue(10)\nparam maximumInstanceCount int", "AutoscaleMinimumBelowTwo" },
        { AppService, "type: 'ChangeCount'\n              value: '1'\n              cooldown: 'PT20M'", "type: 'ExactCount'\n              value: '1'\n              cooldown: 'PT20M'", "AutoscaleMinimumBelowTwo" },
        { AppService, "    profiles: [\n", "    profiles: [\n      {\n        name: 'night'\n        capacity: {\n          minimum: string(1)\n        }\n        rules: []\n      }\n", "AutoscaleMinimumBelowTwo" },

        // AC-021/AC-022 — the private origin and SCM cannot be bypassed.
        { AppService, "publicNetworkAccess: 'Disabled'", "publicNetworkAccess: 'Enabled'", "PortalPublicNetworkAccessEnabled" },
        { AppService, "httpsOnly: true", "httpsOnly: false", "HttpsOnlyDisabled" },
        { AppService, "      ipSecurityRestrictionsDefaultAction: 'Deny'", "      ipSecurityRestrictionsDefaultAction: 'Allow'", "PublicOriginBypass" },
        { AppService, "      alwaysOn: true", "      alwaysOn: true\n      ipSecurityRestrictions: []", "PublicOriginBypass" },
        { AppService, "scmIpSecurityRestrictionsDefaultAction: 'Deny'", "scmIpSecurityRestrictionsDefaultAction: 'Allow'", "ScmBypass" },
        { AppService, "scmIpSecurityRestrictionsUseMain: false", "scmIpSecurityRestrictionsUseMain: true", "ScmBypass" },
        { AppService, "name: 'scm'\n  properties: {\n    allow: false", "name: 'scm'\n  properties: {\n    allow: true", "ScmBypass" },
        { AppService, "ftpsState: 'Disabled'", "ftpsState: 'AllAllowed'", "ScmBypass" },
        { AppService, "      minTlsVersion: '1.2'", "      minTlsVersion: '1.0'", "TransportBelowMinimum" },
        { AppService, "scmMinTlsVersion: '1.2'", "scmMinTlsVersion: '1.0'", "TransportBelowMinimum" },

        // FR-05 — application AND configuration traffic enter the network.
        { AppService, "vnetRouteAllEnabled: true", "vnetRouteAllEnabled: false", "ApplicationRoutingNotForced" },
        { AppService, "vnetContentShareEnabled: true", "vnetContentShareEnabled: false", "ConfigurationRoutingNotForced" },
        { AppService, "vnetImagePullEnabled: true", "vnetImagePullEnabled: false", "ConfigurationRoutingNotForced" },
        { AppService, "vnetBackupRestoreEnabled: true", "vnetBackupRestoreEnabled: false", "ConfigurationRoutingNotForced" },

        // AC-024 — the questionnaire's transport and the single trusted hop.
        { AppService, "webSocketsEnabled: true", "webSocketsEnabled: false", "InteractiveTransportDisabled" },
        { AppService, "ASPNETCORE_FORWARDEDHEADERS_ENABLED: 'false'", "ASPNETCORE_FORWARDEDHEADERS_ENABLED: 'true'", "PlatformForwardingEnabled" },

        // No payload-bearing or diagnostic logging surface.
        { AppService, "httpLoggingEnabled: false", "httpLoggingEnabled: true", "UnsafeDiagnosticEnabled" },
        { AppService, "requestTracingEnabled: false", "requestTracingEnabled: true", "UnsafeDiagnosticEnabled" },
        { AppService, "output siteId string", "resource logs 'Microsoft.Insights/diagnosticSettings@2021-05-01' = {\n  name: 'logs'\n}\n\noutput siteId string", "UnsafeDiagnosticEnabled" },
    };

    [Theory]
    [MemberData(nameof(HostingMutations))]
    public void EveryHostingSecurityMutationIsRefused(string file, string original, string mutated, string expectedCode) =>
        AssertRefused(file, original, mutated, expectedCode);

    public static TheoryData<string, string, string, string> IngressMutations() => new()
    {
        // AC-021 — WAF v2, enforcing, public HTTPS 443 only.
        { Gateway, "name: 'WAF_v2'\n      tier: 'WAF_v2'", "name: 'Standard_v2'\n      tier: 'Standard_v2'", "GatewayNotWafV2" },
        { Gateway, "mode: 'Prevention'", "mode: 'Detection'", "WafNotEnforcing" },
        { Gateway, "state: 'Enabled'", "state: 'Disabled'", "WafNotEnforcing" },
        { Gateway, "requestBodyCheck: true", "requestBodyCheck: false", "WafNotEnforcing" },
        { Gateway, "    managedRules: {", "    customRules: []\n    managedRules: {", "WafNotEnforcing" },
        { Gateway, "minCapacity: minimumCapacity", "minCapacity: 0", "GatewayMinimumBelowTwo" },
        { Gateway, "@minValue(2)\n@maxValue(10)\nparam minimumCapacity int", "@minValue(0)\n@maxValue(10)\nparam minimumCapacity int", "GatewayMinimumBelowTwo" },
        { Gateway, "        name: 'https'\n        properties: {\n          port: 443", "        name: 'https'\n        properties: {\n          port: 80", "PublicListenerNotHttps443Only" },
        { Gateway, "    sslCertificates: [", "    redirectConfigurations: []\n    sslCertificates: [", "PublicListenerNotHttps443Only" },
        { Gateway, "        name: 'public'\n        properties: {\n", "        name: 'public'\n        properties: {\n          privateIPAllocationMethod: 'Dynamic'\n", "PublicListenerNotHttps443Only" },
        { Gateway, "protocol: 'Https'\n          hostName: publicHostName\n          requireServerNameIndication: true", "protocol: 'Http'\n          hostName: publicHostName\n          requireServerNameIndication: true", "PlaintextForwardingConfigured" },
        { Gateway, "zones: zones\n  identity", "zones: []\n  identity", "ZoneRedundancyDisabled" },
        { Gateway, "var zones = zoneRedundant ? [", "var zones = location == 'canadacentral' ? [", "ZoneRedundancyDisabled" },
        { Gateway, "var zones = zoneRedundant ? [", "var zones = false ? [", "ZoneRedundancyDisabled" },
        { Main, "zoneRedundant: zoneCapability.primary\n    namePrefix: namePrefix\n    gatewaySubnetId", "zoneRedundant: zoneCapability.recovery\n    namePrefix: namePrefix\n    gatewaySubnetId", "ZoneRedundancyDisabled" },
        { Gateway, "  'canadacentral'\n  'canadaeast'\n])\nparam location string", "  'canadacentral'\n  'westeurope'\n])\nparam location string", "RegionNotApproved" },

        // AC-023 — the backend leg is certificate- and host-name-validated HTTPS.
        { Gateway, "pickHostNameFromBackendAddress: false", "pickHostNameFromBackendAddress: true", "BackendNotCertificateValidated" },
        { Gateway, "pickHostNameFromBackendAddress: false", "pickHostNameFromBackendAddress: false\n          validateCertChainAndExpiry: false", "BackendNotCertificateValidated" },
        { Gateway, "pickHostNameFromBackendAddress: false", "pickHostNameFromBackendAddress: false\n          validateSNI: false", "BackendNotCertificateValidated" },
        { Gateway, "    sslCertificates: [", "    trustedRootCertificates: []\n    sslCertificates: [", "BackendNotCertificateValidated" },
        { Gateway, "policyName: 'AppGwSslPolicy20220101S'", "policyName: 'AppGwSslPolicy20150501'", "TransportBelowMinimum" },
        { Gateway, "          port: 443\n          hostName: publicHostName", "          port: 8443\n          hostName: publicHostName", "PublicListenerNotHttps443Only" },

        // AC-154 — plaintext HTTP forwarding from gateway to App Service.
        { Gateway, "protocol: 'Https'\n          port: 443\n          hostName: publicHostName", "protocol: 'Http'\n          port: 443\n          hostName: publicHostName", "PlaintextForwardingConfigured" },
        { Gateway, "protocol: 'Https'\n          host: publicHostName", "protocol: 'Http'\n          host: publicHostName", "PlaintextForwardingConfigured" },

        // No gateway diagnostic category is enabled.
        { Gateway, "output gatewayId string", "resource logs 'Microsoft.Insights/diagnosticSettings@2021-05-01' = {\n  name: 'logs'\n}\n\noutput gatewayId string", "UnsafeDiagnosticEnabled" },
    };

    [Theory]
    [MemberData(nameof(IngressMutations))]
    public void EveryIngressSecurityMutationIsRefused(string file, string original, string mutated, string expectedCode) =>
        AssertRefused(file, original, mutated, expectedCode);

    public static TheoryData<string, string, string, string> ParameterMutations() => new()
    {
        { ProductionExample, "minimumInstanceCount: 2", "minimumInstanceCount: 1", "MinimumWorkersBelowTwo" },
        { ProductionExample, "minimumCapacity: 2", "minimumCapacity: 1", "MinimumWorkersBelowTwo" },
        { ProductionExample, "param primaryRegion = 'canadacentral'", "param primaryRegion = 'eastus'", "RegionNotApproved" },
        { ProductionExample, "evidenceManifest: '{\"schemaVersion\":1}'", "evidenceManifest: '{\"schemaVersion\":1,\"items\":[]}'", "AffirmativeExampleEvidence" },
        { RecoveryExample, "param recoveryRegion = 'canadaeast'", "param recoveryRegion = 'westus'", "RegionNotApproved" },
        { RecoveryExample, "param namePrefix = 'taxai-dr'", "param namePrefix = 'taxai-dr'\nparam primary = {}", "RecoveryProfileActive" },
        { Main, "@allowed([\n  'canadacentral'\n])\nparam primaryRegion string", "@allowed([\n  'canadacentral'\n  'eastus'\n])\nparam primaryRegion string", "RegionNotApproved" },
        { Main, "@sealed()\ntype hostingSettings", "type hostingSettings", "PrimaryInputsNotClosed" },
        { Main, "var isPrimary", "var channel = 'preview'\nvar isPrimary", "PreviewFeatureConfigured" },
    };

    [Theory]
    [MemberData(nameof(ParameterMutations))]
    public void EveryParameterAndEntryPointMutationIsRefused(string file, string original, string mutated, string expectedCode) =>
        AssertRefused(file, original, mutated, expectedCode);

    [Fact] // AC-018 to AC-020: the bounds are types, not conventions.
    public void EveryCapacityBoundIsAtLeastTwoInTheTypesAndEveryParameterFile()
    {
        var main = InfrastructurePlanHarness.Read(Main);
        Assert.Equal(
            4,
            Regex.Matches(main, @"@minValue\(2\)\s+@maxValue\(\d+\)\s+(?:minimumInstanceCount|maximumInstanceCount|minimumCapacity|maximumCapacity): int").Count);

        foreach (var file in new[] { ProductionExample, "parameters/synthetic.bicepparam" })
        {
            var values = Regex.Matches(
                    InfrastructurePlanHarness.Read(file),
                    @"(?:minimumInstanceCount|maximumInstanceCount|minimumCapacity|maximumCapacity): (?<n>\d+)")
                .Select(match => int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            Assert.Equal(4, values.Length);
            Assert.All(values, value => Assert.True(value >= 2));
        }
    }

    [Fact] // AC-021, AC-023, AC-154: one listener, one HTTPS backend, no redirect.
    public void TheGatewayHasExactlyOneHttpsListenerAndOneHttpsBackend()
    {
        var gateway = InfrastructurePlanHarness.Read(Gateway);

        Assert.Single(Regex.Matches(gateway, @"httpListeners: \["));
        Assert.Single(Regex.Matches(gateway, @"backendHttpSettingsCollection: \["));
        Assert.Single(Regex.Matches(gateway, @"requestRoutingRules: \["));
        Assert.Equal(new[] { "443", "443" }, Regex.Matches(gateway, @"\bport: (\d+)").Select(match => match.Groups[1].Value).ToArray());
        Assert.DoesNotContain("protocol: 'Http'\n", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("redirectConfigurations", gateway, StringComparison.Ordinal);
        Assert.Contains("requireServerNameIndication: true", gateway, StringComparison.Ordinal);
    }

    [Fact] // The example evidence grants nothing, and nothing tracked is a secret.
    public void NoParameterFileCarriesASecretOrAffirmativeEvidence()
    {
        foreach (var file in new[] { ProductionExample, RecoveryExample, "parameters/synthetic.bicepparam" })
        {
            var text = InfrastructurePlanHarness.Read(file);
            Assert.DoesNotMatch(new Regex(@"(?i)password|secret\s*=|accountkey|sharedaccess|sig=|BEGIN [A-Z ]*PRIVATE"), text);
            Assert.DoesNotMatch(new Regex(@"(?i)""(?:passed|approved|satisfied)"""), text);
        }
        Assert.Contains("example.invalid", InfrastructurePlanHarness.Read(ProductionExample), StringComparison.Ordinal);
    }

    [Fact] // The toolchain is pinned to one exact version, with a hash per platform.
    public void TheBicepToolchainIsPinnedWithAnIntegrityHashPerPlatform()
    {
        using var toolchain = System.Text.Json.JsonDocument.Parse(InfrastructurePlanHarness.Read("contracts/toolchain.json"));
        var bicep = toolchain.RootElement.GetProperty("bicep");

        Assert.Matches(@"^\d+\.\d+\.\d+$", bicep.GetProperty("version").GetString());
        Assert.Equal("https://api.nuget.org/v3-flatcontainer", bicep.GetProperty("source").GetString());
        var packages = bicep.GetProperty("packages").EnumerateObject().ToArray();
        Assert.Contains(packages, package => package.Name == "win-x64");
        Assert.Contains(packages, package => package.Name == "linux-x64");
        Assert.All(packages, package => Assert.Matches("^[0-9a-f]{128}$", package.Value.GetString()));

        var script = File.ReadAllText(InfrastructurePlanHarness.PlanScript);
        Assert.Contains("Get-FileHash -LiteralPath $archive -Algorithm SHA512", script, StringComparison.Ordinal);
        Assert.Contains("'ToolchainIntegrityFailed'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-Command bicep", script, StringComparison.Ordinal);
        Assert.DoesNotContain("az bicep", script, StringComparison.Ordinal);
    }

    [Fact] // The linter treats every relevant rule as an error, and no preview feature is on.
    public void TheBicepLinterConfigurationIsStrict()
    {
        using var configuration = System.Text.Json.JsonDocument.Parse(InfrastructurePlanHarness.Read("bicepconfig.json"));
        var rules = configuration.RootElement.GetProperty("analyzers").GetProperty("core").GetProperty("rules");

        foreach (var rule in new[] { "no-hardcoded-env-urls", "secure-secrets-in-params", "outputs-should-not-contain-secrets", "no-hardcoded-location", "no-unused-params" })
        {
            Assert.Equal("error", rules.GetProperty(rule).GetProperty("level").GetString());
        }
        Assert.Empty(configuration.RootElement.GetProperty("experimentalFeaturesEnabled").EnumerateObject());
    }

    private static void AssertRefused(string file, string original, string mutated, string expectedCode)
    {
        var (exitCode, output) = InfrastructurePlanHarness.RunPolicyWithMutation(file, original, mutated);

        Assert.True(exitCode == 1, output);
        Assert.Contains("INFRASTRUCTURE PLAN REFUSED", output, StringComparison.Ordinal);
        Assert.Contains($"{expectedCode} ({file})", output, StringComparison.Ordinal);
        InfrastructurePlanHarness.AssertEchoesNoValue(output);
    }
}

/// <summary>
/// Runs the tracked offline infrastructure scripts, optionally against a
/// temporary copy of <c>infra/azure</c> with exactly one mutation applied.
/// </summary>
internal static class InfrastructurePlanHarness
{
    internal static readonly string RepositoryRoot = FindRepositoryRoot();

    internal static readonly string InfraRoot = Path.Combine(RepositoryRoot, "infra", "azure");

    internal static readonly string PlanScript = Path.Combine(RepositoryRoot, "scripts", "azure", "Test-ProductionPlan.ps1");

    internal static readonly string NetworkScript = Path.Combine(RepositoryRoot, "scripts", "azure", "Test-PrivateNetwork.ps1");

    internal static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(InfraRoot, relativePath)).ReplaceLineEndings("\n");

    internal static (int ExitCode, string Output) RunPolicy(string infraRoot) =>
        Run(PlanScript, "-InfraRoot", infraRoot, "-Stage", "Policy");

    internal static (int ExitCode, string Output) RunPolicyWithMutation(string file, string original, string mutated)
    {
        var copy = Path.Combine(Path.GetTempPath(), $"taxai-t02-plan-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(InfraRoot, copy);
            var path = Path.Combine(copy, file);
            var text = File.ReadAllText(path).ReplaceLineEndings("\n");
            Assert.True(
                text.Contains(original, StringComparison.Ordinal),
                $"The mutation anchor was not found in {file}: the mutation would prove nothing.");
            var index = text.IndexOf(original, StringComparison.Ordinal);
            File.WriteAllText(
                path,
                string.Concat(text.AsSpan(0, index), mutated, text.AsSpan(index + original.Length)),
                new UTF8Encoding(false));

            return RunPolicy(copy);
        }
        finally
        {
            if (Directory.Exists(copy))
            {
                Directory.Delete(copy, recursive: true);
            }
        }
    }

    internal static (int ExitCode, string Output) Run(string script, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script }.Concat(arguments))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)), "The check did not finish.");

        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    /// <summary>
    /// Refusal output names closed codes and tracked file paths only.
    /// </summary>
    internal static void AssertEchoesNoValue(string output)
    {
        Assert.DoesNotMatch(new Regex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b"), output);
        Assert.DoesNotContain("example.invalid", output, StringComparison.Ordinal);
        Assert.DoesNotContain("taxai-prod", output, StringComparison.Ordinal);
        Assert.DoesNotContain("microsoftonline", output, StringComparison.Ordinal);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static string FindRepositoryRoot()
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
