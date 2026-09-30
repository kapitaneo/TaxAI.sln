using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.Production;

/// <summary>
/// Spec 016 T01 — the production runtime contract is one contract, not four
/// that happen to agree (AC-017, AC-148, DEP-23).
/// <para>
/// The Portal's closed production key set, the IaC settings schema, the logical
/// runtime-profile schema, the Bicep module's emitted settings and the offline
/// check script are compared with each other, and the evidence schema with
/// Tenancy's closed vocabulary. The executable check is run for real, offline,
/// on synthetic settings; its configuration digest must equal the gate's.
/// </para>
/// </summary>
public sealed class RepositoryContractTests
{
    private const string AppSettingPrefix = "TaxAi__Production__";
    private const string ManifestAppSetting = AppSettingPrefix + "EvidenceManifest";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string RuntimeSchemaPath = Path.Combine(
        RepositoryRoot, "infra", "azure", "contracts", "runtime-configuration.schema.json");

    private static readonly string BicepPath = Path.Combine(
        RepositoryRoot, "infra", "azure", "modules", "runtime-configuration.bicep");

    private static readonly string CheckScriptPath = Path.Combine(
        RepositoryRoot, "scripts", "azure", "Test-ProductionConfiguration.ps1");

    private static readonly string SpecContracts = Path.Combine(
        RepositoryRoot, "specs", "016-canadian-azure-production-infrastructure", "contracts");

    // --------------------------------------------------- one closed key set

    [Fact]
    public void TheIaCSchemaDeclaresExactlyThePortalProductionKeysAndRequiresAllOfThem()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(RuntimeSchemaPath));
        var root = schema.RootElement;

        Assert.False(root.GetProperty("additionalProperties").GetBoolean());

        var declared = root.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();
        var required = root.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(declared.OrderBy(n => n, StringComparer.Ordinal), required.OrderBy(n => n, StringComparer.Ordinal));

        var productionKeys = declared
            .Where(name => name.StartsWith(AppSettingPrefix, StringComparison.Ordinal) && name != ManifestAppSetting)
            .Select(ToConfigurationKey)
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(PortalSettingNames().OrderBy(n => n, StringComparer.Ordinal), productionKeys);
        Assert.Contains(ManifestAppSetting, declared);
        Assert.Equal(
            new[] { "ConnectionStrings__TaxAiDatabase", "TaxAi__Extraction__ContentReader", "TaxAi__Extraction__Provider" },
            declared.Where(n => !n.StartsWith(AppSettingPrefix, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void TheRuntimeProfileSchemaIsTheSameClosedKeySet()
    {
        using var profile = JsonDocument.Parse(File.ReadAllText(Path.Combine(SpecContracts, "runtime-profile.schema.json")));
        var root = profile.RootElement;

        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            PortalSettingNames().Select(name => name["TaxAi:Production:".Length..]).OrderBy(n => n, StringComparer.Ordinal),
            root.GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(AppSettingPrefix + "Environment", "Production")]
    [InlineData(AppSettingPrefix + "PrimaryRegion", "canadacentral")]
    [InlineData(AppSettingPrefix + "RecoveryRegion", "canadaeast")]
    [InlineData(AppSettingPrefix + "DocumentIntelligence__ApiVersion", "2024-11-30")]
    [InlineData(AppSettingPrefix + "DocumentIntelligence__Model", "prebuilt-layout")]
    [InlineData(AppSettingPrefix + "DocumentIntelligence__Features", "keyValuePairs")]
    [InlineData(AppSettingPrefix + "SourceStorage__PublicNetworkAccess", "Disabled")]
    [InlineData(AppSettingPrefix + "Sql__PrivateEndpoint", "Required")]
    [InlineData(AppSettingPrefix + "Retention__FinalizedSourceDays", "30")]
    [InlineData(AppSettingPrefix + "Retention__StagedUploadHours", "24")]
    [InlineData(AppSettingPrefix + "Monitoring__RetentionDays", "30")]
    public void TheSchemaFixesEveryPolicyValueAsAConstant(string key, string expected)
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(RuntimeSchemaPath));

        Assert.Equal(
            expected,
            schema.RootElement.GetProperty("properties").GetProperty(key).GetProperty("const").GetString());
    }

    [Fact]
    public void TheBicepModuleEmitsEveryRequiredSettingAndNoPermissiveParameter()
    {
        var bicep = File.ReadAllText(BicepPath);
        using var schema = JsonDocument.Parse(File.ReadAllText(RuntimeSchemaPath));

        foreach (var key in schema.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()!))
        {
            var emitted = key.StartsWith(AppSettingPrefix, StringComparison.Ordinal)
                ? $"'${{production}}{key[AppSettingPrefix.Length..]}':"
                : $"{key}:";
            Assert.True(
                bicep.Contains(emitted, StringComparison.Ordinal),
                $"The module does not emit {key}.");
        }

        Assert.Contains("@allowed([\n  'canadacentral'\n])", bicep.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("@allowed([\n  'canadaeast'\n])", bicep.ReplaceLineEndings("\n"), StringComparison.Ordinal);

        var parameters = Regex.Matches(bicep, @"^param\s+(?<name>\w+)", RegexOptions.Multiline)
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        Assert.NotEmpty(parameters);
        Assert.DoesNotContain(parameters, name =>
            Regex.IsMatch(name, "enable|allow|public|egress|outbound|override|bypass|preview|apiVersion|model|retention|region$", RegexOptions.IgnoreCase) &&
            name is not "primaryRegion" and not "recoveryRegion");
        Assert.DoesNotMatch(new Regex(@"^\s*resource\s", RegexOptions.Multiline), bicep);
        Assert.DoesNotContain("preview", bicep, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------ evidence vocabulary

    [Fact]
    public void TheEvidenceSchemaIsTenancysClosedVocabularyWithNoAffirmativeDefault()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(SpecContracts, "production-evidence.schema.json")));
        var defs = schema.RootElement.GetProperty("$defs");

        Assert.Equal(
            Enumerable.Range(1, 24).Select(number => $"DEP-{number:00}"),
            EnumValues(defs.GetProperty("evidenceItem").GetProperty("properties").GetProperty("subject")));
        Assert.Equal(
            Enum.GetValues<ProductionEvidenceSubject>().Select(value => $"DEP-{(int)value:00}"),
            EnumValues(defs.GetProperty("evidenceItem").GetProperty("properties").GetProperty("subject")));
        Assert.Equal(
            Enum.GetNames<ProductionEvidenceStatus>().Where(name => name != "Absent"),
            EnumValues(defs.GetProperty("status")));
        Assert.Equal(
            Enum.GetNames<ProviderAssuranceSubject>(),
            EnumValues(defs.GetProperty("assurance").GetProperty("properties").GetProperty("subject")));
        Assert.Equal(
            Enum.GetNames<ProductionApprovalRole>(),
            EnumValues(defs.GetProperty("approval").GetProperty("properties").GetProperty("role")));
        Assert.DoesNotContain("default", File.ReadAllText(Path.Combine(SpecContracts, "production-evidence.schema.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void NoTrackedProductionContractOrToolCarriesASecret()
    {
        var files = new[] { RuntimeSchemaPath, BicepPath, CheckScriptPath }
            .Concat(Directory.EnumerateFiles(SpecContracts, "*.json"))
            .ToArray();
        var secret = new Regex(
            @"AccountKey=|SharedAccessSignature|[?&]sig=|Password\s*=|Pwd\s*=|BEGIN (RSA |EC )?PRIVATE KEY|client_secret|ClientSecret",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        Assert.Equal(5, files.Length);
        Assert.All(files, file => Assert.DoesNotMatch(secret, File.ReadAllText(file)));

        // Positive control: the scanner recognizes a planted secret shape.
        Assert.Matches(secret, "Server=x;Password=planted");
    }

    // ---------------------------------------- spec 016 T03 key protection

    [Fact] // T03 adds its IaC, contract, drill and procedure without a secret.
    public void EveryT03ContractTemplateToolAndProcedureIsSecretFree()
    {
        var files = new[]
        {
            Path.Combine(RepositoryRoot, "infra", "azure", "contracts", "data-configuration.schema.json"),
            Path.Combine(RepositoryRoot, "infra", "azure", "modules", "key-vault.bicep"),
            Path.Combine(RepositoryRoot, "infra", "azure", "modules", "key-ring-storage.bicep"),
            Path.Combine(RepositoryRoot, "infra", "azure", "modules", "sql.bicep"),
            Path.Combine(RepositoryRoot, "scripts", "azure", "Invoke-KeyRotationDrill.ps1"),
            Path.Combine(RepositoryRoot, "docs", "operations", "key-rotation.md"),
        };
        var secret = new Regex(
            @"AccountKey=|SharedAccessSignature|[?&]sig=|Password\s*=|Pwd\s*=|BEGIN (RSA |EC )?PRIVATE KEY|client_secret|ClientSecret|administratorLoginPassword|""d""\s*:\s*""|""dp""\s*:",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        Assert.All(files, file => Assert.True(File.Exists(file), file));
        Assert.All(files, file => Assert.DoesNotMatch(secret, File.ReadAllText(file)));

        // Positive controls: a private JWK exponent and a SQL password are caught.
        Assert.Matches(secret, "{\"kty\":\"RSA\",\"d\":\"planted\"}");
        Assert.Matches(secret, "Server=x;Password=planted");
    }

    [Fact] // T03 consumes the existing closed profile and adds no production setting.
    public void KeyProtectionAddsNoProductionRuntimeSetting()
    {
        var names = PortalSettingNames();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(names, name =>
            Regex.IsMatch(name, "Secret|Password|Material|Digest|Fingerprint|KeyRingBlob|Rotation", RegexOptions.IgnoreCase));
        foreach (var consumed in new[]
                 {
                     "TaxAi:Production:KeyVault:Endpoint",
                     "TaxAi:Production:SourceEnvelope:KeyUri",
                     "TaxAi:Production:DataProtection:StorageEndpoint",
                     "TaxAi:Production:DataProtection:KeyRingContainer",
                     "TaxAi:Production:DataProtection:KeyUri",
                     "TaxAi:Production:DataProtection:ApplicationDiscriminator",
                     "TaxAi:Production:ManagedIdentity:ClientId",
                 })
        {
            Assert.Contains(consumed, names);
        }
    }

    // ------------------------------------------- spec 016 T04 source storage

    [Fact] // T04 adds its IaC, contract and probe without a secret or SAS.
    public void EveryT04TemplateContractAndProbeIsSecretFree()
    {
        var files = new[]
        {
            Path.Combine(RepositoryRoot, "infra", "azure", "contracts", "data-configuration.schema.json"),
            Path.Combine(RepositoryRoot, "infra", "azure", "modules", "storage.bicep"),
            Path.Combine(RepositoryRoot, "scripts", "azure", "Test-SourceProtection.ps1"),
        };
        var secret = new Regex(
            @"AccountKey=|SharedAccessSignature|\bsig=[A-Za-z0-9%]|\bsv=\d{4}-|Password\s*=|BEGIN (RSA |EC )?PRIVATE KEY|client_secret|listKeys\(|listAccountSas\(|listServiceSas\(",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        Assert.All(files, file => Assert.True(File.Exists(file), file));
        Assert.All(files, file => Assert.DoesNotMatch(secret, File.ReadAllText(file)));

        // Positive controls: a SAS query and a listed account key are caught.
        Assert.Matches(secret, "https://x.blob.core.windows.net/c/b?sv=2025-01-05&sig=abc%2Bdef");
        Assert.Matches(secret, "var key = listKeys(account.id, '2023-05-01')");
    }

    [Fact] // T04 consumes the existing closed profile and adds no production setting.
    public void SourceStorageAddsNoProductionRuntimeSetting()
    {
        var names = PortalSettingNames();

        Assert.DoesNotContain(names, name =>
            Regex.IsMatch(name, "Container(?!.*KeyRing)|Sas|SharedKey|Purge|Snapshot|Versioning|Immutab|Block", RegexOptions.IgnoreCase) &&
            !name.EndsWith("DataProtection:KeyRingContainer", StringComparison.Ordinal));
        foreach (var consumed in new[]
                 {
                     "TaxAi:Production:SourceStorage:Endpoint",
                     "TaxAi:Production:SourceStorage:ResourceId",
                     "TaxAi:Production:SourceEnvelope:KeyUri",
                     "TaxAi:Production:ManagedIdentity:ClientId",
                 })
        {
            Assert.Contains(consumed, names);
        }
    }

    // ------------------------------- spec 016 T05 production Document Intelligence

    [Fact] // T05 adds its IaC and probe without a secret, key listing or API key.
    public void EveryT05TemplateAndProbeIsSecretFree()
    {
        var files = new[]
        {
            Path.Combine(RepositoryRoot, "infra", "azure", "main.bicep"),
            Path.Combine(RepositoryRoot, "infra", "azure", "modules", "document-intelligence.bicep"),
            Path.Combine(RepositoryRoot, "scripts", "azure", "Test-DocumentIntelligence.ps1"),
        };
        var secret = new Regex(
            @"Ocp-Apim-Subscription-Key|\bapi-key\b|listKeys\(|Password\s*=|BEGIN (RSA |EC )?PRIVATE KEY|client_secret|AccountKey=",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        Assert.All(files, file => Assert.True(File.Exists(file), file));
        Assert.All(files, file => Assert.DoesNotMatch(secret, File.ReadAllText(file)));

        // Positive controls: a key header and a listed account key are caught.
        Assert.Matches(secret, "request.Headers.Add('Ocp-Apim-Subscription-Key', $key)");
        Assert.Matches(secret, "var key = listKeys(account.id, '2024-10-01').key1");
    }

    [Fact] // T05 consumes the existing closed profile and adds no production setting.
    public void DocumentIntelligenceAddsNoProductionRuntimeSetting()
    {
        var names = PortalSettingNames();
        var intelligence = names
            .Where(name => name.StartsWith("TaxAi:Production:DocumentIntelligence:", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Exactly the T01 resource binding and GA API/model/feature triple: no
        // key, secondary, fallback, failover or alternate-processor setting.
        Assert.Equal(
            new[] { "ApiVersion", "Endpoint", "Features", "Model", "PrivateEndpoint", "PublicNetworkAccess", "Region", "ResourceId" }
                .Select(suffix => "TaxAi:Production:DocumentIntelligence:" + suffix),
            intelligence);
        Assert.DoesNotContain(names, name =>
            Regex.IsMatch(name, "ApiKey|SubscriptionKey|Fallback|Failover|Secondary|Alternate", RegexOptions.IgnoreCase));
    }

    // ------------------------------------------- the executable offline check

    [Fact]
    public void TheOfflineCheckAcceptsTheApprovedSyntheticSettingsAndItsDigestEqualsTheGates()
    {
        var settings = AppSettings();
        var result = RunCheck(settings, "-ComputeDigest");

        Assert.True(result.ExitCode == 0, result.Output);

        var digest = Regex.Match(result.Output, "CONFIGURATION DIGEST: (?<d>[0-9a-f]{64})").Groups["d"].Value;
        var gateDigest = ProductionStartupTests.Digest(new ConfigurationBuilder()
            .AddInMemoryCollection(ProductionStartupTests.ProductionProfile())
            .Build());

        Assert.Equal(gateDigest, digest);
    }

    public static TheoryData<string, string?, string> OfflineRefusals() => new()
    {
        { AppSettingPrefix + "PrimaryRegion", "eastus", "ApprovedConstantNotMet" },
        { AppSettingPrefix + "Environment", null, "RequiredSettingMissing" },
        { AppSettingPrefix + "DocumentIntelligence__ApiVersion", "2024-07-31-preview", "PreviewFeatureConfigured" },
        { AppSettingPrefix + "SourceStorage__PublicNetworkAccess", "Enabled", "ApprovedConstantNotMet" },
        { AppSettingPrefix + "Egress__AllowedHost", "https://planted-destination.example/", "UnapprovedConfigurationEntry" },
        { AppSettingPrefix + "KeyVault__Endpoint", "https://kv-other.vault.azure.net/", "DependencyBindingInvalid" },
        { AppSettingPrefix + "DataProtection__KeyUri", "https://kv-taxai-prod-cc.vault.azure.net/keys/source-envelope", "DataProtectionConfigurationMissing" },
        { AppSettingPrefix + "Retention__RejectedSourceDays", "70", "ApprovedConstantNotMet" },
        { AppSettingPrefix + "Resilience__OperationDeadline", "01:00:00", "ApprovedPatternNotMet" },
        { "ConnectionStrings__TaxAiDatabase", ProductionStartupTests.ProductionConnectionString.Replace("Encrypt=Strict", "Encrypt=False"), "ApprovedPatternNotMet" },
        { "TaxAi__Extraction__Provider", "Deterministic", "ApprovedConstantNotMet" },
    };

    [Theory] // DEP-23 for the pre-deployment check
    [MemberData(nameof(OfflineRefusals))]
    public void TheOfflineCheckRefusesEachUnsafeChangeWithoutEchoingTheValue(
        string key,
        string? value,
        string expectedCode)
    {
        var settings = AppSettings();
        if (value is null)
        {
            settings.Remove(key);
        }
        else
        {
            settings[key] = value;
        }

        var result = RunCheck(settings);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(expectedCode, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("planted", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eastus", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveModeWithoutExplicitAuthorityOrForAnotherSubscriptionIsNeverAPass()
    {
        var withoutAuthority = RunCheck(AppSettings(), "-Mode", "Live");
        Assert.Equal(2, withoutAuthority.ExitCode);

        var otherSubscription = RunCheck(
            AppSettings(),
            "-Mode", "Live",
            "-SubscriptionId", "99999999-9999-4999-8999-999999999999",
            "-ConfirmReadOnlyInventoryAccess");
        Assert.Equal(1, otherSubscription.ExitCode);
    }

    // -------------------------------------------------------------- helpers

    private static string[] PortalSettingNames() =>
        ((IReadOnlyList<string>)typeof(Program).Assembly
            .GetType("TaxAI.Portal.Production.ProductionConfiguration", throwOnError: true)!
            .GetField("SettingNames", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!).ToArray();

    private static string ToConfigurationKey(string appSetting) =>
        appSetting.Replace("__", ":", StringComparison.Ordinal);

    private static string[] EnumValues(JsonElement element) =>
        element.GetProperty("enum").EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static Dictionary<string, string?> AppSettings()
    {
        var settings = ProductionStartupTests.ProductionProfile()
            .ToDictionary(
                entry => entry.Key.Replace(":", "__", StringComparison.Ordinal),
                entry => entry.Value,
                StringComparer.Ordinal);
        settings[ManifestAppSetting] = "{\"schemaVersion\":1}";
        settings["TaxAi__Extraction__Provider"] = "AzureDocumentIntelligence";
        settings["TaxAi__Extraction__ContentReader"] = "AzureBlobSourceReader";
        settings["ConnectionStrings__TaxAiDatabase"] = ProductionStartupTests.ProductionConnectionString;
        return settings;
    }

    private static (int ExitCode, string Output) RunCheck(
        IReadOnlyDictionary<string, string?> settings,
        params string[] arguments)
    {
        var path = Path.Combine(Path.GetTempPath(), $"taxai-t01-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(settings), new UTF8Encoding(false));
        try
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
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", CheckScriptPath, "-SettingsPath", path }
                         .Concat(arguments))
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            Assert.True(process.Start());
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)), "The check did not finish.");

            return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        }
        finally
        {
            File.Delete(path);
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
