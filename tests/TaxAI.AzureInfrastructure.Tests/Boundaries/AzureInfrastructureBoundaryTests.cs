using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TaxAI.AzureInfrastructure.Configuration;

namespace TaxAI.AzureInfrastructure.Tests.Boundaries;

/// <summary>
/// Spec 016 T01 — the boundary of the new Azure infrastructure library
/// (AC-003–008).
/// <para>
/// Everything this library may own is pinned exactly: its one project
/// reference, its two packages, its source inventory and its public surface.
/// Later slices extend each pin only with a working consumer. Every negative
/// scan has a positive control so an empty result cannot pass for free.
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
    public void TheLibraryReferencesExactlyTenancyInThisSlice()
    {
        Assert.Equal(
            new[] { @"..\Tenancy\Tenancy.csproj" },
            ProjectReferences(LibraryProject));
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
                ("Azure.Identity", "1.21.0"),
                ("Microsoft.Extensions.DependencyInjection.Abstractions", "10.0.10"),
            },
            packages);
    }

    [Theory] // AC-004, AC-006, AC-007
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Azure.AI.DocumentIntelligence")]
    [InlineData("Azure.AI.FormRecognizer")]
    [InlineData("DocumentIntelligence")]
    [InlineData("ExtractionWorkflow")]
    [InlineData("ClientIntake")]
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
    public void TheSourceInventoryIsExactlyTheT01Slice()
    {
        var files = SourceFiles()
            .Select(path => Path.GetRelativePath(LibraryRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "AzureInfrastructureRegistration.cs",
                "Configuration/AzureInfrastructureOptions.cs",
                "Configuration/ManagedIdentityBindingProbe.cs",
                "Configuration/ProductionConfigurationValidator.cs",
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
                "TaxAI.AzureInfrastructure.Configuration.AzureInfrastructureOptions",
                "TaxAI.AzureInfrastructure.Configuration.DataProtectionSettings",
                "TaxAI.AzureInfrastructure.Configuration.DocumentIntelligenceSettings",
                "TaxAI.AzureInfrastructure.Configuration.ManagedIdentityBindingProbe",
                "TaxAI.AzureInfrastructure.Configuration.ManagedIdentitySettings",
                "TaxAI.AzureInfrastructure.Configuration.MonitoringSettings",
                "TaxAI.AzureInfrastructure.Configuration.PrivateDependencySettings",
                "TaxAI.AzureInfrastructure.Configuration.ProductionConfigurationValidator",
                "TaxAI.AzureInfrastructure.Configuration.ResilienceSettings",
                "TaxAI.AzureInfrastructure.Configuration.RetentionSettings",
                "TaxAI.AzureInfrastructure.Configuration.SourceEnvelopeSettings",
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
    [InlineData("web", @"\bMicrosoft\.AspNetCore\b|\bHttpContext\b")]
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
