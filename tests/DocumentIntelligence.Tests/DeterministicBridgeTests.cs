using System.Text;
using System.Xml.Linq;
using ExtractionWorkflow.Models;

namespace DocumentIntelligence.Tests;

/// <summary>
/// Spec 017 T02 — the one leading upload-signature line the Development
/// provider tolerates before its header.
/// <para>
/// The upload gate accepts a file declared as a PDF only when it begins with
/// the PDF signature, and the deterministic provider reads only its own text
/// format. The bridge between them is exactly one skipped line, and these tests
/// are mostly about what it must not become: PDF support, a second place the
/// header may sit, or a way for an image to carry a payload.
/// </para>
/// </summary>
public sealed class DeterministicBridgeTests
{
    private const string Header = DeterministicDocumentIntelligenceProvider.DocumentHeader;

    private const string Signature = "%PDF-1.7";

    private static readonly string[] T4Body =
    {
        "KIND: T4",
        "CONFIDENCE: 0.75",
        "YEAR: 2025",
        "EMPLOYER-NAME: Synthetic Employer",
        "EMPLOYEE-NAME: Synthetic Employee",
        "EXEMPT-EI: false",
        "BOX 14: 50000.00",
        "BOX-CONFIDENCE 14: 0.91",
        "BOX 45: 1",
    };

    private static readonly string[] MedicalBody =
    {
        "KIND: MEDICAL-EXPENSE-EVIDENCE",
        "SERVICE-DATE: 14 mars 2025",
        "GROSS-AMOUNT: $1,120.50",
        "CURRENCY: CAD",
        "PROVIDER-NAME: Synthetic Dental Clinic",
        "PATIENT-NAME: Synthetic Employee",
        "RECEIPT-REFERENCE: R-SYNTHETIC",
    };

    // --------------------------------------------------------- FR-21: positive

    [Fact] // FR-21
    public async Task ASignatureLineThenTheHeaderClassifiesAsAT4()
    {
        var result = await Provider().ClassifyAsync(
            Bridged(Signature, T4Body), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(DocumentClassification.T4Slip, result!.Classification);
        Assert.Equal(0.75m, result.Confidence);
    }

    [Fact] // FR-21
    public async Task ASignatureLineThenTheHeaderClassifiesAsMedicalEvidence()
    {
        var provider = Provider();

        var classified = await provider.ClassifyAsync(
            Bridged(Signature, MedicalBody), CancellationToken.None);
        var extracted = await provider.ExtractMedicalAsync(
            Bridged(Signature, MedicalBody), CancellationToken.None);

        Assert.Equal(DocumentClassification.MedicalExpenseEvidence, classified!.Classification);
        Assert.Equal("14 mars 2025", extracted!.ServiceDateAsPrinted);
        Assert.Equal("$1,120.50", extracted.GrossAmountAsPrinted);
        Assert.Equal("Synthetic Dental Clinic", extracted.ProviderDisplayName);
    }

    [Fact] // FR-21: "the remainder is read exactly as today".
    public async Task TheBridgedFormExtractsExactlyWhatTheHeaderFirstFormExtracts()
    {
        var provider = Provider();

        var legacy = await provider.ExtractT4Async(HeaderFirst(T4Body), CancellationToken.None);
        var bridged = await provider.ExtractT4Async(
            Bridged(Signature, T4Body), CancellationToken.None);

        Assert.NotNull(legacy);
        Assert.NotNull(bridged);
        Assert.Equal(legacy!.Year, bridged!.Year);
        Assert.Equal(legacy.EmployerName, bridged.EmployerName);
        Assert.Equal(legacy.EmployerAddress, bridged.EmployerAddress);
        Assert.Equal(legacy.EmployeeName, bridged.EmployeeName);
        Assert.Equal(legacy.EmployeeAddress, bridged.EmployeeAddress);
        Assert.Equal(legacy.ExemptCppQpp, bridged.ExemptCppQpp);
        Assert.Equal(legacy.ExemptEi, bridged.ExemptEi);
        Assert.Equal(legacy.ExemptPpip, bridged.ExemptPpip);
        Assert.Equal(legacy.Confidence, bridged.Confidence);
        Assert.Equal(
            legacy.Boxes!.Select(box => (box!.Code, box.Value, box.Confidence)).ToArray(),
            bridged.Boxes!.Select(box => (box!.Code, box.Value, box.Confidence)).ToArray());

        // The skipped line contributes nothing: no box, no field, no value.
        Assert.Equal(
            new (string?, string?)[] { ("14", "50000.00"), ("45", "1") },
            bridged.Boxes!.Select(box => (box!.Code, box.Value)).ToArray());
    }

    [Theory] // FR-21: the line only has to begin with the signature.
    [InlineData("%PDF-")]
    [InlineData("%PDF-1.4")]
    [InlineData("%PDF-2.0")]
    [InlineData("%PDF-1.7 synthetic sample")]
    public async Task AnySignatureLineIsSkippedWithoutBeingRead(string signatureLine)
    {
        var result = await Provider().ClassifyAsync(
            Bridged(signatureLine, T4Body), CancellationToken.None);

        Assert.Equal(DocumentClassification.T4Slip, result!.Classification);
    }

    [Fact] // FR-21: "next non-empty line" — blank lines and line endings are not lines of substance.
    public async Task BlankLinesWindowsLineEndingsAndAByteOrderMarkAreStillTolerated()
    {
        var text = "﻿" + string.Join("\r\n", new[]
        {
            string.Empty,
            Signature,
            string.Empty,
            "   ",
            Header,
            string.Empty,
            "KIND: T4",
            "BOX 14: 50000.00",
        });

        var result = await Provider().ExtractT4Async(
            new MemoryStream(Encoding.UTF8.GetBytes(text)), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("50000.00", result!.Boxes!.Single()!.Value);
    }

    // ----------------------------------------------------------------- AC-40

    public static TheoryData<string, string[]> SignatureWithoutTheHeader() => new()
    {
        { "signature only", new[] { Signature } },
        { "signature then fields", new[] { Signature, "KIND: T4", "BOX 14: 50000.00" } },
        { "signature then another header", new[] { Signature, "SOME-OTHER-DOCUMENT/1", "KIND: T4" } },
        { "signature then a near-miss header", new[] { Signature, "x" + Header, "KIND: T4" } },
        { "signature then a lower-case header", new[] { Signature, Header.ToLowerInvariant(), "KIND: T4" } },
        {
            "the opening of a real PDF",
            new[] { Signature, "1 0 obj", "<< /Type /Catalog /Pages 2 0 R >>", "endobj", "%%EOF" }
        },
    };

    [Theory] // AC-40
    [MemberData(nameof(SignatureWithoutTheHeader))]
    public async Task ASignatureNotFollowedByTheHeaderIsUnsupported(
        string description,
        string[] lines)
    {
        await AssertUnsupportedAsync(Encoding.UTF8.GetBytes(string.Join("\n", lines)));

        Assert.NotEmpty(description);
    }

    [Fact] // AC-40, FR-22: nothing is inferred from PDF content, even content that names a kind.
    public async Task PdfStructureIsNeverReadForAPayload()
    {
        var lines = new[]
        {
            Signature,
            "1 0 obj",
            "stream",
            Header,
            "KIND: T4",
            "BOX 14: 50000.00",
            "endstream",
        };

        await AssertUnsupportedAsync(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
    }

    // ----------------------------------------------------------------- AC-41

    [Fact] // AC-41
    public async Task TheHeaderMustImmediatelyFollowTheSignatureLine()
    {
        var lines = new[] { Signature, "%âãÏÓ", Header }.Concat(T4Body);

        await AssertUnsupportedAsync(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
    }

    [Fact] // AC-41: one signature line is tolerated, not any number of them.
    public async Task ASecondSignatureLineIsNotTolerated()
    {
        var lines = new[] { Signature, Signature, Header }.Concat(T4Body);

        await AssertUnsupportedAsync(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
    }

    [Theory] // AC-41: the tolerated line must itself be the first line and begin with the signature.
    [InlineData("%pdf-1.7")]
    [InlineData("PDF-1.7")]
    [InlineData("x%PDF-1.7")]
    [InlineData("%PDF")]
    [InlineData("%PDF 1.7")]
    public async Task ALineThatOnlyResemblesTheSignatureIsNotTolerated(string firstLine)
    {
        var lines = new[] { firstLine, Header }.Concat(T4Body);

        await AssertUnsupportedAsync(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
    }

    [Fact] // AC-41: the signature is tolerated before the header only, never as a second header.
    public async Task ASignatureLineAfterAnotherLineIsNotTolerated()
    {
        var lines = new[] { "leading text", Signature, Header }.Concat(T4Body);

        await AssertUnsupportedAsync(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
    }

    // ----------------------------------------------------------------- AC-42

    [Fact] // AC-42
    public async Task AJpegSignatureGainsNoBridge()
    {
        byte[] signature = [0xFF, 0xD8, 0xFF];

        await AssertUnsupportedAsync(WithBinarySignature(signature));
    }

    [Fact] // AC-42
    public async Task APngSignatureGainsNoBridge()
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        await AssertUnsupportedAsync(WithBinarySignature(signature));
    }

    [Fact] // FR-22: decoding stays strict over the whole source, signature or not.
    public async Task ABridgedSourceThatIsNotValidUtf8IsUnsupported()
    {
        byte[] content =
        [
            .. Encoding.UTF8.GetBytes($"{Signature}\n{Header}\nKIND: T4\nBOX 14: "),
            0xFF, 0xFE,
            .. Encoding.UTF8.GetBytes("\n"),
        ];

        await AssertUnsupportedAsync(content);
    }

    // ----------------------------------------------------------------- AC-43

    [Fact] // AC-43
    public async Task TheHeaderFirstFormIsStillRecognized()
    {
        var provider = Provider();

        var t4 = await provider.ClassifyAsync(HeaderFirst(T4Body), CancellationToken.None);
        var medical = await provider.ClassifyAsync(HeaderFirst(MedicalBody), CancellationToken.None);
        var extracted = await provider.ExtractT4Async(HeaderFirst(T4Body), CancellationToken.None);

        Assert.Equal(DocumentClassification.T4Slip, t4!.Classification);
        Assert.Equal(0.75m, t4.Confidence);
        Assert.Equal(DocumentClassification.MedicalExpenseEvidence, medical!.Classification);
        Assert.Equal(2025, extracted!.Year);
        Assert.Equal("Synthetic Employer", extracted.EmployerName);
        Assert.Equal(
            new (string?, string?, decimal?)[] { ("14", "50000.00", 0.91m), ("45", "1", null) },
            extracted.Boxes!.Select(box => (box!.Code, box.Value, box.Confidence)).ToArray());
    }

    [Fact] // AC-43: after the header, a signature-looking line is the ordinary ignored line it always was.
    public async Task ASignatureLineAfterTheHeaderChangesNothing()
    {
        var lines = new[] { Header, Signature }.Concat(T4Body);

        var result = await Provider().ExtractT4Async(
            new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\n", lines))),
            CancellationToken.None);

        Assert.Equal(2, result!.Boxes!.Count);
        Assert.Equal("Synthetic Employer", result.EmployerName);
    }

    // ----------------------------------------------------------------- AC-45

    [Fact] // AC-45
    public void TheProjectGainsNoParsingDependency()
    {
        var project = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "src", "DocumentIntelligence", "DocumentIntelligence.csproj"));

        Assert.Equal(
            new[]
            {
                "Azure.Core|1.54.0",
                "Microsoft.Extensions.DependencyInjection.Abstractions|10.0.10",
            },
            project.Descendants("PackageReference")
                .Select(reference =>
                    $"{reference.Attribute("Include")!.Value}|{reference.Attribute("Version")!.Value}")
                .OrderBy(reference => reference, StringComparer.Ordinal)
                .ToArray());

        Assert.Equal(
            new[] { @"..\ExtractionWorkflow\ExtractionWorkflow.csproj" },
            project.Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")!.Value)
                .ToArray());
    }

    // ----------------------------------------------------------------- helpers

    private static async Task AssertUnsupportedAsync(byte[] content)
    {
        var provider = Provider();

        var classification = await provider.ClassifyAsync(
            new MemoryStream(content), CancellationToken.None);

        Assert.NotNull(classification);
        Assert.Equal(DocumentClassification.Unsupported, classification!.Classification);
        Assert.Null(classification.Confidence);

        // No T4 payload and no medical version can come from it.
        Assert.Null(await provider.ExtractT4Async(new MemoryStream(content), CancellationToken.None));
        Assert.Null(await provider.ExtractMedicalAsync(new MemoryStream(content), CancellationToken.None));
    }

    private static byte[] WithBinarySignature(byte[] signature) =>
    [
        .. signature,
        .. Encoding.UTF8.GetBytes("\n" + string.Join("\n", new[] { Header }.Concat(T4Body))),
    ];

    private static MemoryStream Bridged(string signatureLine, IEnumerable<string> body) =>
        DeterministicProviderTests.Document(new[] { signatureLine, Header }.Concat(body));

    private static MemoryStream HeaderFirst(IEnumerable<string> body) =>
        DeterministicProviderTests.Document(new[] { Header }.Concat(body));

    private static DeterministicDocumentIntelligenceProvider Provider() =>
        DeterministicProviderTests.Provider();

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
