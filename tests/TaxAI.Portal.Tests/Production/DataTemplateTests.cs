using System.Text.Json;
using System.Text.RegularExpressions;
using TaxAI.AzureInfrastructure.Content;
using TaxAI.AzureInfrastructure.Keys;
using TaxAI.AzureInfrastructure.Protection;

namespace TaxAI.Portal.Tests.Production;

/// <summary>
/// Spec 016 T03 — the key, customer-managed-key, SQL and key-ring templates
/// (FR-04; AC-041, AC-042, AC-043, AC-044, AC-048).
/// <para>
/// The closed contract <c>infra/azure/contracts/data-configuration.schema.json</c>
/// fixes every posture value as a constant. These tests prove the Bicep modules
/// declare exactly those values, that the code consumes the same names and
/// rotation target, and that a template posture checker refuses each unsafe
/// variant. Each rule is proved by mutating the real template text in memory and
/// observing that rule's own refusal, and the unmodified templates are the
/// positive control. Compiling the templates with the pinned Bicep toolchain is
/// recorded separately in the T03 evidence, as for T02.
/// </para>
/// <para>
/// Passing these checks is not deployment evidence: the live posture is
/// observed by the rotation drill against the exact resources (DEP-14).
/// </para>
/// <para>
/// Spec 016 T04 adds the source store (storage.bicep; AC-025, AC-027, AC-028,
/// AC-029, AC-040) and the envelope contract: GPv2 GZRS, the source-Blob CMK
/// through a dedicated identity, Entra-only, private, one non-public container,
/// and every shadow-retention mechanism off. Each rule is again proved by its own
/// mutation, and the envelope constants are the code's own.
/// </para>
/// </summary>
public sealed class DataTemplateTests
{
    private const string KeyVault = "modules/key-vault.bicep";
    private const string KeyRing = "modules/key-ring-storage.bicep";
    private const string Sql = "modules/sql.bicep";
    private const string Storage = "modules/storage.bicep";
    private const string Main = "main.bicep";

    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string InfraRoot = Path.Combine(RepositoryRoot, "infra", "azure");
    private static readonly string SchemaPath = Path.Combine(InfraRoot, "contracts", "data-configuration.schema.json");

    // ------------------------------------------------------- the contract

    [Fact]
    public void TheContractIsClosedAtEveryLevelAndHasNoDefault()
    {
        var text = File.ReadAllText(SchemaPath);
        using var schema = JsonDocument.Parse(text);
        var root = schema.RootElement;

        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        foreach (var section in new[] { "keyVault", "keyRingStorage", "sql", "sourceStorage", "sourceEnvelope" })
        {
            var node = root.GetProperty("properties").GetProperty(section);
            Assert.False(node.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(
                node.GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal),
                node.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).OrderBy(n => n, StringComparer.Ordinal));
            Assert.All(
                node.GetProperty("properties").EnumerateObject(),
                property => Assert.True(property.Value.TryGetProperty("const", out _), $"{section}.{property.Name} is not fixed."));
        }

        Assert.False(root.GetProperty("$defs").GetProperty("key").GetProperty("additionalProperties").GetBoolean());
        Assert.DoesNotContain("\"default\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("preview", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // AC-044
    public void TheContractRotationTargetIsTheGuardsNinetyDays()
    {
        var target = Const("$defs", "key", "properties", "rotateAfterCreate").GetString();

        Assert.True(KeyVersionUsageGuard.MeetsRotationTarget(target));
        Assert.Contains($"timeAfterCreate: '{target}'", Template(KeyVault), StringComparison.Ordinal);
    }

    [Fact]
    public void TheContractNamesTheSameSecretsBlobAndUsageSourcesAsTheCode()
    {
        Assert.Equal(
            new[] { KeyVaultRuntimeMaterialProvider.DigestKeySecretName, KeyVaultRuntimeMaterialProvider.FingerprintKeySecretName },
            Const("properties", "runtimeSecrets").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(
            AzureDataProtectionConfiguration.KeyRingBlobName,
            Const("properties", "keyRingStorage", "properties", "keyRingBlobName").GetString());

        using var schema = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        Assert.Equal(
            Enum.GetNames<KeyUsageSource>(),
            schema.RootElement.GetProperty("$defs").GetProperty("keyUsageEvidence").GetProperty("properties")
                .GetProperty("source").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));

        var vault = Template(KeyVault);
        Assert.Contains($"'{KeyVaultRuntimeMaterialProvider.DigestKeySecretName}'", vault, StringComparison.Ordinal);
        Assert.Contains($"'{KeyVaultRuntimeMaterialProvider.FingerprintKeySecretName}'", vault, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> DeclaredConstants() => new()
    {
        // AC-042, AC-043: RBAC, soft delete with purge protection, private.
        { KeyVault, "name: 'premium'" },
        { KeyVault, "enableRbacAuthorization: true" },
        { KeyVault, "enableSoftDelete: true" },
        { KeyVault, "softDeleteRetentionInDays: 90" },
        { KeyVault, "enablePurgeProtection: true" },
        { KeyVault, "publicNetworkAccess: 'Disabled'" },
        { KeyVault, "defaultAction: 'Deny'" },
        { KeyVault, "kty: 'RSA-HSM'" },
        { KeyVault, "keySize: 3072" },
        { KeyVault, "exportable: false" },
        { KeyVault, "timeAfterCreate: 'P90D'" },
        { KeyRing, "name: 'Standard_GZRS'" },
        { KeyRing, "allowSharedKeyAccess: false" },
        { KeyRing, "allowBlobPublicAccess: false" },
        { KeyRing, "minimumTlsVersion: 'TLS1_2'" },
        { KeyRing, "publicNetworkAccess: 'Disabled'" },
        { KeyRing, "requireInfrastructureEncryption: true" },
        { KeyRing, "isVersioningEnabled: true" },
        { KeyRing, "days: 30" },
        // AC-041, AC-048: CMK TDE, Entra-only, TLS 1.2+, private.
        { Sql, "minimalTlsVersion: '1.2'" },
        { Sql, "publicNetworkAccess: 'Disabled'" },
        { Sql, "restrictOutboundNetworkAccess: 'Enabled'" },
        { Sql, "azureADOnlyAuthentication: true" },
        { Sql, "serverKeyType: 'AzureKeyVault'" },
        { Sql, "autoRotationEnabled: true" },
        { Sql, "zoneRedundant: true" },
        // T04, AC-025: GPv2 GZRS; AC-028: Entra-only, private; AC-029: no shadow
        // retention; AC-040: the source-Blob CMK.
        { Storage, "kind: 'StorageV2'" },
        { Storage, "name: 'Standard_GZRS'" },
        { Storage, "allowSharedKeyAccess: false" },
        { Storage, "allowBlobPublicAccess: false" },
        { Storage, "publicNetworkAccess: 'Disabled'" },
        { Storage, "keySource: 'Microsoft.Keyvault'" },
        { Storage, "isVersioningEnabled: false" },
        { Storage, "publicAccess: 'None'" },
    };

    [Theory]
    [MemberData(nameof(DeclaredConstants))]
    public void EachModuleDeclaresTheContractValue(string module, string declaration)
    {
        Assert.Contains(declaration, Template(module), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryContractConstantIsDeclaredByItsModule()
    {
        var sections = new (string Section, string Module, Dictionary<string, string> Spelling)[]
        {
            ("keyVault", KeyVault, new() { ["sku"] = "name", ["networkDefaultAction"] = "defaultAction", ["location"] = "location" }),
            ("keyRingStorage", KeyRing, new() { ["sku"] = "name", ["deleteRetentionDays"] = "days", ["keyRingBlobName"] = "-", ["location"] = "location" }),
            ("sql", Sql, new() { ["encryptionProtectorType"] = "serverKeyType", ["tdeKeyName"] = "-", ["location"] = "location" }),
            ("sourceStorage", Storage, new()
            {
                ["sku"] = "name",
                ["location"] = "location",
                ["networkDefaultAction"] = "defaultAction",
                ["networkBypass"] = "bypass",
                ["containerPublicAccess"] = "publicAccess",
                // Asserted by the posture rules below, not by one literal each.
                ["cmkKeyName"] = "-",
                ["deleteRetentionEnabled"] = "-",
                ["containerDeleteRetentionEnabled"] = "-",
                ["changeFeedEnabled"] = "-",
                ["restorePolicyEnabled"] = "-",
                ["containerName"] = "-",
            }),
        };

        foreach (var (section, module, spelling) in sections)
        {
            var template = Template(module);
            foreach (var property in Const("properties", section, "properties").EnumerateObject())
            {
                var name = spelling.TryGetValue(property.Name, out var mapped) ? mapped : property.Name;
                if (name == "-")
                {
                    continue;
                }

                var value = property.Value.GetProperty("const");
                var literal = value.ValueKind switch
                {
                    JsonValueKind.String when name == "location" => $"'{value.GetString()}'",
                    JsonValueKind.String => $"{name}: '{value.GetString()}'",
                    _ => $"{name}: {value.GetRawText()}",
                };

                Assert.True(
                    template.Contains(literal, StringComparison.Ordinal),
                    $"{module} does not declare {section}.{property.Name} as {literal}.");
            }
        }

        Assert.Contains(
            $"var sqlTdeKeyName = '{Const("properties", "sql", "properties", "tdeKeyName").GetString()}'",
            Template(KeyVault),
            StringComparison.Ordinal);
        Assert.Contains(
            $"var sourceBlobKeyName = '{Const("properties", "sourceStorage", "properties", "cmkKeyName").GetString()}'",
            Template(KeyVault),
            StringComparison.Ordinal);
        Assert.Contains(
            $"var sourceContainerName = '{Const("properties", "sourceStorage", "properties", "containerName").GetString()}'",
            Template(Storage),
            StringComparison.Ordinal);
    }

    [Fact] // T04: the envelope contract is exactly what the code implements.
    public void TheEnvelopeContractIsTheCodesOwnConstants()
    {
        string Envelope(string name) => Const("properties", "sourceEnvelope", "properties", name).ToString();

        Assert.Equal("AES-256-GCM", Envelope("cipher"));
        Assert.Equal(SourceEnvelopeCodec.DataKeySize.ToString(System.Globalization.CultureInfo.InvariantCulture), Envelope("dataKeyBytes"));
        Assert.Equal(SourceEnvelopeCodec.ChunkSize.ToString(System.Globalization.CultureInfo.InvariantCulture), Envelope("chunkSizeBytes"));
        Assert.Equal(SourceEnvelopeCodec.TagSize.ToString(System.Globalization.CultureInfo.InvariantCulture), Envelope("tagSizeBytes"));
        Assert.Equal(KeyVaultContentKeyWrapper.Algorithm.ToString(), Envelope("keyWrapAlgorithm"));
        Assert.Equal(AzureIntakeContentStore.BlockSize.ToString(System.Globalization.CultureInfo.InvariantCulture), Envelope("stagedBlockBytes"));
        Assert.StartsWith(Envelope("contentObjectPrefix"), SourceEnvelopeCodec.ContentObjectName(Guid.NewGuid()), StringComparison.Ordinal);
        Assert.StartsWith(Envelope("keyObjectPrefix"), SourceEnvelopeCodec.KeyObjectName(Guid.NewGuid()), StringComparison.Ordinal);
        Assert.Equal(AzureIntakeContentStore.SourceContainerName, Const("properties", "sourceStorage", "properties", "containerName").GetString());
        Assert.Equal("If-None-Match: *", Envelope("uploadCondition"));
    }

    [Fact] // T04: the private endpoint binds exactly the account the module creates.
    public void TheSourcePrivateEndpointBindsTheDeployedSourceAccount()
    {
        var main = Template(Main);

        Assert.Contains("accountName: primaryInputs!.runtime.sourceStorageAccountName", main, StringComparison.Ordinal);
        Assert.Contains("sourceStorageResourceId: resourceId('Microsoft.Storage/storageAccounts', primaryInputs!.runtime.sourceStorageAccountName)", main, StringComparison.Ordinal);
        Assert.Contains("sourceBlobKeyName: keyVault!.outputs.sourceBlobKeyName", main, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Network/privateEndpoints", Template(Storage), StringComparison.Ordinal);
    }

    // ------------------------------------------- posture checker (mutations)

    [Fact] // Positive control: the tracked templates satisfy every rule.
    public void TheTrackedTemplatesSatisfyEveryPostureRule()
    {
        Assert.Empty(PostureViolations(TrackedSources()));
    }

    public static TheoryData<string, string, string, string> UnsafeMutations() => new()
    {
        { KeyVault, "enableRbacAuthorization: true", "enableRbacAuthorization: false", "VaultNotRbac" },
        { KeyVault, "enablePurgeProtection: true", "enablePurgeProtection: false", "PurgeProtectionDisabled" },
        { KeyVault, "softDeleteRetentionInDays: 90", "softDeleteRetentionInDays: 7", "SoftDeleteRetentionShortened" },
        { KeyVault, "    publicNetworkAccess: 'Disabled'\n    networkAcls: {\n      defaultAction: 'Deny'", "    publicNetworkAccess: 'Enabled'\n    networkAcls: {\n      defaultAction: 'Deny'", "PublicEndpointConfigured" },
        { KeyVault, "      defaultAction: 'Deny'\n      bypass: 'AzureServices'", "      defaultAction: 'Allow'\n      bypass: 'AzureServices'", "FirewallNotDenyDefault" },
        { KeyVault, "exportable: false", "exportable: true", "KeyExportable" },
        { KeyVault, "timeAfterCreate: 'P90D'", "timeAfterCreate: 'P365D'", "RotationTargetNotNinetyDays" },
        { KeyVault, "kty: 'RSA-HSM'", "kty: 'RSA'", "KeyNotHsmBacked" },
        { KeyVault, "keySize: 3072", "keySize: 2048", "KeySizeBelowMinimum" },
        { KeyVault, "      'wrapKey'\n      'unwrapKey'\n    ]", "      'wrapKey'\n      'unwrapKey'\n      'decrypt'\n    ]", "KeyOperationsWidened" },
        { KeyVault, "enabledForTemplateDeployment: false", "enabledForTemplateDeployment: true", "VaultAccessWidened" },
        { KeyVault, "scope: keys[index]", "scope: vault", "RoleNotLeastPrivilege" },
        { KeyVault, "resource intakeSecrets 'Microsoft.KeyVault/vaults/secrets@2023-07-01' existing =", "resource intakeSecrets 'Microsoft.KeyVault/vaults/secrets@2023-07-01' =", "SecretValueInTemplate" },
        { KeyRing, "allowSharedKeyAccess: false", "allowSharedKeyAccess: true", "SharedKeyAccessEnabled" },
        { KeyRing, "allowBlobPublicAccess: false", "allowBlobPublicAccess: true", "PublicEndpointConfigured" },
        { KeyRing, "    publicNetworkAccess: 'Disabled'", "    publicNetworkAccess: 'Enabled'", "PublicEndpointConfigured" },
        { KeyRing, "minimumTlsVersion: 'TLS1_2'", "minimumTlsVersion: 'TLS1_0'", "TransportBelowMinimum" },
        { KeyRing, "supportsHttpsTrafficOnly: true", "supportsHttpsTrafficOnly: false", "TransportBelowMinimum" },
        { KeyRing, "isVersioningEnabled: true", "isVersioningEnabled: false", "KeyRingRecoveryDisabled" },
        { KeyRing, "name: 'Standard_GZRS'", "name: 'Standard_LRS'", "KeyRingNotGeoRedundant" },
        { KeyRing, "scope: container", "scope: account", "RoleNotLeastPrivilege" },
        { KeyRing, "privateDnsZoneId: blobZoneId", "privateDnsZoneId: ''", "PrivateBindingMissing" },
        { Sql, "azureADOnlyAuthentication: true", "azureADOnlyAuthentication: false", "SqlLoginsEnabled" },
        { Sql, "minimalTlsVersion: '1.2'", "minimalTlsVersion: '1.0'", "TransportBelowMinimum" },
        { Sql, "    publicNetworkAccess: 'Disabled'", "    publicNetworkAccess: 'Enabled'", "PublicEndpointConfigured" },
        { Sql, "serverKeyType: 'AzureKeyVault'\n    serverKeyName", "serverKeyType: 'ServiceManaged'\n    serverKeyName", "TdeNotCustomerManaged" },
        { Sql, "autoRotationEnabled: true", "autoRotationEnabled: false", "TdeNotRotating" },
        { Sql, "restrictOutboundNetworkAccess: 'Enabled'", "restrictOutboundNetworkAccess: 'Disabled'", "OutboundNotRestricted" },
        { Main, "module keyVault 'modules/key-vault.bicep' = if (isPrimary)", "module keyVault 'modules/key-vault.bicep' = if (false)", "KeyProtectionNotDeployed" },
        { Main, "module sql 'modules/sql.bicep' = if (isPrimary)", "module sql 'modules/sql.bicep' = if (false)", "KeyProtectionNotDeployed" },
        { Main, "module keyRingStorage 'modules/key-ring-storage.bicep' = if (isPrimary)", "module keyRingStorage 'modules/key-ring-storage.bicep' = if (false)", "KeyProtectionNotDeployed" },
        // T04: the source store, one rule at a time.
        { Storage, "name: 'Standard_GZRS'", "name: 'Standard_LRS'", "SourceNotGeoZoneRedundant" },
        { Storage, "name: 'Standard_GZRS'", "name: 'Standard_RAGZRS'", "SourceNotGeoZoneRedundant" },
        { Storage, "kind: 'StorageV2'", "kind: 'BlobStorage'", "SourceAccountNotApproved" },
        { Storage, "isHnsEnabled: false", "isHnsEnabled: true", "SourceAccountNotApproved" },
        { Storage, "allowSharedKeyAccess: false", "allowSharedKeyAccess: true", "SharedKeyAccessEnabled" },
        { Storage, "allowBlobPublicAccess: false", "allowBlobPublicAccess: true", "PublicEndpointConfigured" },
        { Storage, "    publicNetworkAccess: 'Disabled'", "    publicNetworkAccess: 'Enabled'", "PublicEndpointConfigured" },
        { Storage, "bypass: 'None'", "bypass: 'AzureServices'", "FirewallNotDenyDefault" },
        { Storage, "defaultAction: 'Deny'", "defaultAction: 'Allow'", "FirewallNotDenyDefault" },
        { Storage, "minimumTlsVersion: 'TLS1_2'", "minimumTlsVersion: 'TLS1_0'", "TransportBelowMinimum" },
        { Storage, "supportsHttpsTrafficOnly: true", "supportsHttpsTrafficOnly: false", "TransportBelowMinimum" },
        { Storage, "keySource: 'Microsoft.Keyvault'", "keySource: 'Microsoft.Storage'", "SourceNotCustomerManaged" },
        { Storage, "userAssignedIdentity: cmkIdentity.id", "userAssignedIdentity: ''", "SourceNotCustomerManaged" },
        { Storage, "requireInfrastructureEncryption: true", "requireInfrastructureEncryption: false", "SourceNotCustomerManaged" },
        { Storage, "isVersioningEnabled: false", "isVersioningEnabled: true", "SourceShadowRetentionEnabled" },
        { Storage, "    deleteRetentionPolicy: {\n      enabled: false", "    deleteRetentionPolicy: {\n      enabled: true", "SourceShadowRetentionEnabled" },
        { Storage, "    containerDeleteRetentionPolicy: {\n      enabled: false", "    containerDeleteRetentionPolicy: {\n      enabled: true", "SourceShadowRetentionEnabled" },
        { Storage, "    changeFeed: {\n      enabled: false", "    changeFeed: {\n      enabled: true", "SourceShadowRetentionEnabled" },
        { Storage, "    restorePolicy: {\n      enabled: false", "    restorePolicy: {\n      enabled: true", "SourceShadowRetentionEnabled" },
        { Storage, "    publicAccess: 'None'\n  }", "    publicAccess: 'None'\n    immutableStorageWithVersioning: {\n      enabled: true\n    }\n  }", "SourceShadowRetentionEnabled" },
        { Storage, "publicAccess: 'None'", "publicAccess: 'Blob'", "SourceContainerPublic" },
        { Storage, "scope: container", "scope: account", "RoleNotLeastPrivilege" },
        { Storage, "scope: sourceBlobKey", "scope: vault", "RoleNotLeastPrivilege" },
        { Main, "module sourceStorage 'modules/storage.bicep' = if (isPrimary)", "module sourceStorage 'modules/storage.bicep' = if (false)", "SourceStoreNotDeployed" },
    };

    [Theory]
    [MemberData(nameof(UnsafeMutations))]
    public void EachUnsafeTemplateChangeIsRefusedByItsOwnRule(
        string module,
        string original,
        string mutated,
        string expected)
    {
        var sources = TrackedSources();
        Assert.Contains(original, sources[module], StringComparison.Ordinal);
        sources[module] = sources[module].Replace(original, mutated, StringComparison.Ordinal);

        Assert.Contains(expected, PostureViolations(sources));
    }

    [Theory]
    [InlineData(KeyVault)]
    [InlineData(KeyRing)]
    [InlineData(Sql)]
    [InlineData(Storage)]
    public void NoModuleEnablesADiagnosticSinkPreviewOrStoredCredential(string module)
    {
        var template = Template(module);

        foreach (var forbidden in new[]
                 {
                     "diagnosticSettings", "preview", "accessPolicies", "listKeys(", "listAccountSas(",
                     "administratorLoginPassword", "@secure()", "connectionString", "value:",
                 })
        {
            Assert.DoesNotContain(forbidden, template, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact] // The dependency private endpoints bind resources that now exist first.
    public void TheDependencyPrivateEndpointsDependOnTheKeyVaultAndSqlResources()
    {
        var main = Template(Main);
        var block = main[main.IndexOf("module privateEndpoints", StringComparison.Ordinal)..];
        block = block[..(block.IndexOf("\nmodule ", 1, StringComparison.Ordinal) is var next and > 0 ? next : block.Length)];

        Assert.Matches(new Regex(@"dependsOn: \[\s+keyVault\s+sql\s+sourceStorage\s+\]"), block);
        Assert.Matches(new Regex(@"tdeKeyUriWithVersion: keyVault!\.outputs\.sqlTdeKeyUriWithVersion"), main);
        Assert.Matches(new Regex(@"blobZoneId: privateDns\.outputs\.zoneIds\.SourceStorage"), main);
    }

    [Fact] // Keys are purpose-separated and the application reaches only two of them.
    public void TheApplicationIdentityReachesOnlyTheEnvelopeAndKeyRingKeys()
    {
        var vault = Template(KeyVault);

        Assert.Matches(
            new Regex(@"var keyNames = \[\s+sourceEnvelopeKeyName\s+dataProtectionKeyName\s+sqlTdeKeyName\s+sourceBlobKeyName\s+\]"),
            vault);
        Assert.Contains("[for index in range(0, 2):", vault, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(vault, @"roleDefinitionId: subscriptionResourceId").Count);
        Assert.DoesNotMatch(new Regex(@"scope: (vault|resourceGroup\(\))"), vault);

        var sql = Template(Sql);
        Assert.Contains("scope: tdeKey", sql, StringComparison.Ordinal);
        Assert.Contains("principalId: tdeIdentity.properties.principalId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("managedIdentityPrincipalId", sql, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------- helpers

    /// <summary>
    /// The T03 template posture rules. Each returns a closed code and quotes no
    /// template value.
    /// </summary>
    internal static IReadOnlyList<string> PostureViolations(IReadOnlyDictionary<string, string> sources)
    {
        var violations = new List<string>();
        var vault = sources[KeyVault];
        var keyRing = sources[KeyRing];
        var sql = sources[Sql];
        var main = sources[Main];

        void Require(bool holds, string code)
        {
            if (!holds)
            {
                violations.Add(code);
            }
        }

        Require(vault.Contains("enableRbacAuthorization: true", StringComparison.Ordinal), "VaultNotRbac");
        Require(vault.Contains("enablePurgeProtection: true", StringComparison.Ordinal) &&
                vault.Contains("enableSoftDelete: true", StringComparison.Ordinal), "PurgeProtectionDisabled");
        Require(vault.Contains("softDeleteRetentionInDays: 90", StringComparison.Ordinal), "SoftDeleteRetentionShortened");
        Require(vault.Contains("defaultAction: 'Deny'", StringComparison.Ordinal), "FirewallNotDenyDefault");
        Require(!vault.Contains("exportable: true", StringComparison.Ordinal) &&
                vault.Contains("exportable: false", StringComparison.Ordinal), "KeyExportable");
        Require(Regex.Matches(vault, @"timeAfterCreate: '(?<t>[^']+)'").Count == 1 &&
                KeyVersionUsageGuard.MeetsRotationTarget(Regex.Match(vault, @"timeAfterCreate: '(?<t>[^']+)'").Groups["t"].Value), "RotationTargetNotNinetyDays");
        Require(vault.Contains("kty: 'RSA-HSM'", StringComparison.Ordinal), "KeyNotHsmBacked");
        Require(vault.Contains("keySize: 3072", StringComparison.Ordinal), "KeySizeBelowMinimum");
        Require(Regex.IsMatch(vault, @"keyOps: \[\s+'wrapKey'\s+'unwrapKey'\s+\]"), "KeyOperationsWidened");
        Require(!Regex.IsMatch(vault, @"enabledFor\w+: true"), "VaultAccessWidened");
        Require(Regex.IsMatch(vault, @"resource intakeSecrets '[^']+' existing ="), "SecretValueInTemplate");

        Require(keyRing.Contains("allowSharedKeyAccess: false", StringComparison.Ordinal), "SharedKeyAccessEnabled");
        Require(keyRing.Contains("allowBlobPublicAccess: false", StringComparison.Ordinal), "PublicEndpointConfigured");
        Require(keyRing.Contains("minimumTlsVersion: 'TLS1_2'", StringComparison.Ordinal) &&
                keyRing.Contains("supportsHttpsTrafficOnly: true", StringComparison.Ordinal), "TransportBelowMinimum");
        Require(keyRing.Contains("isVersioningEnabled: true", StringComparison.Ordinal), "KeyRingRecoveryDisabled");
        Require(keyRing.Contains("name: 'Standard_GZRS'", StringComparison.Ordinal), "KeyRingNotGeoRedundant");
        Require(keyRing.Contains("privateDnsZoneId: blobZoneId", StringComparison.Ordinal) &&
                keyRing.Contains("groupIds: [\n            'blob'\n          ]", StringComparison.Ordinal), "PrivateBindingMissing");

        Require(sql.Contains("azureADOnlyAuthentication: true", StringComparison.Ordinal), "SqlLoginsEnabled");
        Require(sql.Contains("minimalTlsVersion: '1.2'", StringComparison.Ordinal), "TransportBelowMinimum");
        Require(Regex.Matches(sql, @"serverKeyType: 'AzureKeyVault'").Count == 2 &&
                sql.Contains("keyId: tdeKeyUriWithVersion", StringComparison.Ordinal), "TdeNotCustomerManaged");
        Require(sql.Contains("autoRotationEnabled: true", StringComparison.Ordinal), "TdeNotRotating");
        Require(sql.Contains("restrictOutboundNetworkAccess: 'Enabled'", StringComparison.Ordinal), "OutboundNotRestricted");

        foreach (var (name, source) in new[] { ("vault", vault), ("keyRing", keyRing), ("sql", sql) })
        {
            if (source.Contains("publicNetworkAccess: 'Enabled'", StringComparison.Ordinal) ||
                !source.Contains("publicNetworkAccess: 'Disabled'", StringComparison.Ordinal))
            {
                violations.Add("PublicEndpointConfigured");
            }

            _ = name;
        }

        Require(vault.Contains("scope: keys[index]", StringComparison.Ordinal) &&
                vault.Contains("scope: intakeSecrets[index]", StringComparison.Ordinal) &&
                keyRing.Contains("scope: container", StringComparison.Ordinal) &&
                sql.Contains("scope: tdeKey", StringComparison.Ordinal), "RoleNotLeastPrivilege");

        foreach (var module in new[] { "keyVault 'modules/key-vault.bicep'", "keyRingStorage 'modules/key-ring-storage.bicep'", "sql 'modules/sql.bicep'" })
        {
            Require(main.Contains($"module {module} = if (isPrimary)", StringComparison.Ordinal), "KeyProtectionNotDeployed");
        }

        // T04: the source store.
        var storage = sources[Storage];
        Require(storage.Contains("name: 'Standard_GZRS'", StringComparison.Ordinal), "SourceNotGeoZoneRedundant");
        Require(storage.Contains("kind: 'StorageV2'", StringComparison.Ordinal) &&
                storage.Contains("isHnsEnabled: false", StringComparison.Ordinal), "SourceAccountNotApproved");
        Require(storage.Contains("allowSharedKeyAccess: false", StringComparison.Ordinal) &&
                storage.Contains("isLocalUserEnabled: false", StringComparison.Ordinal), "SharedKeyAccessEnabled");
        Require(storage.Contains("allowBlobPublicAccess: false", StringComparison.Ordinal) &&
                storage.Contains("    publicNetworkAccess: 'Disabled'", StringComparison.Ordinal) &&
                !storage.Contains("publicNetworkAccess: 'Enabled'", StringComparison.Ordinal), "PublicEndpointConfigured");
        Require(storage.Contains("defaultAction: 'Deny'", StringComparison.Ordinal) &&
                storage.Contains("bypass: 'None'", StringComparison.Ordinal), "FirewallNotDenyDefault");
        Require(storage.Contains("minimumTlsVersion: 'TLS1_2'", StringComparison.Ordinal) &&
                storage.Contains("supportsHttpsTrafficOnly: true", StringComparison.Ordinal), "TransportBelowMinimum");
        Require(storage.Contains("keySource: 'Microsoft.Keyvault'", StringComparison.Ordinal) &&
                storage.Contains("userAssignedIdentity: cmkIdentity.id", StringComparison.Ordinal) &&
                storage.Contains("keyname: sourceBlobKeyName", StringComparison.Ordinal) &&
                storage.Contains("requireInfrastructureEncryption: true", StringComparison.Ordinal), "SourceNotCustomerManaged");
        Require(storage.Contains("isVersioningEnabled: false", StringComparison.Ordinal) &&
                Regex.Matches(storage, @"(deleteRetentionPolicy|containerDeleteRetentionPolicy|changeFeed|restorePolicy): \{\n      enabled: false").Count == 4 &&
                !Regex.IsMatch(storage, @"immutab|legalHold|snapshot|lastAccessTimeTracking|managementPolicies", RegexOptions.IgnoreCase), "SourceShadowRetentionEnabled");
        Require(Regex.Matches(storage, @"publicAccess: '(?<v>[^']+)'").Cast<Match>().All(match => match.Groups["v"].Value == "None") &&
                storage.Contains("publicAccess: 'None'", StringComparison.Ordinal), "SourceContainerPublic");
        Require(storage.Contains("scope: container", StringComparison.Ordinal) &&
                storage.Contains("scope: sourceBlobKey", StringComparison.Ordinal) &&
                Regex.Matches(storage, @"\bscope: ").Count == 2, "RoleNotLeastPrivilege");
        Require(main.Contains("module sourceStorage 'modules/storage.bicep' = if (isPrimary)", StringComparison.Ordinal), "SourceStoreNotDeployed");

        return violations.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static Dictionary<string, string> TrackedSources() =>
        new[] { KeyVault, KeyRing, Sql, Storage, Main }.ToDictionary(module => module, Template, StringComparer.Ordinal);

    /// <summary>The template with whole-line comments removed and LF endings.</summary>
    private static string Template(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(InfraRoot, relativePath)).ReplaceLineEndings("\n");
        return Regex.Replace(text, @"(?m)^\s*//.*\n", string.Empty);
    }

    private static JsonElement Const(params string[] path)
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        var node = schema.RootElement;
        foreach (var segment in path)
        {
            node = node.GetProperty(segment);
        }

        return node.TryGetProperty("const", out var value) ? value.Clone() : node.Clone();
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
