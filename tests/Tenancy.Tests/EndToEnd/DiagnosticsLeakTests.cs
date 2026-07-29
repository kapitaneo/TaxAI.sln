using System.Text.RegularExpressions;

namespace Tenancy.Tests.EndToEnd;

/// <summary>
/// Spec 011 AC-91 — audits the complete production path that can handle a
/// persisted review record or authenticated identity. Save, read, and sign-in
/// have no diagnostic sink. Purge has the repository's one approved logger,
/// whose fixed template and arguments are structural.
/// </summary>
public sealed class DiagnosticsLeakTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly Regex DiagnosticSink = new(
        @"\bILogger(?:<|\b)|\bLoggerMessage\s*\.\s*Define\b|"
        + @"\bLog(?:Trace|Debug|Information|Warning|Error|Critical)\s*\(|"
        + @"\bConsole\s*\.\s*Write|\bTrace\s*\.\s*Write|\bDebug\s*\.\s*Write|"
        + @"\bEventSource\b|\bActivitySource\b|\bMeter\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly IReadOnlyDictionary<string, string[]> WorkflowFiles =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["save"] =
            [
                "src/Tenancy/Services/ReviewPackageProjection.cs",
                "src/Persistence/Repositories/SqlReviewRecordRepository.cs",
            ],
            ["read"] =
            [
                "src/Persistence/Repositories/SqlReviewRecordRepository.cs",
            ],
            ["sign-in"] =
            [
                "src/TaxAI.Portal/PortalIdentity.cs",
                "src/TaxAI.Portal/SessionTicketStore.cs",
            ],
            ["purge"] =
            [
                "src/Persistence/Services/SqlReviewRecordPurgeService.cs",
            ],
        };

    [Fact]
    public void TheDiagnosticsAuditCoversTheFullSaveReadSignInAndPurgeWorkflow()
    {
        Assert.Equal(
            new[] { "purge", "read", "save", "sign-in" },
            WorkflowFiles.Keys.OrderBy(key => key, StringComparer.Ordinal));

        Assert.All(
            WorkflowFiles.SelectMany(entry => entry.Value),
            relativePath => Assert.True(
                File.Exists(Absolute(relativePath)),
                $"{relativePath} is part of the audited workflow and must exist."));
    }

    [Theory]
    [InlineData("save")]
    [InlineData("read")]
    [InlineData("sign-in")]
    public void SaveReadAndSignInDeclareNoDiagnosticSink(string stage)
    {
        var matches = WorkflowFiles[stage]
            .SelectMany(relativePath => ExecutableLines(Absolute(relativePath))
                .Where(line => DiagnosticSink.IsMatch(line))
                .Select(line => $"{relativePath}: {line}"))
            .ToArray();

        Assert.True(
            matches.Length == 0,
            $"{stage} declares a diagnostic sink: {string.Join(" | ", matches)}");
    }

    [Fact]
    public void PurgeIsTheOnlyLoggerAndItsTemplateAndArgumentsAreStructural()
    {
        var persistenceAndPortalSources = new[]
            {
                "src/Tenancy",
                "src/Persistence",
                "src/TaxAI.Portal",
            }
            .SelectMany(relativeRoot => Directory.EnumerateFiles(
                Absolute(relativeRoot),
                "*.cs",
                SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .Select(path => new
            {
                Path = path,
                Lines = ExecutableLines(path).ToArray(),
            })
            .Where(source => source.Lines.Any(line => DiagnosticSink.IsMatch(line)))
            .ToArray();

        var loggingSource = Assert.Single(persistenceAndPortalSources);
        Assert.EndsWith(
            "src/Persistence/Services/SqlReviewRecordPurgeService.cs",
            Path.GetRelativePath(RepositoryRoot, loggingSource.Path).Replace('\\', '/'),
            StringComparison.Ordinal);

        var text = File.ReadAllText(loggingSource.Path);

        Assert.Contains(
            "\"Purged {RecordsDeleted} review records and {SlipsDeleted} slips for firm {FirmId}.\"",
            text,
            StringComparison.Ordinal);
        Assert.Single(
            Regex.Matches(text, @"LoggerMessage\s*\.\s*Define").Cast<Match>());
        Assert.Equal(2, Regex.Matches(text, @"PurgeCompleted\s*\(\s*_logger\s*,").Count);

        foreach (var forbidden in new[]
                 {
                     "TaxpayerDisplayName",
                     "EmployerDisplayName",
                     "BoxCode",
                     "LineCode",
                     "SessionId",
                     "ConnectionString",
                     "Request.",
                     "ClaimsPrincipal",
                 })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
        }

        Assert.Matches(
            new Regex(
                @"PurgeCompleted\s*\(\s*_logger\s*,\s*0\s*,\s*0\s*,\s*firm\.Value\s*,\s*null\s*\)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            text);
        Assert.Matches(
            new Regex(
                @"PurgeCompleted\s*\(\s*_logger\s*,\s*recordsDeleted\s*,\s*slipsDeleted\s*,\s*firm\.Value\s*,\s*null\s*\)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            text);
    }

    [Fact]
    public void RuntimePurgeLoggingTestsExerciseBothStructuralOutcomes()
    {
        var runtimeTests = File.ReadAllText(Absolute(
            "tests/Persistence.Tests/Services/PurgeDiagnosticsTests.cs"));

        Assert.Contains(
            "CompletedPurgeLogsOnlyStructuralCountsAndTheOpaqueFirmIdentifier",
            runtimeTests,
            StringComparison.Ordinal);
        Assert.Contains(
            "NoEligibleRecordLogContainsOnlyZeroCountsAndTheOpaqueFirmIdentifier",
            runtimeTests,
            StringComparison.Ordinal);
        Assert.Contains("RecordingLogger<", runtimeTests, StringComparison.Ordinal);
    }

    private static string Absolute(string relativePath) =>
        Path.Combine(
            RepositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

    private static bool IsBuildOutput(string path) =>
        path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) ||
        path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find the repository root containing TaxAI.sln.");
    }
}
