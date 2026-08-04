using System.Globalization;
using System.Text;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;

namespace DocumentIntelligence;

/// <summary>
/// The only document-intelligence provider spec 015 ships: an in-process,
/// deterministic adapter that computes its answer from the source bytes alone.
/// <para>
/// It makes no network call, holds no SDK, and sends nothing anywhere. Taxpayer
/// source bytes do not leave this process in this specification, so a real
/// document-intelligence service — and with it the residency, encryption in
/// transit, zero-retention, secrets, and operations questions — stays blocked
/// until a separate infrastructure specification answers them. This adapter is
/// registered for Development and test hosts only.
/// </para>
/// <para>
/// It reads one fixed, self-describing text format. Anything else — a photo, a
/// PDF, an empty file, a text file with the wrong header — classifies as
/// <see cref="DocumentClassification.Unsupported"/>. <b>It never guesses.</b> A
/// document that is not recognizably one of the two supported kinds produces no
/// tax input at all, which is what stops a picture of a driver's licence in a T4
/// checklist slot from becoming a slip.
/// </para>
/// <para>
/// Nothing here decides anything about tax. Field values are copied as printed
/// into untrusted result shapes; no box code is looked up, required, defaulted,
/// or given a meaning, no amount is parsed or rounded, no date is normalized,
/// and no value is invented for a field the document did not show. The year it
/// reports is the year the document printed — the year an extraction is
/// attributed to comes from the server-stored intake case instead.
/// </para>
/// <para>
/// Confidence is transported, never invented: the document declares it or it is
/// absent. It is copied verbatim, including a value outside the accepted range,
/// so the existing envelope validator remains the one place a range is checked.
/// </para>
/// <para>
/// <b>It never throws.</b> An absent or unreadable stream, content beyond the
/// configured limit, and a cancelled operation all produce a null result — the
/// fail-closed outcome — and never a partial one.
/// </para>
/// </summary>
public sealed class DeterministicDocumentIntelligenceProvider : IDocumentIntelligenceProvider
{
    /// <summary>
    /// The first line every recognizable document carries. A fixed header is
    /// what makes "this adapter recognized it" a fact rather than a heuristic.
    /// </summary>
    public const string DocumentHeader = "TAXAI-DETERMINISTIC-DOCUMENT/1";

    private const string KindKey = "KIND";
    private const string T4KindValue = "T4";
    private const string MedicalKindValue = "MEDICAL-EXPENSE-EVIDENCE";

    private const string ConfidenceKey = "CONFIDENCE";
    private const string YearKey = "YEAR";
    private const string EmployerNameKey = "EMPLOYER-NAME";
    private const string EmployerAddressKey = "EMPLOYER-ADDRESS";
    private const string EmployeeNameKey = "EMPLOYEE-NAME";
    private const string EmployeeAddressKey = "EMPLOYEE-ADDRESS";
    private const string ExemptCppQppKey = "EXEMPT-CPP-QPP";
    private const string ExemptEiKey = "EXEMPT-EI";
    private const string ExemptPpipKey = "EXEMPT-PPIP";
    private const string BoxKeyPrefix = "BOX ";
    private const string BoxConfidenceKeyPrefix = "BOX-CONFIDENCE ";

    private const string ServiceDateKey = "SERVICE-DATE";
    private const string GrossAmountKey = "GROSS-AMOUNT";
    private const string CurrencyKey = "CURRENCY";
    private const string ProviderNameKey = "PROVIDER-NAME";
    private const string PatientNameKey = "PATIENT-NAME";
    private const string ReceiptReferenceKey = "RECEIPT-REFERENCE";

    private const int ReadChunkBytes = 8192;

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    /// <summary>The byte-order mark, stripped before the header is compared.</summary>
    private const char ByteOrderMark = '\uFEFF';

    private readonly DocumentIntelligenceOptions _options;

    /// <exception cref="ArgumentNullException">The options are absent.</exception>
    /// <exception cref="ArgumentException">A configured value is missing, empty, or invalid.</exception>
    public DeterministicDocumentIntelligenceProvider(DocumentIntelligenceOptions options) =>
        _options = DocumentIntelligenceOptions.RequireValid(options);

    /// <inheritdoc />
    public async Task<ProviderClassificationResult?> ClassifyAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(source, cancellationToken).ConfigureAwait(false);

        if (document is null)
        {
            return null;
        }

        return new ProviderClassificationResult
        {
            Classification = document.Classification,
            Confidence = ReadConfidence(document),
            ProviderLabel = _options.ProviderLabel,
        };
    }

    /// <inheritdoc />
    public async Task<ProviderT4Result?> ExtractT4Async(
        Stream source,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(source, cancellationToken).ConfigureAwait(false);

        if (document is null || document.Classification != DocumentClassification.T4Slip)
        {
            return null;
        }

        return new ProviderT4Result
        {
            Year = ReadYear(document),
            EmployerName = document.Value(EmployerNameKey),
            EmployerAddress = document.Value(EmployerAddressKey),
            EmployeeName = document.Value(EmployeeNameKey),
            EmployeeAddress = document.Value(EmployeeAddressKey),
            ExemptCppQpp = ReadFlag(document, ExemptCppQppKey),
            ExemptEi = ReadFlag(document, ExemptEiKey),
            ExemptPpip = ReadFlag(document, ExemptPpipKey),
            Boxes = ReadBoxes(document),
            Confidence = ReadConfidence(document),
        };
    }

    /// <inheritdoc />
    public async Task<ProviderMedicalResult?> ExtractMedicalAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(source, cancellationToken).ConfigureAwait(false);

        if (document is null ||
            document.Classification != DocumentClassification.MedicalExpenseEvidence)
        {
            return null;
        }

        return new ProviderMedicalResult
        {
            ServiceDateAsPrinted = document.Value(ServiceDateKey),
            GrossAmountAsPrinted = document.Value(GrossAmountKey),
            CurrencyCodeAsPrinted = document.Value(CurrencyKey),
            ProviderDisplayName = document.Value(ProviderNameKey),
            PatientDisplayName = document.Value(PatientNameKey),
            ReceiptReference = document.Value(ReceiptReferenceKey),
            Confidence = ReadConfidence(document),
        };
    }

    // ------------------------------------------------------------- reading

    /// <summary>
    /// Reads and parses the source, or returns null when there is nothing this
    /// adapter can honestly say about it. Every failure path lands here, so no
    /// caller ever sees an exception from untrusted content.
    /// </summary>
    private async Task<ParsedDocument?> ReadDocumentAsync(
        Stream? source,
        CancellationToken cancellationToken)
    {
        if (source is null)
        {
            return null;
        }

        byte[] content;

        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[ReadChunkBytes];
            long total = 0;

            while (true)
            {
                var read = await source
                    .ReadAsync(chunk.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                total += read;

                // Refused, never truncated: a truncated document would extract
                // as a document that merely happens to be missing fields.
                if (total > _options.MaximumSourceBytes || total > int.MaxValue)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            content = buffer.ToArray();
        }
        catch (Exception)
        {
            // Fail closed on anything the stream does, including cancellation.
            // Nothing partial is returned and nothing is thrown to the caller.
            return null;
        }

        return Parse(content);
    }

    /// <summary>
    /// Turns bytes into a parsed document. Content this adapter cannot decode is
    /// a document it does not recognize, not a failure to read: it classifies as
    /// unsupported, exactly like a text file with the wrong header.
    /// </summary>
    private static ParsedDocument Parse(byte[] content)
    {
        string text;

        try
        {
            text = StrictUtf8.GetString(content).TrimStart(ByteOrderMark);
        }
        catch (DecoderFallbackException)
        {
            return ParsedDocument.Unsupported;
        }

        var entries = new List<KeyValuePair<string, string?>>();
        var headerSeen = false;
        var classification = DocumentClassification.Unsupported;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (!headerSeen)
            {
                // The header must be the first thing of substance in the file.
                if (!string.Equals(line, DocumentHeader, StringComparison.Ordinal))
                {
                    return ParsedDocument.Unsupported;
                }

                headerSeen = true;
                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);

            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (key.Length == 0)
            {
                continue;
            }

            entries.Add(new KeyValuePair<string, string?>(
                key,
                value.Length == 0 ? null : value));
        }

        if (!headerSeen)
        {
            return ParsedDocument.Unsupported;
        }

        var kind = FirstValue(entries, KindKey);

        if (string.Equals(kind, T4KindValue, StringComparison.OrdinalIgnoreCase))
        {
            classification = DocumentClassification.T4Slip;
        }
        else if (string.Equals(kind, MedicalKindValue, StringComparison.OrdinalIgnoreCase))
        {
            classification = DocumentClassification.MedicalExpenseEvidence;
        }

        return new ParsedDocument(classification, entries);
    }

    // ----------------------------------------------------------- projection

    private static int? ReadYear(ParsedDocument document) =>
        int.TryParse(
            document.Value(YearKey),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var year)
            ? year
            : null;

    /// <summary>
    /// Copies a declared confidence verbatim, including a value outside the
    /// accepted range, so the existing envelope validator stays the single place
    /// that range is enforced. An undeclared or unreadable one stays absent —
    /// this adapter never invents a confidence, and no threshold exists here.
    /// </summary>
    private static decimal? ReadConfidence(ParsedDocument document) =>
        decimal.TryParse(
            document.Value(ConfidenceKey),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var confidence)
            ? confidence
            : null;

    /// <summary>
    /// Reads a checkbox indicator. Anything that is not an exact yes or no stays
    /// absent rather than becoming a false.
    /// </summary>
    private static bool? ReadFlag(ParsedDocument document, string key) =>
        document.Value(key) switch
        {
            null => null,
            var value when string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) => true,
            var value when string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) => false,
            _ => null,
        };

    /// <summary>
    /// Copies the boxes in the order the document listed them, duplicates
    /// included. Collapsing or reordering them would hide exactly what the
    /// engine's own duplicate-box rejection exists to catch.
    /// </summary>
    private static IReadOnlyList<ProviderBoxValue?> ReadBoxes(ParsedDocument document)
    {
        var boxes = new List<ProviderBoxValue?>();

        foreach (var entry in document.Entries)
        {
            if (!entry.Key.StartsWith(BoxKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Taken verbatim: normalizing an extracted code would be this module
            // deciding something about a box, and it decides nothing.
            var code = entry.Key[BoxKeyPrefix.Length..].Trim();

            if (code.Length == 0)
            {
                continue;
            }

            boxes.Add(new ProviderBoxValue
            {
                Code = code,
                Value = entry.Value,
                Confidence = ReadBoxConfidence(document, code),
            });
        }

        return boxes;
    }

    private static decimal? ReadBoxConfidence(ParsedDocument document, string code)
    {
        foreach (var entry in document.Entries)
        {
            if (!entry.Key.StartsWith(BoxConfidenceKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var declaredCode = entry.Key[BoxConfidenceKeyPrefix.Length..].Trim();

            if (!string.Equals(declaredCode, code, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return decimal.TryParse(
                entry.Value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var confidence)
                ? confidence
                : null;
        }

        return null;
    }

    private static string? FirstValue(
        IReadOnlyList<KeyValuePair<string, string?>> entries,
        string key)
    {
        foreach (var entry in entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
    }

    /// <summary>Fixed structural facts only — never the configured label.</summary>
    public override string ToString() => nameof(DeterministicDocumentIntelligenceProvider);

    /// <summary>
    /// One parsed document. The first declaration of a key wins, so a document
    /// that states the same thing twice still reads the same way every time.
    /// </summary>
    private sealed class ParsedDocument
    {
        internal ParsedDocument(
            DocumentClassification classification,
            IReadOnlyList<KeyValuePair<string, string?>> entries)
        {
            Classification = classification;
            Entries = entries;
        }

        internal static ParsedDocument Unsupported { get; } = new(
            DocumentClassification.Unsupported,
            Array.Empty<KeyValuePair<string, string?>>());

        internal DocumentClassification Classification { get; }

        internal IReadOnlyList<KeyValuePair<string, string?>> Entries { get; }

        internal string? Value(string key) => FirstValue(Entries, key);
    }
}
