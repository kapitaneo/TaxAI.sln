using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TaxAI.AzureInfrastructure.Configuration;

namespace TaxAI.AzureInfrastructure.Tests.Boundaries;

/// <summary>
/// Spec 016 T01 — the boundary of the new Azure infrastructure library
/// (AC-003–008), extended by T03 with its working key-protection consumers.
/// <para>
/// Everything this library may own is pinned exactly: its one project
/// reference, its packages, its source inventory and its public surface.
/// Later slices extend each pin only with a working consumer. Every negative
/// scan has a positive control so an empty result cannot pass for free.
/// </para>
/// <para>
/// T03 adds exactly the Key Vault key and secret clients, the Azure Blob and
/// Key Vault Data Protection integrations and the Data Protection abstractions
/// they configure, each consumed by the wrapper, the runtime-material
/// provider, the key-ring configuration or the approved-resource probe. The
/// only ASP.NET Core assemblies the library may name are the two Data
/// Protection ones; hosting, HTTP and routing stay in the portal. No
/// <c>ClientIntake</c> reference is added: the credential port implementation
/// stays in the portal and consumes the loaded material unchanged.
/// </para>
/// <para>
/// T04 adds exactly the two inward references its working source adapters
/// implement — <c>ClientIntake</c> (the write-only intake content port, the
/// source registry and the purger) and <c>ExtractionWorkflow</c> (the
/// extraction-only reader) — and the four <c>Content/</c> sources. No package
/// changes: the Blob client was already pinned by T03. The source adapters get
/// their own scans: no filesystem, no SAS or shared key, and every write
/// upload-once, each with a positive control.
/// </para>
/// </summary>
public sealed class AzureInfrastructureBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string LibraryRoot =
        Path.Combine(RepositoryRoot, "src", "TaxAI.AzureInfrastructure");

    private static readonly string LibraryProject =
        Path.Combine(LibraryRoot, "TaxAI.AzureInfrastructure.csproj");

    private static readonly Assembly Library = typeof(ProductionConfigurationValidator).Assembly;

    [Fact]
    public void TheLibraryIsANet10ClassLibraryNotAHost()
    {
        var project = XDocument.Load(LibraryProject);

        Assert.Equal("Microsoft.NET.Sdk", project.Root!.Attribute("Sdk")!.Value);
        Assert.Equal(
            new[] { "net10.0" },
            project.Descendants().Where(e => e.Name.LocalName == "TargetFramework").Select(e => e.Value));
        Assert.DoesNotContain(project.Descendants(), e => e.Name.LocalName is "OutputType" or "FrameworkReference");
        Assert.Null(Library.EntryPoint);
    }

    [Fact] // AC-005
    public void TheLibraryReferencesExactlyTheConsumedInwardProjects()
    {
        Assert.Equal(
            new[]
            {
                @"..\ClientIntake\ClientIntake.csproj",
                @"..\ExtractionWorkflow\ExtractionWorkflow.csproj",
                @"..\Tenancy\Tenancy.csproj",
            },
            ProjectReferences(LibraryProject));
    }

    [Fact] // T04: the two new references are consumed, not merely declared.
    public void TheCompiledLibraryNamesTheConsumedPortAssemblies()
    {
        var referenced = Library.GetReferencedAssemblies().Select(name => name.Name!).ToArray();

        Assert.Contains("ClientIntake", referenced);
        Assert.Contains("ExtractionWorkflow", referenced);
        Assert.Contains("Azure.Storage.Blobs", referenced);
    }

    [Fact] // AC-004, AC-006
    public void TheLibraryDeclaresExactlyTheConsumedPackagesAtPinnedVersions()
    {
        var packages = XDocument.Load(LibraryProject)
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (e.Attribute("Include")!.Value, e.Attribute("Version")!.Value))
            .OrderBy(p => p.Item1, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                ("Azure.Core", "1.54.0"),
                ("Azure.Extensions.AspNetCore.DataProtection.Blobs", "1.5.2"),
                ("Azure.Extensions.AspNetCore.DataProtection.Keys", "1.6.2"),
                ("Azure.Identity", "1.21.0"),
                ("Azure.Security.KeyVault.Keys", "4.10.1"),
                ("Azure.Security.KeyVault.Secrets", "4.11.1"),
                ("Azure.Storage.Blobs", "12.26.0"),
                ("Microsoft.AspNetCore.DataProtection", "10.0.10"),
                ("Microsoft.Extensions.DependencyInjection.Abstractions", "10.0.10"),
            },
            packages);
    }

    [Fact] // No preview, floating or ranged version anywhere in the library.
    public void EveryPackageVersionIsAnExactStableRelease()
    {
        var versions = XDocument.Load(LibraryProject)
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => e.Attribute("Version")!.Value)
            .ToArray();

        Assert.NotEmpty(versions);
        Assert.All(versions, version => Assert.Matches(@"^\d+\.\d+\.\d+$", version));
    }

    [Fact] // T03: the only ASP.NET Core assemblies are the Data Protection pair.
    public void TheOnlyAspNetCoreAssembliesNamedAreDataProtection()
    {
        var aspNetCore = Library.GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("Microsoft.AspNetCore.DataProtection", aspNetCore);
        Assert.All(aspNetCore, name => Assert.Contains(
            name,
            new[] { "Microsoft.AspNetCore.DataProtection", "Microsoft.AspNetCore.DataProtection.Abstractions" }));
    }

    [Theory] // AC-004, AC-006, AC-007
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.AspNetCore.Http")]
    [InlineData("Microsoft.AspNetCore.Hosting")]
    [InlineData("Microsoft.AspNetCore.Routing")]
    [InlineData("Microsoft.AspNetCore.Mvc")]
    [InlineData("Microsoft.AspNetCore.Authentication")]
    [InlineData("Microsoft.AspNetCore.Components")]
    [InlineData("Azure.AI.DocumentIntelligence")]
    [InlineData("Azure.AI.FormRecognizer")]
    [InlineData("DocumentIntelligence")]
    [InlineData("Persistence")]
    [InlineData("TaxAI.Portal")]
    [InlineData("TaxLogicEngine")]
    [InlineData("CaseProcessing")]
    [InlineData("DocumentExtraction")]
    [InlineData("TaxCases")]
    public void TheCompiledLibraryNamesNoForbiddenAssembly(string forbidden)
    {
        var referenced = Library.GetReferencedAssemblies().Select(name => name.Name!).ToArray();

        // Azure.Identity 1.21 forwards its credential types into Azure.Core,
        // so the compiled reference is Azure.Core; the package pin above is
        // what records the Azure.Identity dependency.
        Assert.Contains("Tenancy", referenced);
        Assert.Contains("Azure.Core", referenced);
        Assert.Contains("Azure.Security.KeyVault.Keys", referenced);
        Assert.DoesNotContain(
            referenced,
            name => name.StartsWith(forbidden, StringComparison.Ordinal));
    }

    [Fact] // AC-003: no module references the host, and only the host references this library.
    public void OnlyThePortalReferencesTheInfrastructureLibrary()
    {
        var consumers = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => ProjectReferences(path).Any(reference =>
                reference.Contains("TaxAI.AzureInfrastructure.csproj", StringComparison.OrdinalIgnoreCase)))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Equal(new[] { "TaxAI.Portal.csproj" }, consumers);
    }

    [Fact]
    public void TheSourceInventoryIsExactlyTheT01T03AndT04Slices()
    {
        var files = SourceFiles()
            .Select(path => Path.GetRelativePath(LibraryRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "AzureInfrastructureRegistration.cs",
                "Configuration/ApprovedResourceProbe.cs",
                "Configuration/AzureInfrastructureOptions.cs",
                "Configuration/ManagedIdentityBindingProbe.cs",
                "Configuration/ProductionConfigurationValidator.cs",
                "Content/AzureExtractionContentReader.cs",
                "Content/AzureIntakeContentStore.cs",
                "Content/AzureSourceObjectPurger.cs",
                "Content/SourceEnvelopeCodec.cs",
                "Keys/KeyVaultContentKeyWrapper.cs",
                "Keys/KeyVaultRuntimeMaterialProvider.cs",
                "Keys/KeyVersionUsageGuard.cs",
                "Protection/AzureDataProtectionConfiguration.cs",
            },
            files);
    }

    [Fact]
    public void ThePublicSurfaceIsExactlyTheConsumedSlice()
    {
        Assert.Equal(
            new[]
            {
                "TaxAI.AzureInfrastructure.AzureInfrastructureRegistration",
                "TaxAI.AzureInfrastructure.Configuration.ApprovedResourceFinding",
                "TaxAI.AzureInfrastructure.Configuration.ApprovedResourceProbe",
                "TaxAI.AzureInfrastructure.Configuration.ApprovedResourceProbeResult",
                "TaxAI.AzureInfrastructure.Configuration.AzureInfrastructureOptions",
                "TaxAI.AzureInfrastructure.Configuration.DataProtectionSettings",
                "TaxAI.AzureInfrastructure.Configuration.DocumentIntelligenceSettings",
                "TaxAI.AzureInfrastructure.Configuration.IApprovedResourceProbe",
                "TaxAI.AzureInfrastructure.Configuration.KeyPostureViolation",
                "TaxAI.AzureInfrastructure.Configuration.ManagedIdentityBindingProbe",
                "TaxAI.AzureInfrastructure.Configuration.ManagedIdentitySettings",
                "TaxAI.AzureInfrastructure.Configuration.MonitoringSettings",
                "TaxAI.AzureInfrastructure.Configuration.PrivateDependencySettings",
                "TaxAI.AzureInfrastructure.Configuration.ProductionConfigurationValidator",
                "TaxAI.AzureInfrastructure.Configuration.ResilienceSettings",
                "TaxAI.AzureInfrastructure.Configuration.RetentionSettings",
                "TaxAI.AzureInfrastructure.Configuration.SourceEnvelopeSettings",
                "TaxAI.AzureInfrastructure.Content.AzureExtractionContentReader",
                "TaxAI.AzureInfrastructure.Content.AzureIntakeContentStore",
                "TaxAI.AzureInfrastructure.Content.AzureSourceObjectPurger",
                "TaxAI.AzureInfrastructure.Content.SourceEnvelopeCodec",
                "TaxAI.AzureInfrastructure.Content.SourceEnvelopeScope",
                "TaxAI.AzureInfrastructure.Keys.EmergencyRevocationRequest",
                "TaxAI.AzureInfrastructure.Keys.KeyGuardDecision",
                "TaxAI.AzureInfrastructure.Keys.KeyGuardRefusal",
                "TaxAI.AzureInfrastructure.Keys.KeyRetirementRequest",
                "TaxAI.AzureInfrastructure.Keys.KeyUsageEvidence",
                "TaxAI.AzureInfrastructure.Keys.KeyUsageSource",
                "TaxAI.AzureInfrastructure.Keys.KeyVaultContentKeyWrapper",
                "TaxAI.AzureInfrastructure.Keys.KeyVaultRuntimeMaterialProvider",
                "TaxAI.AzureInfrastructure.Keys.KeyVersionUsageGuard",
                "TaxAI.AzureInfrastructure.Keys.PrivilegedElevation",
                "TaxAI.AzureInfrastructure.Keys.RevocationJustification",
                "TaxAI.AzureInfrastructure.Keys.RuntimeMaterial",
                "TaxAI.AzureInfrastructure.Keys.RuntimeMaterialOutcome",
                "TaxAI.AzureInfrastructure.Keys.WrappedContentKey",
                "TaxAI.AzureInfrastructure.Protection.AzureDataProtectionConfiguration",
                "TaxAI.AzureInfrastructure.Protection.ProtectedKeyRingSettings",
                "TaxAI.AzureInfrastructure.SourceContentSettings",
            },
            Library.GetExportedTypes().Select(type => type.FullName!).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryInstanceTypeIsSealedWithAStructuralToString()
    {
        var instanceTypes = Library.GetTypes()
            .Where(type => type.IsClass && !type.Name.StartsWith('<') && !(type.IsAbstract && type.IsSealed))
            .Where(type => type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null)
            .ToArray();

        Assert.NotEmpty(instanceTypes);
        Assert.All(instanceTypes, type =>
        {
            Assert.True(type.IsSealed, $"{type.FullName} must be sealed.");
            Assert.Equal(type, type.GetMethod(nameof(ToString), Type.EmptyTypes)!.DeclaringType);
        });
    }

    [Fact]
    public void TheLibraryDeclaresNoInternalsGrant()
    {
        Assert.Empty(Library.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>());
    }

    [Theory] // AC-008 and the identity boundary
    [InlineData("tax authority", @"\bT4\w*|\bT5\w*|\bCra\w*|\bLineMap\w*|\bdecimal\b|\bMath\.Round\b")]
    [InlineData("SIN", @"\bSin\b|SocialInsurance|\bMask\w*|\bRedact\w*")]
    [InlineData("control plane", @"management\.azure\.com|\bArmClient\b|Azure\.ResourceManager")]
    [InlineData("credential chain", @"\bDefaultAzureCredential\b|\bChainedTokenCredential\b|\bEnvironmentCredential\b|\bAzureCliCredential\b|\bInteractiveBrowserCredential\b|\bClientSecretCredential\b|\bVisualStudio\w*Credential\b")]
    [InlineData("diagnostics", @"\bILogger\b|\bConsole\s*\.|\bTrace\s*\.|\bDebug\s*\.|\bEventSource\b|\bAzureEventSourceListener\b")]
    [InlineData("filesystem", @"\bFile\s*\.|\bDirectory\s*\.|\bFileStream\b")]
    [InlineData("network", @"\bHttpClient\b|\bWebRequest\b|\bSocket\b|\bTcpClient\b")]
    [InlineData("clock", @"\bDateTime\.(Utc)?Now\b|\bDateTimeOffset\.(Utc)?Now\b")]
    [InlineData("environment", @"\bEnvironment\.GetEnvironmentVariable\b")]
    [InlineData("EF", @"\bDbContext\b|\bEntityFrameworkCore\b")]
    [InlineData("web", @"\bMicrosoft\.AspNetCore\.(?!DataProtection\b)\w+|\bHttpContext\b|\bIApplicationBuilder\b|\bWebApplication\b")]
    [InlineData("key export", @"\bExportParameters\b|\bExportRSAPrivateKey\w*|\bReleaseKey\w*|\bGetRandomBytes\b|\bBackupKey\w*")]
    [InlineData("secret access", @"\bStorageSharedKeyCredential\b|\bSasBuilder\b|\bGenerateSasUri\b|AccountKey=|SharedAccessSignature")]
    [InlineData("TLS bypass", @"ServerCertificateCustomValidationCallback|DangerousAcceptAnyServerCertificateValidator|RemoteCertificateValidationCallback|TrustServerCertificate\s*=\s*true")]
    public void ExecutableSourceContainsNoForbiddenToken(string boundary, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var offenders = SourceFiles()
            .SelectMany(path => ExecutableLines(path).Select(line => (path, line)))
            .Where(entry => regex.IsMatch(entry.line))
            .Select(entry => $"{Path.GetFileName(entry.path)}: {entry.line}")
            .ToArray();

        Assert.True(offenders.Length == 0, $"{boundary}: {string.Join(" | ", offenders)}");
    }

    [Fact]
    public void TheScannerSeesTheSourceAndMatchesKnownPositiveTokens()
    {
        var executable = string.Join("\n", SourceFiles().SelectMany(ExecutableLines));

        Assert.True(executable.Length > 5000, "Implausibly little executable source.");
        Assert.Matches(new Regex(@"\bManagedIdentityCredential\b"), executable);
        Assert.Matches(new Regex(@"vault\.azure\.net/\.default"), executable);
        Assert.Matches(new Regex(@"\bProductionAdmissionRefusal\b"), executable);
        Assert.Matches(new Regex(@"\bDbConnectionStringBuilder\b"), executable);

        // T03 positive controls: the scanners see the real key-protection code,
        // and the narrowed web pattern still recognizes a forbidden host token.
        Assert.Matches(new Regex(@"\bWrapKeyAsync\b"), executable);
        Assert.Matches(new Regex(@"\bGetSecretAsync\b"), executable);
        Assert.Matches(new Regex(@"\bProtectKeysWithAzureKeyVault\b"), executable);
        Assert.Matches(new Regex(@"\bPersistKeysToAzureBlobStorage\b"), executable);
        Assert.Matches(new Regex(@"using Microsoft\.AspNetCore\.DataProtection;"), executable);
        var web = new Regex(@"\bMicrosoft\.AspNetCore\.(?!DataProtection\b)\w+");
        Assert.Matches(web, "using Microsoft.AspNetCore.Http;");
        Assert.DoesNotMatch(web, "using Microsoft.AspNetCore.DataProtection;");
    }

    [Theory] // T04: the source adapters (AC-028, AC-037).
    [InlineData("temporary files", @"\bPath\s*\.|\bGetTempFileName\b|\bGetTempPath\b|\bFileInfo\b|\bIsolatedStorage\w*")]
    [InlineData("source addressing", @"\bGenerateSasUri\b|\bGenerateUserDelegationSasUri\b|\bGetUserDelegationKey\w*|\bBlobSasBuilder\b|\bPublicAccessType\.(Blob|BlobContainer)\b|\bSetAccessPolicy\w*")]
    [InlineData("shadow retention", @"\bCreateSnapshot\w*|\bSetImmutabilityPolicy\w*|\bSetLegalHold\w*|\bUndelete\w*|\bStartCopyFromUri\w*|\bSyncCopyFromUri\w*")]
    [InlineData("overwrite", @"\bUploadAsync\s*\([^)]*overwrite\s*:\s*true|\bOpenWrite\w*")]
    public void TheSourceAdaptersContainNoForbiddenToken(string boundary, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var offenders = ContentFiles()
            .SelectMany(path => ExecutableLines(path).Select(line => (path, line)))
            .Where(entry => regex.IsMatch(entry.line))
            .Select(entry => $"{Path.GetFileName(entry.path)}: {entry.line}")
            .ToArray();

        Assert.True(offenders.Length == 0, $"{boundary}: {string.Join(" | ", offenders)}");
    }

    [Fact] // The positive controls for the source-adapter scans.
    public void TheSourceScansSeeTheRealAdaptersAndRecognizeForbiddenTokens()
    {
        var executable = string.Join("\n", ContentFiles().SelectMany(ExecutableLines));

        Assert.Equal(4, ContentFiles().Length);
        Assert.Matches(new Regex(@"IfNoneMatch = ETag\.All"), executable);
        Assert.Matches(new Regex(@"\bCommitBlockListAsync\b"), executable);
        Assert.Matches(new Regex(@"\bDeleteSnapshotsOption\.IncludeSnapshots\b"), executable);
        Assert.Matches(new Regex(@"\bFindBoundAsync\b"), executable);
        Assert.Matches(new Regex(@"\bRegisterStagedAsync\b"), executable);
        Assert.Matches(new Regex(@"\bAesGcm\b"), executable);
        Assert.Matches(new Regex(@"\bGenerateSasUri\b"), "var uri = blob.GenerateSasUri(builder);");
        Assert.Matches(new Regex(@"\bPath\s*\."), "var path = Path.GetTempFileName();");
        Assert.Matches(new Regex(@"\bCreateSnapshot\w*"), "await blob.CreateSnapshotAsync();");
    }

    [Fact] // Every source write is upload-once: each upload/commit call site is conditioned.
    public void EveryUploadAndCommitInTheSourceStoreIsConditionedOnAbsence()
    {
        var store = File.ReadAllText(Path.Combine(LibraryRoot, "Content", "AzureIntakeContentStore.cs"));

        Assert.Equal(2, Regex.Matches(store, @"\.(UploadAsync|CommitBlockListAsync)\(").Count);
        Assert.Equal(2, Regex.Matches(store, @"IfNoneMatch = ETag\.All").Count);
    }

    [Fact]
    public void TheCommentFilterRemovesDocumentationButKeepsCode()
    {
        var probe = Path.Combine(LibraryRoot, "Configuration", "ManagedIdentityBindingProbe.cs");
        var raw = File.ReadAllText(probe);
        var executable = string.Join("\n", ExecutableLines(probe));

        Assert.Contains("Azure Resource Manager", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Azure Resource Manager", executable, StringComparison.Ordinal);
        Assert.Contains("GetTokenAsync", executable, StringComparison.Ordinal);
    }

    private static string[] SourceFiles() =>
        Directory.EnumerateFiles(LibraryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string[] ContentFiles() =>
        SourceFiles()
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Content{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

    private static string[] ProjectReferences(string project) =>
        XDocument.Load(project)
            .Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => e.Attribute("Include")!.Value)
            .ToArray();

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

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
