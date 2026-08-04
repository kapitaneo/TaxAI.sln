using System.Globalization;
using System.Text;
using ExtractionWorkflow.Models;

namespace DocumentIntelligence.Tests;

/// <summary>
/// Spec 015 T02 — what the shipped adapter says about a document, and what it
/// refuses to say.
/// <para>
/// Two properties matter more than any individual field. The first is
/// determinism: the same bytes always produce the same answer, because a
/// non-deterministic extractor turns one accountant's review into an
/// unreproducible event. The second is that it never guesses — anything it does
/// not recognize is <see cref="DocumentClassification.Unsupported"/>, which is
/// what keeps a photo of a driver's licence in a T4 checklist slot from
/// becoming a slip.
/// </para>
/// </summary>
public sealed class DeterministicProviderTests
{
    private const string Header = DeterministicDocumentIntelligenceProvider.DocumentHeader;

    private static readonly string[] EmployeeCopyT4 =
    {
        Header,
        "KIND: T4",
        "CONFIDENCE: 0.75",
        "YEAR: 2025",
        "EMPLOYER-NAME: Acme Manufacturing",
        "EMPLOYER-ADDRESS: 12 Main Street",
        "EMPLOYEE-NAME: Casey Taxpayer",
        "EMPLOYEE-ADDRESS: 9 Bay Road",
        "EXEMPT-CPP-QPP: true",
        "EXEMPT-EI: false",
        "BOX 12: 123456789",
        "BOX 14: 50000.00",
        "BOX-CONFIDENCE 14: 0.91",
        "BOX 45: 1",
    };

    private static readonly string[] MedicalReceipt =
    {
        Header,
        "KIND: MEDICAL-EXPENSE-EVIDENCE",
        "CONFIDENCE: 0.5",
        "SERVICE-DATE: 14 mars 2025",
        "GROSS-AMOUNT: $1,120.50",
        "CURRENCY: CAD",
        "PROVIDER-NAME: Bay Street Dental",
        "PATIENT-NAME: Casey Taxpayer",
        "RECEIPT-REFERENCE: R-4471",
    };

    // ------------------------------------------------------ classification

    [Fact]
    public async Task AT4DocumentClassifiesAsAT4()
    {
        var result = await Provider().ClassifyAsync(Document(EmployeeCopyT4), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(DocumentClassification.T4Slip, result!.Classification);
        Assert.Equal(0.75m, result.Confidence);
        Assert.Equal("deterministic-test-adapter", result.ProviderLabel);
    }

    [Fact]
    public async Task AMedicalReceiptClassifiesAsMedicalEvidence()
    {
        var result = await Provider().ClassifyAsync(Document(MedicalReceipt), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(DocumentClassification.MedicalExpenseEvidence, result!.Classification);
    }

    public static TheoryData<string, byte[]> UnrecognizableDocuments() => new()
    {
        { "empty", Array.Empty<byte>() },
        { "whitespace", Encoding.UTF8.GetBytes("   \r\n  \n") },
        { "no header", Encoding.UTF8.GetBytes("KIND: T4\nBOX 14: 50000.00") },
        { "wrong header", Encoding.UTF8.GetBytes("SOME-OTHER-DOCUMENT/1\nKIND: T4") },
        { "unknown kind", Encoding.UTF8.GetBytes($"{Header}\nKIND: DRIVERS-LICENCE\nBOX 14: 1") },
        { "absent kind", Encoding.UTF8.GetBytes($"{Header}\nYEAR: 2025\nBOX 14: 1") },
        { "binary", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 } },
    };

    [Theory]
    [MemberData(nameof(UnrecognizableDocuments))]
    public async Task AnUnrecognizableDocumentIsUnsupportedRatherThanAGuess(
        string description,
        byte[] content)
    {
        var provider = Provider();

        var classification = await provider.ClassifyAsync(
            new MemoryStream(content), CancellationToken.None);

        Assert.NotNull(classification);
        Assert.Equal(DocumentClassification.Unsupported, classification!.Classification);

        // And nothing tax-shaped comes out of it either.
        Assert.Null(await provider.ExtractT4Async(new MemoryStream(content), CancellationToken.None));
        Assert.Null(await provider.ExtractMedicalAsync(new MemoryStream(content), CancellationToken.None));

        Assert.NotEmpty(description);
    }

    [Fact]
    public async Task AT4YieldsNoMedicalResultAndAReceiptYieldsNoT4Result()
    {
        var provider = Provider();

        Assert.Null(await provider.ExtractMedicalAsync(Document(EmployeeCopyT4), CancellationToken.None));
        Assert.Null(await provider.ExtractT4Async(Document(MedicalReceipt), CancellationToken.None));
    }

    // ------------------------------------------------------------ determinism

    [Fact]
    public async Task TheSameBytesAlwaysProduceTheSameAnswer()
    {
        var provider = Provider();

        var first = await provider.ExtractT4Async(Document(EmployeeCopyT4), CancellationToken.None);
        var second = await provider.ExtractT4Async(Document(EmployeeCopyT4), CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Year, second!.Year);
        Assert.Equal(first.EmployerName, second.EmployerName);
        Assert.Equal(first.EmployeeName, second.EmployeeName);
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(
            first.Boxes!.Select(box => (box!.Code, box.Value, box.Confidence)).ToArray(),
            second.Boxes!.Select(box => (box!.Code, box.Value, box.Confidence)).ToArray());
    }

    [Fact]
    public async Task ADifferentInstanceOfTheAdapterAnswersIdentically()
    {
        // Determinism is a property of the bytes, not of one object's state.
        var first = await Provider().ClassifyAsync(Document(MedicalReceipt), CancellationToken.None);
        var second = await Provider().ClassifyAsync(Document(MedicalReceipt), CancellationToken.None);

        Assert.Equal(first!.Classification, second!.Classification);
        Assert.Equal(first.Confidence, second.Confidence);
    }

    // -------------------------------------------------------- T4 extraction

    [Fact]
    public async Task TheT4FieldsAreCopiedExactlyAsTheDocumentShowedThem()
    {
        var result = await Provider().ExtractT4Async(Document(EmployeeCopyT4), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2025, result!.Year);
        Assert.Equal("Acme Manufacturing", result.EmployerName);
        Assert.Equal("12 Main Street", result.EmployerAddress);
        Assert.Equal("Casey Taxpayer", result.EmployeeName);
        Assert.Equal("9 Bay Road", result.EmployeeAddress);
        Assert.True(result.ExemptCppQpp);
        Assert.False(result.ExemptEi);
        Assert.Null(result.ExemptPpip);
    }

    [Fact]
    public async Task TheBoxesArriveInDocumentOrderWithTheirValuesAsPrinted()
    {
        var result = await Provider().ExtractT4Async(Document(EmployeeCopyT4), CancellationToken.None);

        Assert.Equal(
            new (string?, string?)[] { ("12", "123456789"), ("14", "50000.00"), ("45", "1") },
            result!.Boxes!.Select(box => (box!.Code, box.Value)).ToArray());
    }

    [Fact]
    public async Task ADuplicatedBoxIsCarriedTwiceSoTheEngineCanRefuseIt()
    {
        var document = new[]
        {
            Header,
            "KIND: T4",
            "BOX 14: 50000.00",
            "BOX 14: 60000.00",
        };

        var result = await Provider().ExtractT4Async(Document(document), CancellationToken.None);

        Assert.Equal(
            new (string?, string?)[] { ("14", "50000.00"), ("14", "60000.00") },
            result!.Boxes!.Select(box => (box!.Code, box.Value)).ToArray());
    }

    [Fact]
    public async Task ABoxCodeIsCarriedVerbatimAndNeverNormalized()
    {
        var document = new[] { Header, "KIND: T4", "BOX 16a: 188.00" };

        var result = await Provider().ExtractT4Async(Document(document), CancellationToken.None);

        // Rewriting an extracted code would be this adapter deciding something
        // about a box, and it decides nothing.
        Assert.Equal("16a", result!.Boxes!.Single()!.Code);
    }

    [Fact]
    public async Task APerBoxConfidenceIsTransportedWhenTheDocumentDeclaresOne()
    {
        var result = await Provider().ExtractT4Async(Document(EmployeeCopyT4), CancellationToken.None);

        var boxes = result!.Boxes!.ToArray();

        Assert.Equal(0.91m, boxes.Single(box => box!.Code == "14")!.Confidence);
        Assert.Null(boxes.Single(box => box!.Code == "45")!.Confidence);
    }

    [Fact]
    public async Task AnEmptyBoxAndAnAbsentFieldStayAbsent()
    {
        var document = new[]
        {
            Header,
            "KIND: T4",
            "EMPLOYER-NAME:",
            "BOX 14:",
        };

        var result = await Provider().ExtractT4Async(Document(document), CancellationToken.None);

        Assert.Null(result!.EmployerName);
        Assert.Null(result.EmployeeName);
        Assert.Null(result.Year);
        Assert.Null(result.Confidence);
        Assert.Null(result.Boxes!.Single()!.Value);
    }

    [Fact]
    public async Task AnEmployeeCopyWithNoBoxFiftyFourExtractsWithoutOne()
    {
        var result = await Provider().ExtractT4Async(Document(EmployeeCopyT4), CancellationToken.None);

        // Nothing is synthesized for the box the employee copy does not carry.
        Assert.DoesNotContain(result!.Boxes!, box => box!.Code == "54");
    }

    [Fact]
    public async Task AnUnreadableYearOrFlagStaysAbsentRatherThanBecomingAValue()
    {
        var document = new[]
        {
            Header,
            "KIND: T4",
            "YEAR: last year",
            "EXEMPT-EI: maybe",
        };

        var result = await Provider().ExtractT4Async(Document(document), CancellationToken.None);

        Assert.Null(result!.Year);
        Assert.Null(result.ExemptEi);
    }

    // --------------------------------------------------- medical extraction

    [Fact]
    public async Task TheMedicalFieldsAreCopiedExactlyAsPrinted()
    {
        var result = await Provider().ExtractMedicalAsync(
            Document(MedicalReceipt), CancellationToken.None);

        Assert.NotNull(result);

        // Not normalized to a canonical date, not stripped of its symbol, not
        // converted, not rounded: as printed is the whole contract.
        Assert.Equal("14 mars 2025", result!.ServiceDateAsPrinted);
        Assert.Equal("$1,120.50", result.GrossAmountAsPrinted);
        Assert.Equal("CAD", result.CurrencyCodeAsPrinted);
        Assert.Equal("Bay Street Dental", result.ProviderDisplayName);
        Assert.Equal("Casey Taxpayer", result.PatientDisplayName);
        Assert.Equal("R-4471", result.ReceiptReference);
        Assert.Equal(0.5m, result.Confidence);
    }

    [Fact]
    public async Task AnAbsentOptionalMedicalFieldStaysAbsent()
    {
        var document = new[]
        {
            Header,
            "KIND: MEDICAL-EXPENSE-EVIDENCE",
            "SERVICE-DATE: 2025-03-14",
            "GROSS-AMOUNT: 120.50",
        };

        var result = await Provider().ExtractMedicalAsync(Document(document), CancellationToken.None);

        Assert.Null(result!.CurrencyCodeAsPrinted);
        Assert.Null(result.ProviderDisplayName);
        Assert.Null(result.PatientDisplayName);
        Assert.Null(result.ReceiptReference);
    }

    // ----------------------------------------------------------- confidence

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("0.0")]
    [InlineData("1.0")]
    [InlineData("0.5")]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    public async Task ADeclaredConfidenceIsTransportedVerbatim(string printed)
    {
        // Including a value outside the accepted range: the existing envelope
        // validator stays the single place that range is enforced, and no
        // threshold exists here to reject on.
        var document = new[] { Header, "KIND: T4", $"CONFIDENCE: {printed}" };

        var result = await Provider().ClassifyAsync(Document(document), CancellationToken.None);

        Assert.Equal(decimal.Parse(printed, CultureInfo.InvariantCulture), result!.Confidence);
    }

    [Fact]
    public async Task TheLowestAndHighestConfidenceFollowTheSamePath()
    {
        // No threshold exists, so nothing about the outcome may differ between
        // them beyond the transported number itself.
        var provider = Provider();

        var lowest = await provider.ExtractT4Async(
            Document(new[] { Header, "KIND: T4", "CONFIDENCE: 0", "BOX 14: 1.00" }),
            CancellationToken.None);

        var highest = await provider.ExtractT4Async(
            Document(new[] { Header, "KIND: T4", "CONFIDENCE: 1", "BOX 14: 1.00" }),
            CancellationToken.None);

        Assert.NotNull(lowest);
        Assert.NotNull(highest);
        Assert.Equal(0m, lowest!.Confidence);
        Assert.Equal(1m, highest!.Confidence);
        Assert.Equal(
            lowest.Boxes!.Select(box => (box!.Code, box.Value)).ToArray(),
            highest.Boxes!.Select(box => (box!.Code, box.Value)).ToArray());
    }

    [Fact]
    public async Task AnUndeclaredOrUnreadableConfidenceIsNeverInvented()
    {
        foreach (var line in new[] { "CONFIDENCE: high", "CONFIDENCE:" })
        {
            var result = await Provider().ClassifyAsync(
                Document(new[] { Header, "KIND: T4", line }), CancellationToken.None);

            Assert.Null(result!.Confidence);
        }

        var absent = await Provider().ClassifyAsync(
            Document(new[] { Header, "KIND: T4" }), CancellationToken.None);

        Assert.Null(absent!.Confidence);
    }

    // ------------------------------------------------------ format tolerance

    [Fact]
    public async Task WindowsLineEndingsAByteOrderMarkAndBlankLinesAreTolerated()
    {
        var text = "\uFEFF" + string.Join("\r\n", new[]
        {
            string.Empty,
            Header,
            string.Empty,
            "KIND: T4",
            "  BOX 14 :  50000.00  ",
        });

        var result = await Provider().ExtractT4Async(
            new MemoryStream(Encoding.UTF8.GetBytes(text)), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(("14", "50000.00"), (result!.Boxes!.Single()!.Code, result.Boxes!.Single()!.Value));
    }

    [Fact]
    public async Task AnUnknownKeyIsIgnoredAndTheFirstDeclarationOfAKeyWins()
    {
        var document = new[]
        {
            Header,
            "KIND: T4",
            "SOMETHING-ELSE: ignored",
            "EMPLOYER-NAME: First Employer",
            "EMPLOYER-NAME: Second Employer",
        };

        var result = await Provider().ExtractT4Async(Document(document), CancellationToken.None);

        Assert.Equal("First Employer", result!.EmployerName);
    }

    [Fact]
    public async Task ALineWithNoSeparatorIsIgnoredRatherThanGuessedAt()
    {
        var document = new[] { Header, "KIND: T4", "this line means nothing", "BOX 14: 1.00" };

        var result = await Provider().ExtractT4Async(Document(document), CancellationToken.None);

        Assert.Single(result!.Boxes!);
    }

    // ------------------------------------------------------------- helpers

    internal static DeterministicDocumentIntelligenceProvider Provider(
        long maximumSourceBytes = 64 * 1024) =>
        new(new DocumentIntelligenceOptions
        {
            MaximumSourceBytes = maximumSourceBytes,
            ProviderLabel = "deterministic-test-adapter",
        });

    internal static MemoryStream Document(IEnumerable<string> lines) =>
        new(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
}
