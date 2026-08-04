using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;
using TaxLogicEngine.Models;

namespace ExtractionWorkflow.Tests.Models;

/// <summary>
/// Spec 015 T02 — the SIN reaches the engine and reaches nothing else.
/// <para>
/// Box 12 legitimately exists in memory for the duration of one call, because
/// the existing parser cannot validate a T4 without it. Everything here plants
/// a real nine-digit value and then looks for it in every place this module
/// could put it: a result shape, a <c>ToString()</c>, an error message, and a
/// direct serialization.
/// </para>
/// <para>
/// A nine-consecutive-digit sweep is the assertion rather than an equality
/// check on the planted string. It catches a partial rendering, a rearranged
/// one, and a value nobody planted deliberately.
/// </para>
/// </summary>
public sealed class SinInvariantTests
{
    private const string PlantedSin = "123456789";

    private static readonly Regex NineDigitRun =
        new(@"\d{9}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Assembly ModuleAssembly = typeof(ExtractionVersion).Assembly;

    // -------------------------------------------- the planted value's journey

    [Fact]
    public void ThePlantedSinReachesTheEnginePayloadAndOnlyThere()
    {
        var provider = ProviderResultCarryingTheSin();
        string? observedInsideTheCall = null;

        var result = ExtractionPayloadBuilder.Build(provider, payload =>
        {
            // Box 12 is present for the parser, which is the whole point.
            observedInsideTheCall = payload.Boxes
                .Single(box => box.Code == T4BoxCodes.SocialInsuranceNumber)
                .Value;

            return ParseResult<string>.Success("processed");
        });

        Assert.Equal(PlantedSin, observedInsideTheCall);
        Assert.True(result.IsSuccess);

        // ...and the payload is not what came back out.
        Assert.Equal("processed", result.Value);
        Assert.DoesNotMatch(NineDigitRun, result.Value!);
    }

    [Fact]
    public void ThePlantedSinReachesNoResultShape()
    {
        var provider = ProviderResultCarryingTheSin();

        var kept = new List<ExtractionBoxValue>();

        foreach (var box in provider.Boxes!)
        {
            if (ExtractionBoxValue.TryCreate(box!.Code, box.Value, out var boxValue))
            {
                kept.Add(boxValue!);
            }
        }

        // The box loop that would have persisted the SIN cannot: the type
        // refused it, so nothing downstream had to remember to.
        Assert.Equal(2, kept.Count);
        Assert.DoesNotContain(kept, boxValue => boxValue.Value == PlantedSin);
        Assert.All(kept, boxValue => Assert.DoesNotMatch(NineDigitRun, boxValue.Value ?? string.Empty));
    }

    [Fact]
    public void NoToStringOnAnyPublicTypeEmitsThePlantedSin()
    {
        var provider = ProviderResultCarryingTheSin();

        var rendered = new List<string>
        {
            provider.ToString(),
            provider.Boxes!.Single(box => box!.Code == T4BoxCodes.SocialInsuranceNumber)!.ToString(),
            ResultShapeTests.SampleExtractionVersion().ToString(),
            ResultShapeTests.SampleMedicalVersion(
                grossAmountAsPrinted: PlantedSin,
                receiptReference: PlantedSin).ToString(),
            new ProviderMedicalResult { GrossAmountAsPrinted = PlantedSin }.ToString(),
            new ProviderClassificationResult { ProviderLabel = PlantedSin }.ToString(),
        };

        Assert.All(rendered, text => Assert.DoesNotMatch(NineDigitRun, text));
    }

    [Fact]
    public void SerializingAnyResultShapeEmitsNoNineDigitRun()
    {
        var payloads = new object[]
        {
            ProviderResultCarryingTheSin(),
            ResultShapeTests.SampleExtractionVersion(),
            ResultShapeTests.SampleMedicalVersion(
                grossAmountAsPrinted: PlantedSin,
                serviceDateAsPrinted: PlantedSin,
                providerDisplayName: PlantedSin,
                patientDisplayName: PlantedSin,
                receiptReference: PlantedSin),
            new ProviderMedicalResult { GrossAmountAsPrinted = PlantedSin },
        };

        foreach (var payload in payloads)
        {
            Assert.DoesNotMatch(NineDigitRun, JsonSerializer.Serialize(payload));
        }
    }

    [Fact]
    public void NoErrorMessageCanCarryAValue()
    {
        var refused = ExtractionPayloadBuilder.Build<string>(
            new ProviderT4Result
            {
                EmployerName = PlantedSin,
                Boxes = new ProviderBoxValue?[]
                {
                    new() { Code = null, Value = PlantedSin },
                },
            },
            _ => throw new InvalidOperationException("The continuation must not run."));

        Assert.True(refused.IsFailure);

        foreach (var error in refused.Errors)
        {
            Assert.DoesNotMatch(NineDigitRun, error.Message);
            Assert.DoesNotMatch(NineDigitRun, error.Field);
            Assert.DoesNotContain(PlantedSin, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryDeclaredErrorMessageIsAFixedConstantWithNoFormattingHole()
    {
        var messages = typeof(ExtractionWorkflowErrors)
            .GetFields(BindingFlags.Static | BindingFlags.Public)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(messages);

        foreach (var message in messages)
        {
            // No formatting hole means no way to interpolate a caller value in.
            Assert.DoesNotContain("{", message, StringComparison.Ordinal);
            Assert.DoesNotMatch(NineDigitRun, message);
        }
    }

    // ---------------------------------------- no SIN concept exists at all

    [Theory]
    [InlineData("Sin")]
    [InlineData("SocialInsurance")]
    [InlineData("Masked")]
    [InlineData("Redact")]
    [InlineData("Scrub")]
    [InlineData("Hash")]
    [InlineData("Token")]
    public void NoPublicMemberNamesASinOrAWayToDisguiseOne(string forbidden)
    {
        var offenders = new List<string>();

        foreach (var type in ModuleAssembly.GetExportedTypes())
        {
            if (NamesConcept(type.Name, forbidden))
            {
                offenders.Add(type.Name);
            }

            foreach (var member in type.GetMembers(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                // The one deliberate mention is the invariant that refuses the
                // SIN box: it names the engine's constant so the two cannot
                // drift, and it produces no instance.
                if (type == typeof(ExtractionBoxValue) &&
                    member.Name == nameof(ExtractionBoxValue.IsSocialInsuranceNumberBox))
                {
                    continue;
                }

                if (NamesConcept(member.Name, forbidden))
                {
                    offenders.Add($"{type.Name}.{member.Name}");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(", ", offenders));
    }

    /// <summary>
    /// Whether an identifier names a forbidden concept. "Sin" is matched as a
    /// PascalCase word rather than a substring, because "Missing" contains those
    /// three letters and a scan that cannot tell the difference is a scan
    /// nobody can write an honest member name around.
    /// </summary>
    private static bool NamesConcept(string name, string forbidden) =>
        forbidden == "Sin"
            ? Regex.IsMatch(
                name,
                @"(?:(?<![A-Za-z])|(?<=[a-z0-9]))(?:Sin|SIN)(?![a-z])",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1))
            : name.Contains(forbidden, StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("Missing", false)]
    [InlineData("Business", false)]
    [InlineData("Sin", true)]
    [InlineData("SIN", true)]
    [InlineData("SinValue", true)]
    [InlineData("TaxpayerSin", true)]
    [InlineData("MaskedSinDigits", true)]
    public void TheIdentifierScanTellsAWordFromASubstring(string name, bool expected)
    {
        // Without this, the sweep above would either pass for free or forbid
        // ordinary English.
        Assert.Equal(expected, NamesConcept(name, "Sin"));
    }

    [Fact]
    public void TheModuleNeverNamesTheEnginesMaskedIdentifier()
    {
        // Masking stays the engine's job, in memory, and nothing here projects,
        // persists, renders, or constructs one.
        Assert.DoesNotContain(
            ModuleAssembly.GetExportedTypes()
                .SelectMany(type => type.GetProperties())
                .Select(property => property.PropertyType.Name),
            name => name.Contains("RecipientIdentifier", StringComparison.Ordinal));
    }

    [Fact]
    public void NoPublicMemberCarriesASinPresenceSignal()
    {
        // "A SIN was present" is itself a fact about the taxpayer's document
        // that no browser model, column, projection, or audit row may carry.
        var offenders = ModuleAssembly.GetExportedTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => $"{type.Name}.{property.Name}"))
            .Where(name =>
                name.Contains("HasSin", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("SinPresent", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("SinMissing", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static ProviderT4Result ProviderResultCarryingTheSin() =>
        new()
        {
            Year = 2025,
            EmployerName = "Acme Manufacturing",
            EmployeeName = "Casey Taxpayer",
            Boxes = new ProviderBoxValue?[]
            {
                new() { Code = T4BoxCodes.SocialInsuranceNumber, Value = PlantedSin },
                new() { Code = "14", Value = "50000.00" },
                new() { Code = "45", Value = "1" },
            },
        };
}
