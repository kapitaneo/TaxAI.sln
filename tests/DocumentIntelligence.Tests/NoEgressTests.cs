using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DocumentIntelligence.Tests;

/// <summary>
/// Spec 015 T02 — taxpayer source bytes do not leave this process.
/// <para>
/// That claim is worth only as much as the way it is checked, so it is checked
/// four ways: the whole extraction flow runs to completion without loading a
/// single networking assembly; the transitive assembly closure of both new
/// modules contains none, so no networking type is even nameable from them;
/// no project in the repository declares an OCR, LLM, document-intelligence,
/// HTTP-client, or cloud package; and no executable line in either module names
/// a network API.
/// </para>
/// <para>
/// The load-tracking check is the behavioural one, and it is deliberately
/// deterministic rather than clever. Watching a networking event source would
/// pick up the test host's own communication channel and make this test a coin
/// flip; watching what the flow itself pulls into the process does not. The
/// end-to-end run with outbound access actually blocked belongs to the
/// cross-module verification task, where the whole journey exists to run.
/// </para>
/// </summary>
public sealed class NoEgressTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task TheWholeFlowCompletesWithoutPullingInAnyNetworkingAssembly()
    {
        var provider = DeterministicProviderTests.Provider();

        var t4 = new[]
        {
            DeterministicDocumentIntelligenceProvider.DocumentHeader,
            "KIND: T4",
            "YEAR: 2025",
            "EMPLOYER-NAME: Acme Manufacturing",
            "EMPLOYEE-NAME: Casey Taxpayer",
            "BOX 12: 123456789",
            "BOX 14: 50000.00",
            "BOX 45: 1",
        };

        var receipt = new[]
        {
            DeterministicDocumentIntelligenceProvider.DocumentHeader,
            "KIND: MEDICAL-EXPENSE-EVIDENCE",
            "SERVICE-DATE: 2025-03-14",
            "GROSS-AMOUNT: $120.50",
            "PROVIDER-NAME: Bay Street Dental",
        };

        using var tracker = new AssemblyLoadTracker();

        Assert.NotNull(await provider.ClassifyAsync(
            DeterministicProviderTests.Document(t4), CancellationToken.None));
        Assert.NotNull(await provider.ExtractT4Async(
            DeterministicProviderTests.Document(t4), CancellationToken.None));
        Assert.NotNull(await provider.ClassifyAsync(
            DeterministicProviderTests.Document(receipt), CancellationToken.None));
        Assert.NotNull(await provider.ExtractMedicalAsync(
            DeterministicProviderTests.Document(receipt), CancellationToken.None));

        Assert.DoesNotContain(
            tracker.Loaded,
            name => name.StartsWith("System.Net", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTrackerWouldNoticeAnAssemblyBeingPulledIn()
    {
        // Without this, an empty load list would prove nothing: a tracker that
        // never fires is indistinguishable from a flow that loads nothing.
        using var tracker = new AssemblyLoadTracker();

        var context = new AssemblyLoadContext("probe", isCollectible: true);

        try
        {
            var loaded = context.LoadFromAssemblyPath(
                typeof(ExtractionWorkflow.Models.ExtractionVersion).Assembly.Location);

            Assert.NotNull(loaded);
            Assert.Contains("ExtractionWorkflow", tracker.Loaded);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void NoRepositoryAssemblyReachableFromTheAdapterNamesANetworkingAssembly()
    {
        // The walk follows this repository's own assemblies only. Chasing
        // framework facades would prove nothing: every .NET program can reach
        // System.Net.Primitives through the shared framework, so the question
        // worth asking is whether any code we wrote names one.
        var reachable = ReachableRepositoryAssemblies(
            typeof(DeterministicDocumentIntelligenceProvider).Assembly,
            typeof(ExtractionWorkflow.Models.ExtractionVersion).Assembly);

        var names = reachable.Select(assembly => assembly.GetName().Name!).ToArray();

        // The walk must have gone somewhere, or the assertion below is free.
        // The set is what the two modules actually name: a project reference
        // nothing names does not appear in a compiled reference list at all.
        Assert.Contains("ExtractionWorkflow", names);
        Assert.Contains("TaxLogicEngine", names);
        Assert.Contains("DocumentExtraction", names);
        Assert.Contains("Tenancy", names);

        var offenders = reachable
            .SelectMany(assembly => assembly.GetReferencedAssemblies()
                .Select(reference => $"{assembly.GetName().Name} -> {reference.Name}"))
            .Where(edge => edge.Contains("-> System.Net", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheCompiledAdapterNamesNoNetworkingAssembly()
    {
        var referenced = typeof(DeterministicDocumentIntelligenceProvider).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.DoesNotContain(
            referenced,
            name => name.StartsWith("System.Net", StringComparison.Ordinal));

        Assert.Contains("ExtractionWorkflow", referenced);
    }

    [Fact]
    public void NoProjectInTheRepositoryDeclaresAProviderOrTransportPackage()
    {
        // The adapter project is the only one that would ever be permitted such
        // a package, and in this specification it declares none — a boundary
        // with nothing behind it is the correct outcome here.
        var forbidden = new Regex(
            @"Azure|Amazon|AWSSDK|Google|OpenAI|Anthropic|Tesseract|\bOcr|Vision|FormRecognizer|CognitiveServices|HttpClient|RestSharp|Refit|Flurl",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        var offenders = new List<string>();
        var approvedExceptions = new List<string>();

        foreach (var project in RepositoryProjects())
        {
            foreach (var package in XDocument.Load(project)
                         .Descendants()
                         .Where(element => element.Name.LocalName == "PackageReference")
                         .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
                         .Where(name => forbidden.IsMatch(name)))
            {
                var edge = $"{Path.GetFileName(project)} -> {package}";
                if (ApprovedCloudPackageEdges.Contains(edge, StringComparer.Ordinal))
                {
                    approvedExceptions.Add(edge);
                    continue;
                }

                offenders.Add(edge);
            }
        }

        Assert.True(offenders.Count == 0, string.Join(", ", offenders));

        // Positive control and exactness: every approved edge still matches the
        // prohibition, so it is an explicit exception and not a scanner gap, and
        // each one actually exists exactly once.
        Assert.Equal(
            ApprovedCloudPackageEdges.OrderBy(edge => edge, StringComparer.Ordinal),
            approvedExceptions.OrderBy(edge => edge, StringComparer.Ordinal));
    }

    /// <summary>
    /// Spec 016 exceptions to the whole-repository cloud-package prohibition,
    /// each one project and one package, added only by the slice whose working
    /// code consumes it. T01: the Azure infrastructure library's managed-identity
    /// binding probe. The deterministic modules and this adapter stay cloud-free;
    /// no Document Intelligence, storage, OCR, LLM or HTTP-client package is
    /// approved anywhere.
    /// </summary>
    private static readonly string[] ApprovedCloudPackageEdges =
    {
        "TaxAI.AzureInfrastructure.csproj -> Azure.Identity",
    };

    [Fact]
    public void TheProjectScanActuallySeesTheRepositorysProjects()
    {
        var projects = RepositoryProjects().Select(Path.GetFileName).ToArray();

        Assert.Contains("DocumentIntelligence.csproj", projects);
        Assert.Contains("ExtractionWorkflow.csproj", projects);
        Assert.Contains("TaxLogicEngine.csproj", projects);
    }

    [Fact]
    public void TheAdapterProjectDeclaresOnlyTheDependencyInjectionAbstraction()
    {
        var packages = XDocument
            .Load(Path.Combine(RepositoryRoot, "src", "DocumentIntelligence", "DocumentIntelligence.csproj"))
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => (
                element.Attribute("Include")!.Value,
                element.Attribute("Version")!.Value))
            .ToArray();

        Assert.Equal(
            new[] { ("Microsoft.Extensions.DependencyInjection.Abstractions", "10.0.10") },
            packages);
    }

    [Fact]
    public void NoExecutableLineInEitherModuleNamesANetworkApi()
    {
        var network = new Regex(
            @"\bHttpClient\b|\bHttpRequestMessage\b|\bWebRequest\b|\bWebClient\b|\bSocket\b|\bDns\b|\bTcpClient\b|\bUdpClient\b|\bWebSocket\b|\bSmtpClient\b|\bnew\s+Uri\b",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        var offenders = new List<string>();

        foreach (var module in new[] { "DocumentIntelligence", "ExtractionWorkflow" })
        {
            foreach (var file in ProductionSourceFiles(module))
            {
                if (ExecutableLines(file).Any(line => network.IsMatch(line)))
                {
                    offenders.Add(Path.GetFileName(file));
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(", ", offenders));
    }

    [Fact]
    public void NoExecutableLineInEitherModuleLogsOrRetainsAProviderPayload()
    {
        // No prompt, request, response, or source byte is written anywhere, in
        // any environment: there is nothing here that could write one.
        var retention = new Regex(
            @"\bILogger\b|\bConsole\s*\.|\bTrace\s*\.|\bDebug\s*\.\s*Write|\bFile\s*\.|\bDirectory\s*\.|\bPath\s*\.\s*GetTempFileName\b|\bMemoryCache\b",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        var offenders = new List<string>();

        foreach (var module in new[] { "DocumentIntelligence", "ExtractionWorkflow" })
        {
            foreach (var file in ProductionSourceFiles(module))
            {
                if (ExecutableLines(file).Any(line => retention.IsMatch(line)))
                {
                    offenders.Add(Path.GetFileName(file));
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(", ", offenders));
    }

    // ------------------------------------------------------------- helpers

    private static IReadOnlyCollection<Assembly> ReachableRepositoryAssemblies(params Assembly[] roots)
    {
        var seen = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>(roots);

        while (pending.Count > 0)
        {
            var assembly = pending.Dequeue();

            if (!IsRepositoryAssembly(assembly) ||
                !seen.TryAdd(assembly.GetName().Name!, assembly))
            {
                continue;
            }

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (seen.ContainsKey(reference.Name!))
                {
                    continue;
                }

                try
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
                catch (Exception)
                {
                    // An assembly that cannot even be loaded is one this code
                    // cannot call. Its name is still checked as an edge below.
                }
            }
        }

        return seen.Values;
    }

    private static bool IsRepositoryAssembly(Assembly assembly) =>
        !string.IsNullOrEmpty(assembly.Location) &&
        assembly.Location.StartsWith(RepositoryRoot, StringComparison.OrdinalIgnoreCase);

    private static string[] RepositoryProjects() =>
        Directory.EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string[] ProductionSourceFiles(string module) =>
        Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", module), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
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

    /// <summary>
    /// Records every assembly the process pulls in while it is alive. What a
    /// flow loads is a fact about that flow; what the process already had
    /// loaded is a fact about the test host.
    /// </summary>
    private sealed class AssemblyLoadTracker : IDisposable
    {
        private readonly List<string> _loaded = new();

        internal AssemblyLoadTracker() =>
            AppDomain.CurrentDomain.AssemblyLoad += Record;

        internal IReadOnlyList<string> Loaded
        {
            get
            {
                lock (_loaded)
                {
                    return _loaded.ToArray();
                }
            }
        }

        public void Dispose() => AppDomain.CurrentDomain.AssemblyLoad -= Record;

        private void Record(object? sender, AssemblyLoadEventArgs args)
        {
            lock (_loaded)
            {
                _loaded.Add(args.LoadedAssembly.GetName().Name ?? string.Empty);
            }
        }
    }
}
