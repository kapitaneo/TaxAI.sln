using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CaseProcessing.Models;
using CaseProcessing.Services;
using CaseReview.Models;
using CaseReview.Services;
using DocumentExtraction.Models;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;

namespace CaseReview.Tests.EndToEnd;

public sealed class ReviewPackagePiiTests
{
    private static readonly Regex NineConsecutiveDigits = new(
        @"\d{9}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static TheoryData<string, string> PlantMatrix()
    {
        var data = new TheoryData<string, string>();

        foreach (var slot in ReviewPipelineFixture.PlantSlots)
        {
            foreach (var plant in ReviewPipelineFixture.Plants)
            {
                data.Add(slot, plant);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PlantMatrix))]
    public void PublicPipelineMatrix_NoPlantOrNineDigitRunEscapes(
        string slot,
        string plant)
    {
        var accepted = ReviewPipelineFixture.Accepted(slot, plant);
        AssertSafeSurfaces(
            plant,
            RenderedSurfaces(
                accepted.UnsignedPackage,
                accepted.Descriptor,
                Array.Empty<ValidationError>()));
        AssertSafeSurfaces(
            plant,
            RenderedSurfaces(
                accepted.SignedPackage,
                accepted.Descriptor,
                Array.Empty<ValidationError>()));

        foreach (var family in ReviewPipelineFixture.AssemblyFailureFamilies)
        {
            var failure = ReviewPipelineFixture.AssemblyFailure(
                family,
                slot,
                plant);
            Assert.True(
                failure.Result.IsFailure,
                $"{family} must be a real assembly rejection.");
            Assert.Null(failure.Result.Value);
            Assert.NotEmpty(failure.Result.Errors);

            AssertSafeSurfaces(
                plant,
                RenderedSurfaces(
                    package: null,
                    ReviewPipelineFixture.Descriptor(slot, plant),
                    failure.Result.Errors));
        }

        foreach (var family in ReviewPipelineFixture.DecisionFailureFamilies)
        {
            var failure = ReviewPipelineFixture.DecisionFailure(
                family,
                slot,
                plant);
            Assert.True(
                failure.Result.IsFailure,
                $"{family} must be a real decision rejection.");
            Assert.Null(failure.Result.Value);
            Assert.NotEmpty(failure.Result.Errors);

            AssertSafeSurfaces(
                plant,
                RenderedSurfaces(
                    failure.UnsignedPackage,
                    failure.Descriptor,
                    failure.Result.Errors));
            AssertSafeSurfaces(
                plant,
                RenderedSurfaces(
                    failure.TargetPackage,
                    failure.Descriptor,
                    failure.Result.Errors));
        }
    }

    [Theory]
    [MemberData(nameof(PlantMatrix))]
    public void PublicPipelineMatrix_EveryAcceptedPlantIsReachableInMemory(
        string slot,
        string plant)
    {
        var accepted = ReviewPipelineFixture.Accepted(slot, plant);
        var reviewed = Assert.Single(
            accepted.SignedPackage.ReviewedSlips);
        var signOff = Assert.IsType<ReviewSignOff>(
            accepted.SignedPackage.SignOff);

        var actual = slot switch
        {
            "Box12Value" =>
                reviewed.Slip.Sin!.Equals(new RecipientIdentifier(plant))
                    ? plant
                    : "<not-carried>",
            "OtherBoxValue" =>
                reviewed.Slip.RppDpspRegistrationNumber,
            "EmployerName" => reviewed.Slip.EmployerName,
            "EmployerAddress" => reviewed.Slip.EmployerAddress,
            "EmployeeName" => reviewed.Slip.EmployeeName,
            "EmployeeAddress" => reviewed.Slip.EmployeeAddress,
            "DocumentId" => reviewed.DocumentId,
            "FirmId" => accepted.SignedPackage.FirmId,
            "CaseId" => accepted.SignedPackage.CaseId,
            "ParticipantId" => accepted.SignedPackage.ParticipantId,
            "SourceReference" => reviewed.SourceReference,
            "ReviewerReference" => signOff.ReviewerReference,
            "DecidedAtReference" => signOff.DecidedAtReference,
            "Note" => signOff.Note,
            _ => throw new ArgumentOutOfRangeException(
                nameof(slot),
                slot,
                "Unknown plant slot."),
        };

        Assert.Equal(plant, actual);
    }

    [Fact]
    public void PlantMatrix_CoversEveryRequiredSlotPlantAndRejectionFamily()
    {
        Assert.Equal(5, ReviewPipelineFixture.Plants.Length);
        Assert.Equal(14, ReviewPipelineFixture.PlantSlots.Length);
        Assert.Equal(70, PlantMatrix().Count());
        Assert.Equal(9, ReviewPipelineFixture.AssemblyFailureFamilies.Length);
        Assert.Equal(10, ReviewPipelineFixture.DecisionFailureFamilies.Length);

        Assert.Contains("123456789", ReviewPipelineFixture.Plants);
        Assert.Contains("123-456-789", ReviewPipelineFixture.Plants);
        Assert.Contains("123 456 789", ReviewPipelineFixture.Plants);
        Assert.Contains("ZZMARKERZZ", ReviewPipelineFixture.Plants);
        Assert.Contains("<<leak-me>>", ReviewPipelineFixture.Plants);

        Assert.All(
            ReviewPipelineFixture.PlantSlots,
            slot => Assert.False(string.IsNullOrWhiteSpace(slot)));

        foreach (var family in ReviewPipelineFixture.AssemblyFailureFamilies)
        {
            var result = ReviewPipelineFixture.AssemblyFailure(family).Result;
            Assert.True(result.IsFailure);
            Assert.Null(result.Value);
            Assert.NotEmpty(result.Errors);
        }

        foreach (var family in ReviewPipelineFixture.DecisionFailureFamilies)
        {
            var result = ReviewPipelineFixture.DecisionFailure(family).Result;
            Assert.True(result.IsFailure);
            Assert.Null(result.Value);
            Assert.NotEmpty(result.Errors);
        }
    }

    [Fact]
    public void CompactSin_ReachesReviewOnlyAsTheMaskedRecipientIdentifier()
    {
        const string fullSin = "123456789";
        var accepted = ReviewPipelineFixture.Accepted(
            "Box12Value",
            fullSin);
        var reviewed = Assert.Single(
            accepted.SignedPackage.ReviewedSlips);

        var identifier = Assert.IsType<RecipientIdentifier>(
            reviewed.Slip.Sin);
        Assert.Equal("***-**-789", identifier.Masked);
        Assert.Equal("***-**-789", identifier.ToString());

        var graph = PublicGraph(accepted.SignedPackage).ToArray();
        Assert.Contains(identifier, graph);
        Assert.Single(graph.OfType<RecipientIdentifier>());
        Assert.DoesNotContain(graph, value => value is T4RawInput);
        Assert.DoesNotContain(graph, value => value is T4RawBox);
        Assert.DoesNotContain(graph, value => value is ExtractionEnvelope);
        Assert.DoesNotContain(
            graph,
            value => value.GetType().FullName ==
                "DocumentExtraction.Models.ValidatedExtraction");
        Assert.DoesNotContain(
            graph,
            value => value.GetType().FullName ==
                "CaseProcessing.Models.ProcessedT4Extraction");

        AssertSafeSurfaces(
            fullSin,
            RenderedSurfaces(
                accepted.SignedPackage,
                accepted.Descriptor,
                Array.Empty<ValidationError>()));
    }

    [Fact]
    public void PublicPipeline_ExactDefensiveJsonShapesOmitEveryCallerString()
    {
        var accepted = ReviewPipelineFixture.Accepted(
            "ReviewerReference",
            "REVIEWER-MARKER-123456789");
        var reviewed = Assert.Single(
            accepted.SignedPackage.ReviewedSlips);
        var amount = Assert.Single(
            reviewed.BoxAmounts,
            item => item.BoxCode == T4BoxCodes.EmploymentIncome);
        var signOff = accepted.SignedPackage.SignOff!;

        Assert.Equal(
            """{"TaxYear":2025}""",
            JsonSerializer.Serialize(accepted.SignedPackage));
        Assert.Equal(
            """{"TaxYear":2025}""",
            JsonSerializer.Serialize(reviewed));
        Assert.Equal("{}", JsonSerializer.Serialize(amount));
        Assert.Equal(
            """{"Decision":2,"UnmappedBoxesAcknowledged":null}""",
            JsonSerializer.Serialize(accepted.Descriptor));
        Assert.Equal(
            """{"Decision":2,"UnmappedBoxesAcknowledged":null}""",
            JsonSerializer.Serialize(signOff));
    }

    [Fact]
    public void EveryCallerControlledCaseReviewString_IsJsonIgnored()
    {
        AssertJsonIgnored<AccountantReviewPackage>(
            nameof(AccountantReviewPackage.FirmId),
            nameof(AccountantReviewPackage.CaseId),
            nameof(AccountantReviewPackage.ParticipantId));
        AssertJsonIgnored<ReviewedSlip>(
            nameof(ReviewedSlip.DocumentId),
            nameof(ReviewedSlip.SourceReference));
        AssertJsonIgnored<ReviewDecisionDescriptor>(
            nameof(ReviewDecisionDescriptor.ReviewerReference),
            nameof(ReviewDecisionDescriptor.DecidedAtReference),
            nameof(ReviewDecisionDescriptor.Note));
        AssertJsonIgnored<ReviewSignOff>(
            nameof(ReviewSignOff.ReviewerReference),
            nameof(ReviewSignOff.DecidedAtReference),
            nameof(ReviewSignOff.Note));
    }

    [Fact]
    public void InvalidNonNumericAmount_CannotReachCaseReviewOrEscapeInFailureOutput()
    {
        const string invalidAmount = "<<leak-me>>";
        var envelope = ReviewPipelineFixture.Envelope(
            payload: ReviewPipelineFixture.Payload(
                amountBoxes:
                [
                    new T4RawBox(
                        T4BoxCodes.UnionDues,
                        invalidAmount),
                ]));
        var validated = ExtractionEnvelopeValidator.Validate(envelope);
        Assert.True(validated.IsSuccess);
        var processing = new T4CaseProcessor(
            ReviewPipelineFixture.Embedded)
            .Process(validated.Value);

        Assert.True(processing.IsFailure);
        Assert.Null(processing.Value);
        Assert.NotEmpty(processing.Errors);
        AssertSafeSurfaces(
            invalidAmount,
            processing.Errors.SelectMany(
                error => new[]
                {
                    error.Field,
                    error.Message,
                    error.ToString(),
                }));

        var valid = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(documentId: "valid-document"));
        ProcessedT4Extraction?[] attemptedInputs =
        [
            valid.Processed,
            processing.Value,
        ];
        var review = ReviewPackageAssembler.Assemble(attemptedInputs);

        Assert.True(review.IsFailure);
        Assert.Null(review.Value);
        Assert.Equal(
            ReviewFields.Extraction,
            Assert.Single(review.Errors).Field);
        AssertSafeSurfaces(
            invalidAmount,
            review.Errors.SelectMany(
                error => new[]
                {
                    error.Field,
                    error.Message,
                    error.ToString(),
                }));
    }

    private static IEnumerable<string> RenderedSurfaces(
        AccountantReviewPackage? package,
        ReviewDecisionDescriptor? descriptor,
        IReadOnlyList<ValidationError> errors)
    {
        foreach (var error in errors)
        {
            yield return error.Field;
            yield return error.Message;
            yield return error.ToString();
        }

        if (descriptor is not null)
        {
            yield return descriptor.ToString();
            yield return JsonSerializer.Serialize(descriptor);
        }

        if (package is null)
        {
            yield break;
        }

        yield return package.ToString();
        yield return JsonSerializer.Serialize(package);

        foreach (var reviewed in package.ReviewedSlips)
        {
            yield return reviewed.ToString();
            yield return JsonSerializer.Serialize(reviewed);

            foreach (var amount in reviewed.BoxAmounts)
            {
                yield return amount.ToString();
                yield return JsonSerializer.Serialize(amount);
            }
        }

        if (package.SignOff is not null)
        {
            yield return package.SignOff.ToString();
            yield return JsonSerializer.Serialize(package.SignOff);
        }
    }

    private static void AssertSafeSurfaces(
        string plant,
        IEnumerable<string> surfaces)
    {
        var materialized = surfaces.ToArray();
        Assert.NotEmpty(materialized);

        foreach (var surface in materialized)
        {
            Assert.DoesNotContain(plant, surface, StringComparison.Ordinal);
            Assert.DoesNotMatch(NineConsecutiveDigits, surface);
        }
    }

    private static IEnumerable<object> PublicGraph(object root)
    {
        var pending = new Stack<object>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        pending.Push(root);

        while (pending.Count > 0)
        {
            var value = pending.Pop();
            if (!visited.Add(value))
            {
                continue;
            }

            yield return value;

            if (value is string ||
                value.GetType().IsPrimitive ||
                value is decimal ||
                value.GetType().IsEnum)
            {
                continue;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is not null)
                    {
                        pending.Push(item);
                    }
                }

                continue;
            }

            foreach (var property in value.GetType().GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 ||
                    property.GetMethod is null)
                {
                    continue;
                }

                var propertyValue = property.GetValue(value);
                if (propertyValue is not null)
                {
                    pending.Push(propertyValue);
                }
            }
        }
    }

    private static void AssertJsonIgnored<T>(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = typeof(T).GetProperty(propertyName);
            Assert.NotNull(property);
            Assert.NotNull(
                property!.GetCustomAttribute<JsonIgnoreAttribute>());
        }
    }
}
