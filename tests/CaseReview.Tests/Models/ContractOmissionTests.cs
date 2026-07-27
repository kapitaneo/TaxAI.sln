using System.Text.Json;
using System.Text.RegularExpressions;
using CaseReview.Models;
using TaxLogicEngine.Models;

namespace CaseReview.Tests.Models;

/// <summary>
/// Constructed-value PII and omission coverage for the Wave-0 contract. The
/// public pipeline is implemented and tested by later tasks.
/// </summary>
public sealed class ContractOmissionTests
{
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
        "Box12",
        "BoxCode",
        "EmployerName",
        "EmployerAddress",
        "EmployeeName",
        "EmployeeAddress",
        "DocumentId",
        "FirmId",
        "CaseId",
        "ParticipantId",
        "SourceReference",
        "ReviewerReference",
        "DecidedAtReference",
        "Note",
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

    [Theory]
    [MemberData(nameof(Matrix))]
    public void AcceptedPlant_IsReachableOnlyThroughItsContractedInMemoryProperty(
        string slot,
        string plant)
    {
        var graph = BuildGraph(slot, plant);

        switch (slot)
        {
            case "Box12":
                Assert.Equal(
                    new RecipientIdentifier(plant).Masked,
                    graph.ReviewedSlip.Slip.Sin!.Masked);
                break;
            case "BoxCode":
                Assert.Equal(plant, graph.BoxAmount.BoxCode);
                Assert.Equal(plant, Assert.Single(graph.ReviewedSlip.UnmappedBoxCodes));
                Assert.Equal(
                    plant,
                    Assert.Single(Assert.Single(graph.ReviewedSlip.LineEntries).SourceBoxCodes));
                break;
            case "EmployerName":
                Assert.Equal(plant, graph.ReviewedSlip.Slip.EmployerName);
                break;
            case "EmployerAddress":
                Assert.Equal(plant, graph.ReviewedSlip.Slip.EmployerAddress);
                break;
            case "EmployeeName":
                Assert.Equal(plant, graph.ReviewedSlip.Slip.EmployeeName);
                break;
            case "EmployeeAddress":
                Assert.Equal(plant, graph.ReviewedSlip.Slip.EmployeeAddress);
                break;
            case "DocumentId":
                Assert.Equal(plant, graph.ReviewedSlip.DocumentId);
                break;
            case "FirmId":
                Assert.Equal(plant, graph.Package.FirmId);
                break;
            case "CaseId":
                Assert.Equal(plant, graph.Package.CaseId);
                break;
            case "ParticipantId":
                Assert.Equal(plant, graph.Package.ParticipantId);
                break;
            case "SourceReference":
                Assert.Equal(plant, graph.ReviewedSlip.SourceReference);
                break;
            case "ReviewerReference":
                Assert.Equal(plant, graph.Descriptor.ReviewerReference);
                Assert.Equal(plant, graph.SignOff.ReviewerReference);
                break;
            case "DecidedAtReference":
                Assert.Equal(plant, graph.Descriptor.DecidedAtReference);
                Assert.Equal(plant, graph.SignOff.DecidedAtReference);
                break;
            case "Note":
                Assert.Equal(plant, graph.Descriptor.Note);
                Assert.Equal(plant, graph.SignOff.Note);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown plant slot.");
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void EveryPublicRenderingAndJsonSurface_OmitsTheRawPlant(
        string slot,
        string plant)
    {
        var graph = BuildGraph(slot, plant);

        var outputs = new[]
        {
            graph.BoxAmount.ToString(),
            graph.ReviewedSlip.ToString(),
            graph.Descriptor.ToString(),
            graph.SignOff.ToString(),
            graph.Package.ToString(),
            JsonSerializer.Serialize(graph.BoxAmount),
            JsonSerializer.Serialize(graph.ReviewedSlip),
            JsonSerializer.Serialize(graph.Descriptor),
            JsonSerializer.Serialize(graph.SignOff),
            JsonSerializer.Serialize(graph.Package),
        };

        Assert.All(
            outputs,
            output =>
            {
                Assert.DoesNotContain(plant, output, StringComparison.Ordinal);
                Assert.DoesNotMatch(NineConsecutiveDigits, output);
            });

        foreach (var field in AllReviewFieldValues())
        {
            Assert.DoesNotContain(plant, field, StringComparison.Ordinal);
            Assert.DoesNotMatch(NineConsecutiveDigits, field);
        }
    }

    [Fact]
    public void DefensiveJson_HasOnlyTheApprovedSafeStructuralShape()
    {
        var graph = BuildGraph("None", "unused");

        Assert.Equal("{}", JsonSerializer.Serialize(graph.BoxAmount));
        Assert.Equal("{\"TaxYear\":2025}", JsonSerializer.Serialize(graph.ReviewedSlip));
        Assert.Equal(
            "{\"Decision\":1,\"UnmappedBoxesAcknowledged\":true}",
            JsonSerializer.Serialize(graph.Descriptor));
        Assert.Equal(
            "{\"Decision\":1,\"UnmappedBoxesAcknowledged\":true}",
            JsonSerializer.Serialize(graph.SignOff));
        Assert.Equal("{\"TaxYear\":2025}", JsonSerializer.Serialize(graph.Package));
    }

    [Fact]
    public void NineDigitAmount_RemainsReadableButNeverRendersOrSerializes()
    {
        const decimal amount = 123456789m;
        var boxAmount = new ReviewedBoxAmount("box-safe", amount);
        var slip = BuildReviewedSlip(BuildSlip(), boxAmount);
        var package = new AccountantReviewPackage(
            "firm-safe",
            "case-safe",
            "participant-safe",
            2025,
            new[] { slip },
            signOff: null);

        Assert.Equal(amount, boxAmount.Amount);

        foreach (var output in new[]
                 {
                     boxAmount.ToString(),
                     slip.ToString(),
                     package.ToString(),
                     JsonSerializer.Serialize(boxAmount),
                     JsonSerializer.Serialize(slip),
                     JsonSerializer.Serialize(package),
                 })
        {
            Assert.DoesNotContain("123456789", output, StringComparison.Ordinal);
            Assert.DoesNotMatch(NineConsecutiveDigits, output);
        }
    }

    [Fact]
    public void Matrix_ExercisesEveryRequiredPlantAndSlot()
    {
        Assert.Equal(5, Plants.Length);
        Assert.Equal(14, PlantSlots.Length);
        Assert.Equal(Plants.Length * PlantSlots.Length, Matrix().Count());
        Assert.Contains("123456789", Plants);
        Assert.Contains("123-456-789", Plants);
        Assert.Contains("123 456 789", Plants);
        Assert.Contains("ZZMARKERZZ", Plants);
        Assert.Contains("<<leak-me>>", Plants);
        Assert.All(PlantSlots, slot => Assert.False(string.IsNullOrWhiteSpace(slot)));
    }

    [Fact]
    public void RawPayloadTypes_AreNotReachableFromTheCaseReviewPublicSurface()
    {
        var publicPropertyTypes = typeof(AccountantReviewPackage)
            .Assembly
            .GetTypes()
            .Where(type => type.IsPublic)
            .SelectMany(type => type.GetProperties())
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.DoesNotContain(
            publicPropertyTypes,
            type => type.FullName is
                "TaxLogicEngine.Models.T4RawInput" or
                "TaxLogicEngine.Models.T4RawBox");
    }

    private static ReviewGraph BuildGraph(string slot, string plant)
    {
        string Plant(string name, string fallback) => slot == name ? plant : fallback;

        var slip = BuildSlip(
            sin: Plant("Box12", "000000000"),
            employerName: Plant("EmployerName", "Employer Safe"),
            employerAddress: Plant("EmployerAddress", "Employer Address Safe"),
            employeeName: Plant("EmployeeName", "Employee Safe"),
            employeeAddress: Plant("EmployeeAddress", "Employee Address Safe"));

        var boxCode = Plant("BoxCode", "box-safe");
        var boxAmount = new ReviewedBoxAmount(boxCode, 12.34m);
        var reviewedSlip = new ReviewedSlip(
            Plant("DocumentId", "document-safe"),
            2025,
            Plant("SourceReference", "source-safe"),
            slip,
            new[] { new TaxLineEntry("line-safe", 12.34m, new[] { boxCode }) },
            new[] { boxAmount },
            new[] { boxCode });

        var descriptor = new ReviewDecisionDescriptor(
            SignOffDecision.Approved,
            Plant("ReviewerReference", "reviewer-safe"),
            Plant("DecidedAtReference", "decided-safe"),
            Plant("Note", "note-safe"),
            unmappedBoxesAcknowledged: true);

        var signOff = new ReviewSignOff(
            descriptor.Decision,
            descriptor.ReviewerReference!,
            descriptor.DecidedAtReference!,
            descriptor.Note,
            descriptor.UnmappedBoxesAcknowledged);

        var package = new AccountantReviewPackage(
            Plant("FirmId", "firm-safe"),
            Plant("CaseId", "case-safe"),
            Plant("ParticipantId", "participant-safe"),
            2025,
            new[] { reviewedSlip },
            signOff);

        return new ReviewGraph(
            boxAmount,
            reviewedSlip,
            descriptor,
            signOff,
            package);
    }

    private static ReviewedSlip BuildReviewedSlip(
        T4Slip slip,
        ReviewedBoxAmount boxAmount) =>
        new(
            "document-safe",
            2025,
            "source-safe",
            slip,
            new[] { new TaxLineEntry("line-safe", boxAmount.Amount, new[] { boxAmount.BoxCode }) },
            new[] { boxAmount },
            new[] { boxAmount.BoxCode });

    private static T4Slip BuildSlip(
        string sin = "000000000",
        string employerName = "Employer Safe",
        string? employerAddress = "Employer Address Safe",
        string employeeName = "Employee Safe",
        string? employeeAddress = "Employee Address Safe") =>
        new()
        {
            Year = 2025,
            EmployerName = employerName,
            EmployerAddress = employerAddress,
            EmployeeName = employeeName,
            EmployeeAddress = employeeAddress,
            DentalBenefitCode = "1",
            Sin = new RecipientIdentifier(sin),
        };

    private static IEnumerable<string> AllReviewFieldValues() =>
        typeof(ReviewFields)
            .GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);

    private sealed record ReviewGraph(
        ReviewedBoxAmount BoxAmount,
        ReviewedSlip ReviewedSlip,
        ReviewDecisionDescriptor Descriptor,
        ReviewSignOff SignOff,
        AccountantReviewPackage Package);
}
