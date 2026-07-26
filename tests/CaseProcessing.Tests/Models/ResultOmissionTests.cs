using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaseProcessing.Models;
using TaxLogicEngine.Models;

namespace CaseProcessing.Tests.Models;

/// <summary>
/// The mandatory PII matrix at the constructed-value level (the T01 arm).
/// <para>
/// Three SIN-like strings and two non-numeric markers are planted
/// <b>separately</b> in each of eleven slots — the Box 12 value, another box's
/// value, the four employer/employee identity fields, and every caller-supplied
/// reference — and driven through a reflection-built
/// <see cref="ProcessedT4Extraction"/>. The non-numeric markers are required
/// because a digit-run regex cannot catch an echoed document id.
/// </para>
/// <para>
/// Each plant must stay readable in memory where the contract exposes it
/// programmatically (the <b>reachability guard</b>, so a mis-wired slot fails
/// loudly instead of passing vacuously), while no plant and no
/// nine-consecutive-digit run may appear in <c>ToString()</c> or in direct JSON
/// serialization. Omission means not even a masked form like <c>***-**-789</c>
/// appears; the one permitted SIN representation is the engine's
/// <see cref="RecipientIdentifier"/> reached via <see cref="T4Slip.Sin"/>.
/// </para>
/// </summary>
public sealed class ResultOmissionTests
{
    private const string Box12Value = "Box12Value";
    private const string OtherBoxValue = "OtherBoxValue";
    private const string DefaultSin = "000000000";

    private static readonly string[] Plants =
    {
        "123456789",
        "123-456-789",
        "123 456 789",
        "ZZMARKERZZ",
        "<<leak-me>>",
    };

    private static readonly string[] PlantSlots =
    {
        Box12Value,
        OtherBoxValue,
        "EmployerName",
        "EmployerAddress",
        "EmployeeName",
        "EmployeeAddress",
        "DocumentId",
        "FirmId",
        "CaseId",
        "ParticipantId",
        "SourceReference",
    };

    private static readonly Regex NineConsecutiveDigits =
        new(@"\d{9}", RegexOptions.None, TimeSpan.FromSeconds(1));

    public static TheoryData<string, string> Matrix()
    {
        var data = new TheoryData<string, string>();
        foreach (var slot in PlantSlots)
        {
            foreach (var plant in Plants)
            {
                data.Add(slot, plant);
            }
        }

        return data;
    }

    // --- reachability guard: a mis-wired slot must fail loudly ---

    [Theory]
    [MemberData(nameof(Matrix))]
    public void PlantedValue_StaysReadableWhereTheContractExposesIt(string slot, string plant)
    {
        var payload = BuildPayload(slot, plant);
        var result = BuildResult(slot, plant);

        switch (slot)
        {
            case Box12Value:
                // Box 12 is exposed only through the engine's masked identifier — the
                // one permitted SIN representation. The raw value stays on the payload.
                Assert.Equal(plant, payload.Boxes.Single(b => b.Code == "12").Value);
                Assert.Equal(new RecipientIdentifier(plant).Masked, result.Slip.Sin!.Masked);
                break;
            case OtherBoxValue:
                Assert.Equal(plant, payload.Boxes.Single(b => b.Code == "14").Value);
                Assert.Equal(plant, result.Slip.EmployerAccountNumber);
                break;
            case "EmployerName":
                Assert.Equal(plant, payload.EmployerName);
                Assert.Equal(plant, result.Slip.EmployerName);
                break;
            case "EmployerAddress":
                Assert.Equal(plant, payload.EmployerAddress);
                Assert.Equal(plant, result.Slip.EmployerAddress);
                break;
            case "EmployeeName":
                Assert.Equal(plant, payload.EmployeeName);
                Assert.Equal(plant, result.Slip.EmployeeName);
                break;
            case "EmployeeAddress":
                Assert.Equal(plant, payload.EmployeeAddress);
                Assert.Equal(plant, result.Slip.EmployeeAddress);
                break;
            case "DocumentId":
                Assert.Equal(plant, result.DocumentId);
                break;
            case "FirmId":
                Assert.Equal(plant, result.FirmId);
                break;
            case "CaseId":
                Assert.Equal(plant, result.CaseId);
                break;
            case "ParticipantId":
                Assert.Equal(plant, result.ParticipantId);
                break;
            case "SourceReference":
                Assert.Equal(plant, result.SourceReference);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown plant slot.");
        }
    }

    // --- omission from every rendered output ---

    [Theory]
    [MemberData(nameof(Matrix))]
    public void ResultToString_OmitsThePlantedValueEntirely(string slot, string plant)
    {
        var text = BuildResult(slot, plant).ToString();

        Assert.DoesNotContain(plant, text, StringComparison.Ordinal);
        Assert.DoesNotMatch(NineConsecutiveDigits, text);
        // Not even a masked form: ToString reports SIN presence, never the value.
        Assert.DoesNotContain(new RecipientIdentifier(plant).Masked, text, StringComparison.Ordinal);
        Assert.DoesNotContain(new RecipientIdentifier(DefaultSin).Masked, text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void ResultJson_OmitsThePlantedValueEntirely(string slot, string plant)
    {
        var json = JsonSerializer.Serialize(BuildResult(slot, plant));

        // Exact-shape assertion first: it cannot be defeated by JSON escaping (for
        // example "<<leak-me>>" would serialize as "<<leak-me>>").
        Assert.Equal("{\"TaxYear\":2025}", json);
        Assert.DoesNotContain(plant, json, StringComparison.Ordinal);
        Assert.DoesNotMatch(NineConsecutiveDigits, json);
        Assert.DoesNotContain(new RecipientIdentifier(plant).Masked, json, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void SlipAndMappingToString_OmitThePlantedValueEntirely(string slot, string plant)
    {
        // The engine objects are reachable programmatically; their own rendering must
        // not become a leak either. T4Slip.ToString is SIN-safe by spec 003.
        var result = BuildResult(slot, plant);

        var slipText = result.Slip.ToString();
        Assert.DoesNotContain(plant, slipText, StringComparison.Ordinal);
        Assert.DoesNotMatch(NineConsecutiveDigits, slipText);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void ClosedFieldConstantSet_ContainsNoPlantedValue(string slot, string plant)
    {
        Assert.NotEmpty(slot);

        // This module's ValidationError.Field values come only from the closed set,
        // so no caller or payload value can reach a Field name.
        foreach (var field in ContractTests.AllProcessingFieldValues())
        {
            Assert.DoesNotContain(plant, field, StringComparison.Ordinal);
            Assert.DoesNotMatch(NineConsecutiveDigits, field);
        }
    }

    // --- the raw payload never reaches the result ---

    [Fact]
    public void RawPayload_IsUnreachableFromTheResultsPublicSurface()
    {
        var result = BuildResult(Box12Value, "123456789");

        var reachable = result
            .GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.GetValue(result))
            .ToArray();

        Assert.DoesNotContain(reachable, value => value is T4RawInput or T4RawBox);
        Assert.DoesNotContain(reachable, value => value is IEnumerable<T4RawBox>);
    }

    [Fact]
    public void EveryPlantSlotAndPlantIsExercised()
    {
        // Scanner guard: the matrix must actually cover the mandated eleven slots and
        // five plants, so a truncated table cannot make the suite pass for free.
        Assert.Equal(11, PlantSlots.Length);
        Assert.Equal(5, Plants.Length);
        Assert.Equal(PlantSlots.Length * Plants.Length, Matrix().Count());
        Assert.Contains("ZZMARKERZZ", Plants);
        Assert.Contains("<<leak-me>>", Plants);
        Assert.All(PlantSlots, slot => Assert.False(string.IsNullOrWhiteSpace(slot)));
    }

    // --- builders ---

    private static ProcessedT4Extraction BuildResult(string slot, string plant)
    {
        string Plant(string name, string fallback) => slot == name ? plant : fallback;

        var slip = ContractTests.BuildSlip(
            employerName: Plant("EmployerName", "Employer Ltd"),
            employerAddress: Plant("EmployerAddress", "1 Employer Way"),
            employeeName: Plant("EmployeeName", "Employee Name"),
            employeeAddress: Plant("EmployeeAddress", "2 Employee Street"),
            employerAccountNumber: Plant(OtherBoxValue, "employer-account"),
            sin: Plant(Box12Value, DefaultSin));

        return ContractTests.CreateResult(
            Plant("DocumentId", "doc-1"),
            Plant("FirmId", "firm-1"),
            Plant("CaseId", "case-1"),
            Plant("ParticipantId", "participant-1"),
            Plant("SourceReference", "store/ref-1"),
            slip,
            T4MappingResult.Empty);
    }

    private static T4RawInput BuildPayload(string slot, string plant)
    {
        string Plant(string name, string fallback) => slot == name ? plant : fallback;

        return new T4RawInput
        {
            Year = 2025,
            EmployerName = Plant("EmployerName", "Employer Ltd"),
            EmployerAddress = Plant("EmployerAddress", "1 Employer Way"),
            EmployeeName = Plant("EmployeeName", "Employee Name"),
            EmployeeAddress = Plant("EmployeeAddress", "2 Employee Street"),
            Boxes = new[]
            {
                new T4RawBox("12", Plant(Box12Value, "value-12")),
                new T4RawBox("14", Plant(OtherBoxValue, "value-14")),
            },
        };
    }
}
