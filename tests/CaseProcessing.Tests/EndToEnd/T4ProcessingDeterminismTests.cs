using CaseProcessing.Models;
using CaseProcessing.Services;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;

namespace CaseProcessing.Tests.EndToEnd;

/// <summary>
/// T04 — AC-7. The same extraction and the same provider always yield an equal
/// result: equal success/failure, the same errors <b>in the same order</b>, the
/// same slip values, and the same line entries. Replay equality is asserted over
/// the accepted pipeline and every rejection family, five replays each.
/// <para>
/// Error <i>ordering</i> is the point, not set equality. A caller reading the
/// first error, or diffing two runs, must see a stable sequence — an
/// accumulating stage that reordered itself between runs would still satisfy a
/// set comparison and would still be a defect.
/// </para>
/// </summary>
public sealed class T4ProcessingDeterminismTests
{
    private const int Replays = 5;

    public static TheoryData<string> AllPipelines()
    {
        var data = new TheoryData<string>();

        foreach (var pipeline in T4ProcessingFixture.Pipelines)
        {
            data.Add(pipeline);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPipelines))]
    public void Replaying_TheSameExtraction_YieldsAnEqualResult(string pipeline) // AC-7
    {
        // One extraction, processed repeatedly — the input is held fixed so any
        // difference between runs is the module's own.
        var (extraction, first) = T4ProcessingFixture.Run(pipeline);
        var provider = ProviderFor(pipeline);

        for (var replay = 0; replay < Replays; replay++)
        {
            var again = new T4CaseProcessor(provider).Process(extraction);

            AssertResultsEqual(first, again, pipeline);
        }
    }

    [Theory]
    [MemberData(nameof(AllPipelines))]
    public void TwoDistinctButEquivalentExtractions_Agree(string pipeline) // AC-7
    {
        // Two separately-built envelopes with identical content produce two
        // distinct ValidatedExtraction instances. Equal inputs, equal outputs —
        // nothing is keyed to object identity, a clock, or an allocation order.
        var (firstExtraction, firstResult) = T4ProcessingFixture.Run(pipeline);
        var (secondExtraction, secondResult) = T4ProcessingFixture.Run(pipeline);

        if (firstExtraction is not null && secondExtraction is not null)
        {
            Assert.NotSame(firstExtraction, secondExtraction);
        }

        AssertResultsEqual(firstResult, secondResult, pipeline);
    }

    [Fact]
    public void EveryRejectionFamily_KeepsItsErrorOrderingAcrossReplays() // AC-7
    {
        // The ordering claim, stated once over every failing pipeline at once so a
        // family that started reordering itself cannot hide behind a per-family
        // theory that happens to emit a single error.
        foreach (var pipeline in T4ProcessingFixture.Pipelines.Where(p => p != "Accepted"))
        {
            var (extraction, first) = T4ProcessingFixture.Run(pipeline);
            var provider = ProviderFor(pipeline);

            Assert.True(first.IsFailure, $"The {pipeline} pipeline must fail closed.");
            var expected = first.Errors.Select(error => $"{error.Field}|{error.Message}").ToArray();
            Assert.NotEmpty(expected);

            for (var replay = 0; replay < Replays; replay++)
            {
                var again = new T4CaseProcessor(provider).Process(extraction);

                Assert.Equal(expected, again.Errors.Select(error => $"{error.Field}|{error.Message}").ToArray());
            }
        }
    }

    [Fact]
    public void AcceptedReplays_ProduceIdenticalSlipsAndLineEntries() // AC-7
    {
        var (extraction, first) = T4ProcessingFixture.Run("Accepted");

        Assert.True(first.IsSuccess);

        for (var replay = 0; replay < Replays; replay++)
        {
            var again = new T4CaseProcessor(T4ProcessingFixture.Embedded).Process(extraction);

            Assert.True(again.IsSuccess);
            T4ProcessingFixture.AssertSlipsEqual(first.Value!.Slip, again.Value!.Slip);
            T4ProcessingFixture.AssertMappingsEqual(first.Value.Mapping, again.Value.Mapping);
            Assert.Equal(first.Value.TaxYear, again.Value.TaxYear);
            Assert.Equal(first.Value.DocumentId, again.Value.DocumentId, StringComparer.Ordinal);
        }
    }

    [Fact]
    public void Processing_MutatesNeitherTheExtractionNorItsPayload() // AC-7
    {
        var envelope = T4ProcessingFixture.Envelope();
        var payload = envelope.T4Payload!;
        var extraction = ExtractionEnvelopeValidator.Validate(envelope).Value!;

        var boxSnapshot = payload.Boxes.ToArray();
        var identitySnapshot = new[]
        {
            payload.EmployerName, payload.EmployerAddress, payload.EmployeeName, payload.EmployeeAddress,
        };
        var attributionSnapshot = new[]
        {
            extraction.DocumentId, extraction.FirmId, extraction.CaseId,
            extraction.ParticipantId, extraction.SourceReference,
        };

        for (var replay = 0; replay < Replays; replay++)
        {
            new T4CaseProcessor(T4ProcessingFixture.Embedded).Process(extraction);
        }

        // Same payload instance, same box instances, same order — nothing was
        // trimmed, normalized, deduplicated, reordered, or dropped.
        Assert.Same(payload, extraction.T4Payload);
        Assert.Equal(boxSnapshot.Length, payload.Boxes.Count);
        for (var index = 0; index < boxSnapshot.Length; index++)
        {
            Assert.Same(boxSnapshot[index], payload.Boxes[index]);
        }

        Assert.Equal(
            identitySnapshot,
            new[] { payload.EmployerName, payload.EmployerAddress, payload.EmployeeName, payload.EmployeeAddress });
        Assert.Equal(T4ProcessingFixture.TaxYear, payload.Year);

        Assert.Equal(
            attributionSnapshot,
            new[]
            {
                extraction.DocumentId, extraction.FirmId, extraction.CaseId,
                extraction.ParticipantId, extraction.SourceReference,
            });
    }

    [Fact]
    public void ReplaySweep_ActuallyCoversAnAcceptedRunAndEveryRejectionFamily()
    {
        // Guards the sweep itself: if Pipelines shrank, or a "rejection" family
        // stopped rejecting, every theory above would still pass while covering
        // less.
        Assert.Equal(11, T4ProcessingFixture.Pipelines.Length);

        var outcomes = T4ProcessingFixture.Pipelines
            .ToDictionary(pipeline => pipeline, pipeline => T4ProcessingFixture.Run(pipeline).Result.IsSuccess);

        Assert.True(outcomes["Accepted"]);
        Assert.Equal(10, outcomes.Count(outcome => !outcome.Value));
    }

    /// <summary>
    /// The provider each pipeline runs against — the failing stub for the provider
    /// family, the embedded 2025 config for everything else.
    /// </summary>
    private static TaxLogicEngine.Services.IT4LineMapProvider ProviderFor(string pipeline) =>
        string.Equals(pipeline, "ProviderFailure", StringComparison.Ordinal)
            ? new AlwaysFailingProvider()
            : T4ProcessingFixture.Embedded;

    private static void AssertResultsEqual(
        ParseResult<ProcessedT4Extraction> expected,
        ParseResult<ProcessedT4Extraction> actual,
        string pipeline)
    {
        Assert.Equal(expected.IsSuccess, actual.IsSuccess);

        // Sequence equality, not set equality: ordering is part of the contract.
        Assert.Equal(
            expected.Errors.Select(error => $"{error.Field}|{error.Message}").ToArray(),
            actual.Errors.Select(error => $"{error.Field}|{error.Message}").ToArray());

        if (!expected.IsSuccess)
        {
            Assert.Null(actual.Value);
            Assert.NotEmpty(actual.Errors);
            return;
        }

        Assert.NotNull(actual.Value);
        T4ProcessingFixture.AssertSlipsEqual(expected.Value!.Slip, actual.Value!.Slip);
        T4ProcessingFixture.AssertMappingsEqual(expected.Value.Mapping, actual.Value.Mapping);
        Assert.Equal(expected.Value.TaxYear, actual.Value.TaxYear);
        Assert.Equal(expected.Value.ToString(), actual.Value.ToString());
        Assert.True(actual.Value.TaxYear == T4ProcessingFixture.TaxYear, $"{pipeline} produced an unexpected tax year.");
    }

    private sealed class AlwaysFailingProvider : TaxLogicEngine.Services.IT4LineMapProvider
    {
        public ParseResult<T4LineMap> TryGetForYear(int taxYear) =>
            ParseResult<T4LineMap>.Failure(
                new ValidationError("TaxYear", "stub: no config for that year."));
    }
}
