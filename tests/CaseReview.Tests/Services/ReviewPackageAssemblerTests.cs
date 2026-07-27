using System.Collections;
using CaseProcessing.Models;
using CaseProcessing.Services;
using CaseReview.Models;
using CaseReview.Services;
using DocumentExtraction.Models;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace CaseReview.Tests.Services;

public sealed class ReviewPackageAssemblerTests
{
    private static readonly EmbeddedT4LineMapProvider Embedded = new();

    [Fact]
    public void Assemble_SingleExtraction_SucceedsWithExactAttributionAndNoSignOff()
    {
        const string documentId = "  Document-MARKER  ";
        const string firmId = "\tFirm-MARKER ";
        const string caseId = " Case-MARKER\t";
        const string participantId = " Participant-MARKER ";
        const string sourceReference = "  Source-MARKER  ";
        var processed = Processed(
            documentId: documentId,
            firmId: firmId,
            caseId: caseId,
            participantId: participantId,
            sourceReference: sourceReference,
            extraBoxes:
            [
                new T4RawBox(T4BoxCodes.EmploymentIncome, "52000.00"),
            ]);

        var result = ReviewPackageAssembler.Assemble([processed]);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        var package = Assert.IsType<AccountantReviewPackage>(result.Value);
        Assert.Equal(firmId, package.FirmId, StringComparer.Ordinal);
        Assert.Equal(caseId, package.CaseId, StringComparer.Ordinal);
        Assert.Equal(participantId, package.ParticipantId, StringComparer.Ordinal);
        Assert.Equal(processed.TaxYear, package.TaxYear);
        Assert.Null(package.SignOff);

        var reviewed = Assert.Single(package.ReviewedSlips);
        Assert.Equal(documentId, reviewed.DocumentId, StringComparer.Ordinal);
        Assert.Equal(sourceReference, reviewed.SourceReference, StringComparer.Ordinal);
        Assert.Equal(processed.TaxYear, reviewed.TaxYear);
    }

    [Fact]
    public void Assemble_SeveralExtractions_PreservesSuppliedOrderAndEveryYear()
    {
        var third = Processed(documentId: "document-third");
        var first = Processed(documentId: "document-first");
        var second = Processed(documentId: "document-second");

        var result = ReviewPackageAssembler.Assemble([third, first, second]);

        Assert.True(result.IsSuccess);
        var package = result.Value!;
        Assert.Equal(
            new[] { "document-third", "document-first", "document-second" },
            package.ReviewedSlips.Select(slip => slip.DocumentId));
        Assert.Equal(3, package.ReviewedSlips.Count);
        Assert.All(
            package.ReviewedSlips,
            reviewed => Assert.Equal(package.TaxYear, reviewed.TaxYear));
        Assert.Equal(third.TaxYear, package.TaxYear);
        Assert.Equal(first.TaxYear, package.TaxYear);
        Assert.Equal(second.TaxYear, package.TaxYear);
    }

    [Fact]
    public void Assemble_PreservesSlipIdentityAndEveryEngineProducedSequence()
    {
        var processed = Processed(
            extraBoxes:
            [
                new T4RawBox(T4BoxCodes.EmploymentIncome, "52000.00"),
                new T4RawBox(T4BoxCodes.IncomeTaxDeducted, "8000.00"),
                new T4RawBox(T4BoxCodes.CppContributions, "3500.00"),
                new T4RawBox(
                    T4BoxCodes.CppQppPensionableEarnings,
                    "52000.00"),
            ]);
        Assert.NotEmpty(processed.Mapping.Lines);
        Assert.NotEmpty(processed.Mapping.UnmappedBoxCodes);

        var result = ReviewPackageAssembler.Assemble([processed]);

        Assert.True(result.IsSuccess);
        var reviewed = Assert.Single(result.Value!.ReviewedSlips);
        Assert.Same(processed.Slip, reviewed.Slip);

        Assert.NotSame(processed.Mapping.Lines, reviewed.LineEntries);
        Assert.Equal(processed.Mapping.Lines.Count, reviewed.LineEntries.Count);
        for (var index = 0; index < processed.Mapping.Lines.Count; index++)
        {
            var expected = processed.Mapping.Lines[index];
            var actual = reviewed.LineEntries[index];
            Assert.NotSame(expected, actual);
            Assert.Equal(expected.LineNumber, actual.LineNumber, StringComparer.Ordinal);
            Assert.Equal(expected.Amount, actual.Amount);
            Assert.NotSame(expected.SourceBoxCodes, actual.SourceBoxCodes);
            Assert.Equal(expected.SourceBoxCodes, actual.SourceBoxCodes);
        }

        var expectedBoxAmounts = T4SlipBoxAmounts
            .Enumerate(processed.Slip)
            .ToArray();
        Assert.NotEmpty(expectedBoxAmounts);
        Assert.Equal(
            expectedBoxAmounts,
            reviewed.BoxAmounts
                .Select(amount => (amount.BoxCode, amount.Amount))
                .ToArray());
        Assert.Equal(
            processed.Mapping.UnmappedBoxCodes,
            reviewed.UnmappedBoxCodes);
    }

    [Fact]
    public void Assemble_SameInputsTwice_ProducesEqualPackages()
    {
        ProcessedT4Extraction?[] inputs =
        [
            Processed(documentId: "document-a"),
            Processed(documentId: "document-b"),
        ];

        var first = ReviewPackageAssembler.Assemble(inputs);
        var second = ReviewPackageAssembler.Assemble(inputs);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(first.Value!.GetHashCode(), second.Value!.GetHashCode());
        Assert.Equal(first.Errors, second.Errors);
    }

    [Fact]
    public void Assemble_MaterializesInputExactlyOnce()
    {
        var inputs = new SingleEnumerationReadOnlyList<ProcessedT4Extraction?>(
        [
            Processed(documentId: "document-a"),
            Processed(documentId: "document-b"),
        ]);

        var result = ReviewPackageAssembler.Assemble(inputs);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, inputs.EnumerationCount);
        Assert.Equal(
            new[] { "document-a", "document-b" },
            result.Value!.ReviewedSlips.Select(slip => slip.DocumentId));
    }

    [Fact]
    public void Assemble_DoesNotMutateAnyProcessedInput()
    {
        var first = Processed(
            documentId: "document-a",
            sourceReference: "source-a",
            extraBoxes:
            [
                new T4RawBox(T4BoxCodes.EmploymentIncome, "100.005"),
            ]);
        var second = Processed(
            documentId: "document-b",
            sourceReference: "source-b",
            extraBoxes:
            [
                new T4RawBox(T4BoxCodes.IncomeTaxDeducted, "25.00"),
            ]);
        ProcessedT4Extraction?[] inputs = [first, second];
        var before = inputs
            .Select(input => Snapshot(input!))
            .ToArray();

        var result = ReviewPackageAssembler.Assemble(inputs);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ProcessedT4Extraction?[] { first, second }, inputs);
        for (var index = 0; index < inputs.Length; index++)
        {
            var input = inputs[index]!;
            var snapshot = before[index];
            Assert.Equal(snapshot.DocumentId, input.DocumentId, StringComparer.Ordinal);
            Assert.Equal(snapshot.FirmId, input.FirmId, StringComparer.Ordinal);
            Assert.Equal(snapshot.CaseId, input.CaseId, StringComparer.Ordinal);
            Assert.Equal(snapshot.ParticipantId, input.ParticipantId, StringComparer.Ordinal);
            Assert.Equal(snapshot.TaxYear, input.TaxYear);
            Assert.Equal(snapshot.SourceReference, input.SourceReference, StringComparer.Ordinal);
            Assert.Same(snapshot.Slip, input.Slip);
            Assert.Same(snapshot.Mapping, input.Mapping);
            Assert.Same(snapshot.Lines, input.Mapping.Lines);
            Assert.Same(snapshot.UnmappedBoxCodes, input.Mapping.UnmappedBoxCodes);
        }
    }

    [Fact]
    public void Assemble_EmptyLineMapping_SucceedsWithAnEmptyReviewedLineSnapshot()
    {
        var processed = Processed(provider: new EmptyMapProvider());
        Assert.Empty(processed.Mapping.Lines);

        var result = ReviewPackageAssembler.Assemble([processed]);

        Assert.True(result.IsSuccess);
        Assert.Empty(Assert.Single(result.Value!.ReviewedSlips).LineEntries);
    }

    [Fact]
    public void Assemble_ZeroAmount_RemainsExactlyTheEnginesValue()
    {
        var processed = Processed(
            extraBoxes:
            [
                new T4RawBox(T4BoxCodes.EmploymentIncome, "0"),
            ]);
        var engineAmounts = T4SlipBoxAmounts.Enumerate(processed.Slip).ToArray();

        var result = ReviewPackageAssembler.Assemble([processed]);

        Assert.True(result.IsSuccess);
        var reviewed = Assert.Single(result.Value!.ReviewedSlips);
        Assert.Same(processed.Slip, reviewed.Slip);
        Assert.Equal(processed.Slip.EmploymentIncome, reviewed.Slip.EmploymentIncome);
        Assert.Equal(
            engineAmounts,
            reviewed.BoxAmounts
                .Select(amount => (amount.BoxCode, amount.Amount))
                .ToArray());
    }

    [Theory]
    [InlineData("100.005")]
    [InlineData("100.015")]
    public void Assemble_HalfCentAmount_EqualsTheUpstreamEngineOutput(string rawAmount)
    {
        var processed = Processed(
            extraBoxes:
            [
                new T4RawBox(T4BoxCodes.EmploymentIncome, rawAmount),
            ]);
        var engineAmount = Assert.Single(
            T4SlipBoxAmounts
                .Enumerate(processed.Slip)
                .Where(amount => string.Equals(
                    amount.BoxCode,
                    T4BoxCodes.EmploymentIncome,
                    StringComparison.Ordinal)));

        var result = ReviewPackageAssembler.Assemble([processed]);

        Assert.True(result.IsSuccess);
        var reviewedAmount = Assert.Single(
            Assert.Single(result.Value!.ReviewedSlips)
                .BoxAmounts
                .Where(amount => string.Equals(
                    amount.BoxCode,
                    T4BoxCodes.EmploymentIncome,
                    StringComparison.Ordinal)));
        Assert.Equal(engineAmount.Amount, reviewedAmount.Amount);
        Assert.Equal(processed.Slip.EmploymentIncome, reviewedAmount.Amount);
    }

    [Fact]
    public void Assemble_NullCollection_FailsClosedWithoutThrowing()
    {
        var exception = Record.Exception(
            () => ReviewPackageAssembler.Assemble(null));

        Assert.Null(exception);
        AssertSingleFailure(
            ReviewPackageAssembler.Assemble(null),
            ReviewFields.Extractions,
            "A processed extraction collection is required.");
    }

    [Fact]
    public void Assemble_EmptyCollection_FailsClosed()
    {
        AssertSingleFailure(
            ReviewPackageAssembler.Assemble([]),
            ReviewFields.Extractions,
            "At least one processed extraction is required.");
    }

    [Fact]
    public void Assemble_NullElement_FailsClosedWithoutThrowing()
    {
        var processed = Processed();
        ProcessedT4Extraction?[] inputs = [processed, null];

        var exception = Record.Exception(
            () => ReviewPackageAssembler.Assemble(inputs));

        Assert.Null(exception);
        AssertSingleFailure(
            ReviewPackageAssembler.Assemble(inputs),
            ReviewFields.Extraction,
            "Every processed extraction is required.");
    }

    [Fact]
    public void Assemble_FirmMismatch_FailsClosedWithoutLeakingInput()
    {
        var result = ReviewPackageAssembler.Assemble(
        [
            Processed(documentId: "document-a", firmId: "FIRM-MARKER-A"),
            Processed(documentId: "document-b", firmId: "FIRM-MARKER-B"),
        ]);

        var error = AssertSingleFailure(
            result,
            ReviewFields.FirmId,
            "All processed extractions must share one firm identifier.");
        AssertDoesNotContainCallerValues(
            error,
            "FIRM-MARKER-A",
            "FIRM-MARKER-B");
    }

    [Fact]
    public void Assemble_CaseMismatch_FailsClosedWithoutLeakingInput()
    {
        var result = ReviewPackageAssembler.Assemble(
        [
            Processed(documentId: "document-a", caseId: "CASE-MARKER-A"),
            Processed(documentId: "document-b", caseId: "CASE-MARKER-B"),
        ]);

        var error = AssertSingleFailure(
            result,
            ReviewFields.CaseId,
            "All processed extractions must share one case identifier.");
        AssertDoesNotContainCallerValues(
            error,
            "CASE-MARKER-A",
            "CASE-MARKER-B");
    }

    [Fact]
    public void Assemble_ParticipantMismatch_FailsClosedWithoutLeakingInput()
    {
        var result = ReviewPackageAssembler.Assemble(
        [
            Processed(documentId: "document-a", participantId: "PARTICIPANT-MARKER-A"),
            Processed(documentId: "document-b", participantId: "PARTICIPANT-MARKER-B"),
        ]);

        var error = AssertSingleFailure(
            result,
            ReviewFields.ParticipantId,
            "All processed extractions must share one participant identifier.");
        AssertDoesNotContainCallerValues(
            error,
            "PARTICIPANT-MARKER-A",
            "PARTICIPANT-MARKER-B");
    }

    [Theory]
    [InlineData("Participant-MARKER", "participant-MARKER")]
    [InlineData("Participant-MARKER", " Participant-MARKER")]
    [InlineData("Participant-MARKER", "Participant-MARKER ")]
    public void Assemble_IdentifierNearMatches_AreComparedOrdinally(
        string firstParticipantId,
        string secondParticipantId)
    {
        var result = ReviewPackageAssembler.Assemble(
        [
            Processed(
                documentId: "document-a",
                participantId: firstParticipantId),
            Processed(
                documentId: "document-b",
                participantId: secondParticipantId),
        ]);

        var error = AssertSingleFailure(
            result,
            ReviewFields.ParticipantId,
            "All processed extractions must share one participant identifier.");
        AssertDoesNotContainCallerValues(
            error,
            firstParticipantId,
            secondParticipantId);
    }

    [Fact]
    public void Assemble_DuplicateDocumentId_FailsClosedWithoutLeakingInput()
    {
        const string duplicateDocumentId = "DUPLICATE-DOCUMENT-MARKER";
        var result = ReviewPackageAssembler.Assemble(
        [
            Processed(documentId: duplicateDocumentId, sourceReference: "source-a"),
            Processed(documentId: duplicateDocumentId, sourceReference: "source-b"),
        ]);

        var error = AssertSingleFailure(
            result,
            ReviewFields.DocumentId,
            "Document identifiers must be unique within a review package.");
        AssertDoesNotContainCallerValues(error, duplicateDocumentId);
    }

    [Fact]
    public void Assemble_IndependentFailures_AccumulateOnceEachInFixedOrder()
    {
        const string duplicateDocumentId = "DUPLICATE-DOCUMENT-MARKER";
        var result = ReviewPackageAssembler.Assemble(
        [
            Processed(
                documentId: duplicateDocumentId,
                firmId: "firm-a",
                caseId: "case-a",
                participantId: "participant-a"),
            Processed(
                documentId: duplicateDocumentId,
                firmId: "firm-b",
                caseId: "case-b",
                participantId: "participant-b"),
            Processed(
                documentId: "document-c",
                firmId: "firm-c",
                caseId: "case-c",
                participantId: "participant-c"),
        ]);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(
            new[]
            {
                ReviewFields.FirmId,
                ReviewFields.CaseId,
                ReviewFields.ParticipantId,
                ReviewFields.DocumentId,
            },
            result.Errors.Select(error => error.Field));
        Assert.Equal(4, result.Errors.Count);
        Assert.All(
            result.Errors,
            error => AssertDoesNotContainCallerValues(
                error,
                duplicateDocumentId,
                "firm-a",
                "firm-b",
                "firm-c",
                "case-a",
                "case-b",
                "case-c",
                "participant-a",
                "participant-b",
                "participant-c"));
    }

    private static ProcessedT4Extraction Processed(
        string documentId = "document-1",
        string firmId = "firm-1",
        string caseId = "case-1",
        string participantId = "participant-1",
        string sourceReference = "source-1",
        IReadOnlyList<T4RawBox>? extraBoxes = null,
        IT4LineMapProvider? provider = null)
    {
        var envelope = new ExtractionEnvelope
        {
            DocumentId = documentId,
            Kind = ExtractedDocumentKind.T4Slip,
            FirmId = firmId,
            CaseId = caseId,
            ParticipantId = participantId,
            TaxYear = 2025,
            Provenance = new ExtractionProvenance
            {
                SourceReference = sourceReference,
            },
            T4Payload = ValidPayload(extraBoxes),
        };
        var validated = ExtractionEnvelopeValidator.Validate(envelope);
        Assert.True(
            validated.IsSuccess,
            "The test envelope must clear the document-extraction gate.");

        var processed = new T4CaseProcessor(provider ?? Embedded)
            .Process(validated.Value);
        Assert.True(
            processed.IsSuccess,
            "The test extraction must clear the T4 case processor.");
        return processed.Value!;
    }

    private static T4RawInput ValidPayload(
        IReadOnlyList<T4RawBox>? extraBoxes)
    {
        var boxes = new List<T4RawBox>
        {
            new(T4BoxCodes.SocialInsuranceNumber, "046454286"),
            new(T4BoxCodes.EmployerAccountNumber, "123456789RP0001"),
            new(T4BoxCodes.DentalBenefits, "1"),
        };
        boxes.AddRange(extraBoxes ?? Array.Empty<T4RawBox>());

        return new T4RawInput
        {
            Year = 2025,
            EmployerName = "Employer",
            EmployerAddress = "Employer address",
            EmployeeName = "Employee",
            EmployeeAddress = "Employee address",
            Boxes = boxes,
        };
    }

    private static ProcessedSnapshot Snapshot(ProcessedT4Extraction processed) =>
        new(
            processed.DocumentId,
            processed.FirmId,
            processed.CaseId,
            processed.ParticipantId,
            processed.TaxYear,
            processed.SourceReference,
            processed.Slip,
            processed.Mapping,
            processed.Mapping.Lines,
            processed.Mapping.UnmappedBoxCodes);

    private static ValidationError AssertSingleFailure(
        ParseResult<AccountantReviewPackage> result,
        string field,
        string message)
    {
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        var error = Assert.Single(result.Errors);
        Assert.Equal(field, error.Field, StringComparer.Ordinal);
        Assert.Equal(message, error.Message, StringComparer.Ordinal);
        return error;
    }

    private static void AssertDoesNotContainCallerValues(
        ValidationError error,
        params string[] callerValues)
    {
        foreach (var callerValue in callerValues)
        {
            Assert.DoesNotContain(callerValue, error.Field, StringComparison.Ordinal);
            Assert.DoesNotContain(callerValue, error.Message, StringComparison.Ordinal);
        }
    }

    private sealed class EmptyMapProvider : IT4LineMapProvider
    {
        public ParseResult<T4LineMap> TryGetForYear(int taxYear) =>
            T4LineMap.Create(
                new T4LineMappingConfig(
                    taxYear,
                    Array.Empty<T4BoxLineMapping>()));
    }

    private sealed class SingleEnumerationReadOnlyList<T> : IReadOnlyList<T>
    {
        private readonly IReadOnlyList<T> _items;

        public SingleEnumerationReadOnlyList(IReadOnlyList<T> items) =>
            _items = items;

        public int EnumerationCount { get; private set; }

        public int Count => _items.Count;

        public T this[int index] => _items[index];

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException(
                    "The assembler enumerated its input more than once.");
            }

            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed record ProcessedSnapshot(
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
}
