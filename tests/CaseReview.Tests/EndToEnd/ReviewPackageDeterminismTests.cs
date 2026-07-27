using CaseProcessing.Models;
using CaseReview.Models;
using CaseReview.Services;
using TaxLogicEngine.Models;

namespace CaseReview.Tests.EndToEnd;

public sealed class ReviewPackageDeterminismTests
{
    private const int ReplayCount = 5;

    public static TheoryData<string> AssemblyFamilies()
    {
        var data = new TheoryData<string>();
        foreach (var family in ReviewPipelineFixture.AssemblyFailureFamilies)
        {
            data.Add(family);
        }

        return data;
    }

    public static TheoryData<string> DecisionFamilies()
    {
        var data = new TheoryData<string>();
        foreach (var family in ReviewPipelineFixture.DecisionFailureFamilies)
        {
            data.Add(family);
        }

        return data;
    }

    [Fact]
    public void AcceptedPipeline_ReplaysFiveTimesWithEqualPackagesAndSignOffs()
    {
        var first = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(documentId: "document-a"));
        var second = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(documentId: "document-b"));
        var firstSnapshot = ReviewPipelineFixture.Snapshot(first);
        var secondSnapshot = ReviewPipelineFixture.Snapshot(second);
        var collections = Enumerable
            .Range(0, ReplayCount)
            .Select(
                _ => (IReadOnlyList<ProcessedT4Extraction?>)
                    new ProcessedT4Extraction?[]
                    {
                        first.Processed,
                        second.Processed,
                    })
            .ToArray();
        Assert.Equal(ReplayCount, collections.Length);
        Assert.Equal(
            ReplayCount,
            collections.Distinct(ReferenceEqualityComparer.Instance).Count());

        var assemblies = collections
            .Select(ReviewPackageAssembler.Assemble)
            .ToArray();

        Assert.Equal(ReplayCount, assemblies.Length);
        Assert.All(assemblies, result => Assert.True(result.IsSuccess));
        var baselinePackage = assemblies[0].Value!;
        Assert.All(
            assemblies,
            result =>
            {
                Assert.Empty(result.Errors);
                Assert.Equal(baselinePackage, result.Value);
                Assert.Equal(
                    baselinePackage.GetHashCode(),
                    result.Value!.GetHashCode());
            });

        var descriptors = Enumerable
            .Range(0, ReplayCount)
            .Select(
                _ => new ReviewDecisionDescriptor(
                    SignOffDecision.ChangesRequested,
                    "reviewer-1",
                    "opaque-decided-at",
                    "review-note",
                    unmappedBoxesAcknowledged: null))
            .ToArray();
        Assert.Equal(
            ReplayCount,
            descriptors.Distinct(ReferenceEqualityComparer.Instance).Count());

        var signed = descriptors
            .Select(baselinePackage.RecordDecision)
            .ToArray();

        Assert.Equal(ReplayCount, signed.Length);
        Assert.All(signed, result => Assert.True(result.IsSuccess));
        var baselineSigned = signed[0].Value!;
        Assert.All(
            signed,
            result =>
            {
                Assert.Equal(baselineSigned, result.Value);
                Assert.Equal(
                    baselineSigned.GetHashCode(),
                    result.Value!.GetHashCode());
                Assert.Equal(
                    baselineSigned.SignOff,
                    result.Value.SignOff);
                Assert.Equal(
                    baselinePackage.ReviewedSlips,
                    result.Value.ReviewedSlips);
            });

        Assert.Null(baselinePackage.SignOff);
        ReviewPipelineFixture.AssertUnchanged(firstSnapshot, first);
        ReviewPipelineFixture.AssertUnchanged(secondSnapshot, second);
    }

    [Theory]
    [MemberData(nameof(AssemblyFamilies))]
    public void EveryAssemblyRejection_ReplaysFiveTimesWithExactErrors(
        string family)
    {
        var failure = ReviewPipelineFixture.AssemblyFailure(family);
        var inputState = Capture(failure.Inputs);
        var results = new List<ParseResult<AccountantReviewPackage>>
        {
            failure.Result,
        };

        for (var replay = 1; replay < ReplayCount; replay++)
        {
            results.Add(
                ReviewPackageAssembler.Assemble(failure.Inputs));
        }

        Assert.Equal(ReplayCount, results.Count);
        var baseline = results[0];
        Assert.True(baseline.IsFailure);
        Assert.Null(baseline.Value);
        Assert.NotEmpty(baseline.Errors);

        Assert.All(
            results,
            result =>
            {
                Assert.True(result.IsFailure);
                Assert.Null(result.Value);
                Assert.Equal(baseline.Errors, result.Errors);
                Assert.Equal(
                    baseline.Errors.Select(error => error.Field),
                    result.Errors.Select(error => error.Field));
                Assert.Equal(
                    baseline.Errors.Select(error => error.Message),
                    result.Errors.Select(error => error.Message));
            });

        AssertInputState(inputState, failure.Inputs);
    }

    [Theory]
    [MemberData(nameof(DecisionFamilies))]
    public void EveryDecisionRejection_ReplaysFiveTimesWithExactErrors(
        string family)
    {
        var failure = ReviewPipelineFixture.DecisionFailure(family);
        var upstreamSnapshot = ReviewPipelineFixture.Snapshot(
            failure.Upstream);
        var targetSignOff = failure.TargetPackage.SignOff;
        var reviewedSlips = failure.TargetPackage.ReviewedSlips.ToArray();
        var descriptorState = DescriptorState.From(failure.Descriptor);
        var results = new List<ParseResult<AccountantReviewPackage>>
        {
            failure.Result,
        };

        for (var replay = 1; replay < ReplayCount; replay++)
        {
            results.Add(
                failure.TargetPackage.RecordDecision(
                    failure.Descriptor));
        }

        Assert.Equal(ReplayCount, results.Count);
        var baseline = results[0];
        Assert.True(baseline.IsFailure);
        Assert.Null(baseline.Value);
        Assert.NotEmpty(baseline.Errors);

        Assert.All(
            results,
            result =>
            {
                Assert.True(result.IsFailure);
                Assert.Null(result.Value);
                Assert.Equal(baseline.Errors, result.Errors);
                Assert.Equal(
                    baseline.Errors.Select(error => error.Field),
                    result.Errors.Select(error => error.Field));
                Assert.Equal(
                    baseline.Errors.Select(error => error.Message),
                    result.Errors.Select(error => error.Message));
            });

        Assert.Same(targetSignOff, failure.TargetPackage.SignOff);
        Assert.Equal(reviewedSlips, failure.TargetPackage.ReviewedSlips);
        for (var index = 0; index < reviewedSlips.Length; index++)
        {
            Assert.Same(
                reviewedSlips[index],
                failure.TargetPackage.ReviewedSlips[index]);
        }

        descriptorState.AssertUnchanged(failure.Descriptor);
        ReviewPipelineFixture.AssertUnchanged(
            upstreamSnapshot,
            failure.Upstream);
    }

    [Fact]
    public void DistinctEquivalentInputCollections_HaveEqualAcceptedAndRejectedBehavior()
    {
        var first = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(documentId: "document-a"));
        var second = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(documentId: "document-b"));
        IReadOnlyList<ProcessedT4Extraction?> array =
            new ProcessedT4Extraction?[]
            {
                first.Processed,
                second.Processed,
            };
        IReadOnlyList<ProcessedT4Extraction?> list =
            new List<ProcessedT4Extraction?>
            {
                first.Processed,
                second.Processed,
            };

        var acceptedArray = ReviewPackageAssembler.Assemble(array);
        var acceptedList = ReviewPackageAssembler.Assemble(list);

        Assert.True(acceptedArray.IsSuccess);
        Assert.True(acceptedList.IsSuccess);
        Assert.Equal(acceptedArray.Value, acceptedList.Value);
        Assert.Equal(acceptedArray.Errors, acceptedList.Errors);

        IReadOnlyList<ProcessedT4Extraction?> rejectedArray =
            new ProcessedT4Extraction?[]
            {
                first.Processed,
                null,
            };
        IReadOnlyList<ProcessedT4Extraction?> rejectedList =
            new List<ProcessedT4Extraction?>
            {
                first.Processed,
                null,
            };

        var failureArray = ReviewPackageAssembler.Assemble(rejectedArray);
        var failureList = ReviewPackageAssembler.Assemble(rejectedList);

        Assert.True(failureArray.IsFailure);
        Assert.True(failureList.IsFailure);
        Assert.Equal(failureArray.Errors, failureList.Errors);
        Assert.Null(failureArray.Value);
        Assert.Null(failureList.Value);
    }

    [Fact]
    public void MultiErrorOrdering_IsStableAcrossFivePublicPipelineReplays()
    {
        var assembly = ReviewPipelineFixture.AssemblyFailure(
            "MultipleDefects");
        var assemblyFields = Enumerable
            .Range(0, ReplayCount)
            .Select(
                _ => ReviewPackageAssembler
                    .Assemble(assembly.Inputs)
                    .Errors
                    .Select(error => error.Field)
                    .ToArray())
            .ToArray();
        Assert.All(
            assemblyFields,
            fields => Assert.Equal(assemblyFields[0], fields));
        Assert.True(assemblyFields[0].Length > 1);

        var decision = ReviewPipelineFixture.DecisionFailure(
            "MultipleDefects");
        var decisionFields = Enumerable
            .Range(0, ReplayCount)
            .Select(
                _ => decision.TargetPackage
                    .RecordDecision(decision.Descriptor)
                    .Errors
                    .Select(error => error.Field)
                    .ToArray())
            .ToArray();
        Assert.All(
            decisionFields,
            fields => Assert.Equal(decisionFields[0], fields));
        Assert.True(decisionFields[0].Length > 1);
    }

    private static ProcessedState?[]? Capture(
        IReadOnlyList<ProcessedT4Extraction?>? inputs) =>
        inputs?
            .Select(
                input => input is null
                    ? null
                    : new ProcessedState(
                        input,
                        input.DocumentId,
                        input.FirmId,
                        input.CaseId,
                        input.ParticipantId,
                        input.TaxYear,
                        input.SourceReference,
                        input.Slip,
                        input.Mapping,
                        input.Mapping.Lines,
                        input.Mapping.UnmappedBoxCodes))
            .ToArray();

    private static void AssertInputState(
        ProcessedState?[]? expected,
        IReadOnlyList<ProcessedT4Extraction?>? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        Assert.Equal(expected.Length, actual!.Count);

        for (var index = 0; index < expected.Length; index++)
        {
            var state = expected[index];
            var input = actual[index];

            if (state is null)
            {
                Assert.Null(input);
                continue;
            }

            Assert.Same(state.Processed, input);
            Assert.Equal(state.DocumentId, input!.DocumentId);
            Assert.Equal(state.FirmId, input.FirmId);
            Assert.Equal(state.CaseId, input.CaseId);
            Assert.Equal(state.ParticipantId, input.ParticipantId);
            Assert.Equal(state.TaxYear, input.TaxYear);
            Assert.Equal(state.SourceReference, input.SourceReference);
            Assert.Same(state.Slip, input.Slip);
            Assert.Same(state.Mapping, input.Mapping);
            Assert.Same(state.Lines, input.Mapping.Lines);
            Assert.Same(
                state.UnmappedBoxCodes,
                input.Mapping.UnmappedBoxCodes);
        }
    }

    private sealed record ProcessedState(
        ProcessedT4Extraction Processed,
        string DocumentId,
        string FirmId,
        string CaseId,
        string ParticipantId,
        int TaxYear,
        string SourceReference,
        T4Slip Slip,
        T4MappingResult Mapping,
        IReadOnlyList<TaxLineEntry> Lines,
        IReadOnlyList<string> UnmappedBoxCodes);

    private sealed class DescriptorState
    {
        private DescriptorState(
            ReviewDecisionDescriptor? descriptor)
        {
            Descriptor = descriptor;
            Decision = descriptor?.Decision;
            ReviewerReference = descriptor?.ReviewerReference;
            DecidedAtReference = descriptor?.DecidedAtReference;
            Note = descriptor?.Note;
            Acknowledgement = descriptor?.UnmappedBoxesAcknowledged;
        }

        private ReviewDecisionDescriptor? Descriptor { get; }
        private SignOffDecision? Decision { get; }
        private string? ReviewerReference { get; }
        private string? DecidedAtReference { get; }
        private string? Note { get; }
        private bool? Acknowledgement { get; }

        internal static DescriptorState From(
            ReviewDecisionDescriptor? descriptor) =>
            new(descriptor);

        internal void AssertUnchanged(
            ReviewDecisionDescriptor? actual)
        {
            Assert.Same(Descriptor, actual);

            if (actual is null)
            {
                return;
            }

            Assert.Equal(Decision, actual.Decision);
            Assert.Equal(ReviewerReference, actual.ReviewerReference);
            Assert.Equal(DecidedAtReference, actual.DecidedAtReference);
            Assert.Equal(Note, actual.Note);
            Assert.Equal(
                Acknowledgement,
                actual.UnmappedBoxesAcknowledged);
        }
    }
}
