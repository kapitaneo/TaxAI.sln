using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DocumentIntelligence;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxAI.Portal.Tests.Startup;

/// <summary>
/// Spec 017 T02 — the local sample generator.
/// <para>
/// The generator is run for real, into a temporary directory. What it writes is
/// then handed to the real deterministic provider, the real payload builder,
/// and the real parser and mapper, which is the only meaningful proof that a
/// developer following the demo uploads something the chain accepts.
/// </para>
/// <para>
/// The T4 sample carries a recipient-identifier value assembled when the
/// generator runs. These tests read it back from the generated file only; it is
/// never a literal here, and no assertion message contains it.
/// </para>
/// </summary>
public sealed class LocalDemoSampleTests : IDisposable
{
    private const string T4SampleName = "t4-sample.pdf";

    private const string MedicalSampleName = "medical-sample.pdf";

    private const string RecipientIdentifierKey = "BOX 12";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string GeneratorPath =
        Path.Combine(RepositoryRoot, "scripts", "local", "New-LocalDemoSamples.ps1");

    private static readonly string StartupToolPath =
        Path.Combine(RepositoryRoot, "scripts", "local", "Start-LocalDemo.ps1");

    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), $"taxai-local-demo-samples-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    // -------------------------------------------------------- FR-25, FR-26

    [Fact] // FR-25
    public void TheGeneratorWritesExactlyTheTwoSamplesEachBehindTheUploadSignature()
    {
        _ = Generate();

        Assert.Equal(
            new[] { MedicalSampleName, T4SampleName },
            Directory.GetFiles(_workspace)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        foreach (var name in new[] { T4SampleName, MedicalSampleName })
        {
            var bytes = File.ReadAllBytes(Path.Combine(_workspace, name));

            // The first five bytes are what the unchanged upload gate checks.
            Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);

            var lines = Lines(name);
            Assert.StartsWith("%PDF-", lines[0], StringComparison.Ordinal);
            Assert.Equal(DeterministicDocumentIntelligenceProvider.DocumentHeader, lines[1]);
        }
    }

    [Fact] // FR-26
    public void TheT4SampleCarriesOnlyTheRequiredFieldsAndOneMappedAmountBox()
    {
        _ = Generate();
        var fields = Fields(T4SampleName);

        Assert.Equal(
            new[] { "KIND", "YEAR", "EMPLOYER-NAME", "EMPLOYEE-NAME", RecipientIdentifierKey, "BOX 14", "BOX 45" },
            fields.Select(field => field.Key).ToArray());
        Assert.Equal("2025", Value(fields, "YEAR"));
        Assert.Contains("Synthetic", Value(fields, "EMPLOYER-NAME"), StringComparison.Ordinal);
        Assert.Contains("Synthetic", Value(fields, "EMPLOYEE-NAME"), StringComparison.Ordinal);
    }

    [Fact] // FR-29
    public void TheMedicalSampleCarriesOnlyTheClosedMedicalFieldSet()
    {
        _ = Generate();
        var fields = Fields(MedicalSampleName);

        Assert.Equal(
            new[]
            {
                "KIND", "SERVICE-DATE", "GROSS-AMOUNT", "CURRENCY",
                "PROVIDER-NAME", "PATIENT-NAME", "RECEIPT-REFERENCE",
            },
            fields.Select(field => field.Key).ToArray());
        Assert.Contains("Synthetic", Value(fields, "PROVIDER-NAME"), StringComparison.Ordinal);
        Assert.Contains("Synthetic", Value(fields, "PATIENT-NAME"), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- AC-50

    [Theory] // AC-50
    [InlineData("artifacts/local-demo/" + T4SampleName)]
    [InlineData("artifacts/local-demo/" + MedicalSampleName)]
    public void TheDefaultSampleLocationIsIgnoredByGit(string path)
    {
        var (exitCode, output) = Run("git", "check-ignore", path);

        Assert.Equal(0, exitCode);
        Assert.Contains(path, output, StringComparison.Ordinal);
    }

    [Fact] // AC-50: the default really is that ignored directory, for both tools.
    public void BothToolsWriteSamplesOnlyIntoTheIgnoredLocalDemoDirectory()
    {
        var generator = File.ReadAllText(GeneratorPath);
        var startup = File.ReadAllText(StartupToolPath);

        Assert.Contains(
            "$OutputDirectory = Join-Path $repositoryRoot 'artifacts' 'local-demo'",
            generator,
            StringComparison.Ordinal);
        Assert.Contains(
            "$localDemoRoot = Join-Path $repositoryRoot 'artifacts' 'local-demo'",
            startup,
            StringComparison.Ordinal);
        Assert.Contains(
            "& $sampleGenerator -OutputDirectory $localDemoRoot",
            startup,
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------- AC-51, FR-27

    [Fact] // AC-51
    public void TheGeneratedRecipientIdentifierIsInNoTrackedFile()
    {
        _ = Generate();
        var value = Value(Fields(T4SampleName), RecipientIdentifierKey);

        Assert.True(Regex.IsMatch(value, @"^\d{9}$"), "The generated value has an unexpected shape.");

        // Searches the working-tree content of every tracked file. Only file
        // names could be printed, and none may be.
        var (exitCode, output) = Run("git", "grep", "-l", "-F", "-e", value);

        Assert.True(exitCode == 1 && output.Length == 0, "A tracked file contains a generated value.");
    }

    [Fact] // FR-27
    public void TheGeneratorPrintsNoValue()
    {
        var output = Generate();
        var value = Value(Fields(T4SampleName), RecipientIdentifierKey);

        Assert.False(
            output.Contains(value, StringComparison.Ordinal),
            "The generator printed a generated value.");
        Assert.DoesNotMatch(@"\d{9}", output);
        Assert.DoesNotContain(_workspace, output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // FR-27: the value comes from the random source when the tool runs.
    public void TheRecipientIdentifierIsAssembledAtRunTime()
    {
        var generator = File.ReadAllText(GeneratorPath);

        Assert.Contains("RandomNumberGenerator]::GetInt32(0, 10)", generator, StringComparison.Ordinal);
        Assert.Contains(
            "\"BOX 12: $(New-SyntheticRecipientIdentifier)\"",
            generator,
            StringComparison.Ordinal);
    }

    [Fact] // FR-25: generated once; a second run changes nothing unless asked.
    public void ASecondRunKeepsTheSamplesAndForceReplacesThem()
    {
        _ = Generate();
        var first = File.ReadAllBytes(Path.Combine(_workspace, T4SampleName));
        var medical = File.ReadAllBytes(Path.Combine(_workspace, MedicalSampleName));

        _ = Generate();
        Assert.Equal(first, File.ReadAllBytes(Path.Combine(_workspace, T4SampleName)));
        Assert.Equal(medical, File.ReadAllBytes(Path.Combine(_workspace, MedicalSampleName)));

        File.WriteAllText(Path.Combine(_workspace, T4SampleName), "replaced by hand");
        _ = Generate("-Force");

        Assert.Equal(
            DeterministicDocumentIntelligenceProvider.DocumentHeader,
            Lines(T4SampleName)[1]);
    }

    // ---------------------------------------------------------------- AC-52

    [Theory] // AC-52
    [InlineData("New-LocalDemoSamples.ps1")]
    [InlineData("Start-LocalDemo.ps1")]
    public void TheTrackedToolingHoldsNoNineDigitRun(string script)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "local", script));

        Assert.DoesNotMatch(@"\d{9}", text);
    }

    // ---------------------------------------------------------------- AC-53

    [Fact] // AC-53, FR-26
    public async Task TheGeneratedT4ClassifiesParsesAndMapsThroughTheExistingChain()
    {
        _ = Generate();
        var source = File.ReadAllBytes(Path.Combine(_workspace, T4SampleName));
        var provider = Provider();

        var classification = await provider.ClassifyAsync(
            new MemoryStream(source), CancellationToken.None);
        var extracted = await provider.ExtractT4Async(
            new MemoryStream(source), CancellationToken.None);

        Assert.Equal(DocumentClassification.T4Slip, classification!.Classification);
        Assert.NotNull(extracted);

        // The same in-memory payload path the workflow uses, into the existing
        // parser and the existing data-driven mapper.
        var parsed = ExtractionPayloadBuilder.Build(
            extracted, payload => new T4SlipParser().Parse(payload));

        Assert.True(parsed.IsSuccess, "The existing parser refused the generated sample.");
        Assert.Equal(2025, parsed.Value.Year);

        var mapped = new T4LineMapper(new EmbeddedT4LineMapProvider()).Map([parsed.Value]);

        Assert.True(mapped.IsSuccess, "The existing mapper refused the generated sample.");
        Assert.NotEmpty(mapped.Value.Lines);
        Assert.Empty(mapped.Value.UnmappedBoxCodes);
    }

    [Fact] // AC-53
    public async Task TheGeneratedMedicalSampleClassifiesAsMedicalEvidence()
    {
        _ = Generate();
        var source = File.ReadAllBytes(Path.Combine(_workspace, MedicalSampleName));
        var provider = Provider();

        var classification = await provider.ClassifyAsync(
            new MemoryStream(source), CancellationToken.None);
        var extracted = await provider.ExtractMedicalAsync(
            new MemoryStream(source), CancellationToken.None);

        Assert.Equal(DocumentClassification.MedicalExpenseEvidence, classification!.Classification);
        Assert.NotNull(extracted);
        Assert.NotNull(extracted!.ServiceDateAsPrinted);
        Assert.NotNull(extracted.GrossAmountAsPrinted);
    }

    // ---------------------------------------------------------------- AC-54

    [Fact] // AC-54
    public void TheMedicalSampleCarriesNoNineDigitRun()
    {
        _ = Generate();

        Assert.DoesNotMatch(
            @"\d{9}",
            File.ReadAllText(Path.Combine(_workspace, MedicalSampleName)));
    }

    // ----------------------------------------------------------------- helpers

    private string Generate(params string[] extra)
    {
        var arguments = new[]
            {
                "-NoProfile", "-NonInteractive", "-File", GeneratorPath,
                "-OutputDirectory", _workspace,
            }
            .Concat(extra)
            .ToArray();

        var (exitCode, output) = Run("pwsh", arguments);

        Assert.True(exitCode == 0, $"The sample generator failed ({exitCode}).");

        return output;
    }

    private string[] Lines(string sampleName) =>
        File.ReadAllText(Path.Combine(_workspace, sampleName), new UTF8Encoding(false, true))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Every field line after the signature and the header, in order.</summary>
    private KeyValuePair<string, string>[] Fields(string sampleName) =>
        Lines(sampleName)
            .Skip(2)
            .Select(line => line.Split(':', 2))
            .Select(parts => new KeyValuePair<string, string>(parts[0].Trim(), parts[1].Trim()))
            .ToArray();

    private static string Value(IEnumerable<KeyValuePair<string, string>> fields, string key) =>
        fields.Single(field => field.Key == key).Value;

    private static DeterministicDocumentIntelligenceProvider Provider() =>
        new(new DocumentIntelligenceOptions
        {
            MaximumSourceBytes = 64 * 1024,
            ProviderLabel = "deterministic-test-adapter",
        });

    private static (int ExitCode, string Output) Run(string fileName, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(3)), $"{fileName} did not finish.");

        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new InvalidOperationException("The repository root was not found.");
    }
}
