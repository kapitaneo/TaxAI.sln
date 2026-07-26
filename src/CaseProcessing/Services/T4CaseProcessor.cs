using CaseProcessing.Models;
using DocumentExtraction.Models;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace CaseProcessing.Services;

/// <summary>
/// The module's single entry point: drives one already-validated T4 extraction
/// through the existing engine and returns an attributed, fail-closed result.
/// Synchronous, pure, and deterministic — the same extraction and the same
/// provider always yield an equal result, including equal error ordering.
/// <para>
/// Every tax judgement is delegated to code that already shipped: the typed slip
/// comes only from <see cref="T4SlipParser"/> and the CRA line entries only from
/// <see cref="T4LineMapper"/>. Engine errors are surfaced verbatim and in order —
/// never translated, summarized, re-worded, or filtered. This module performs no
/// arithmetic, reads no mapping resource, and contains no CRA line number.
/// </para>
/// <para>
/// <see cref="Process"/> takes a <b>nullable</b> extraction and fails closed on
/// null rather than throwing: this module is the untrusted-input boundary, so
/// nothing it is handed may escape as an exception. (<c>T4SlipParser.Parse</c>
/// throws on null; that difference is deliberate.)
/// </para>
/// <para>
/// <see cref="IT4LineMapProvider"/> is constructor-injected so the year requested
/// is observable — a recording double can prove exactly one year was asked for,
/// and that it was the slip's. <see cref="T4SlipParser"/> and
/// <see cref="T4LineMapper"/> are constructed internally: they have no policy to
/// vary, so injecting them would add surface without adding a guarantee.
/// </para>
/// </summary>
public sealed class T4CaseProcessor
{
    // Fixed message templates. Deliberately constants with no interpolation: a
    // template that cannot take an argument cannot leak a caller or payload
    // value. The supported year is never spelled out either, so the year policy
    // stays in SupportedProcessingTaxYears alone.
    private const string ExtractionRequired = "An extraction is required.";
    private const string KindNotT4Slip = "The extraction is not a T4 slip extraction.";
    private const string PayloadRequired = "A T4 slip extraction requires a T4 payload.";
    private const string TaxYearUnsupported = "TaxYear is not a supported processing tax year.";
    private const string SlipYearMismatch = "The parsed slip's tax year must equal the extraction's tax year.";

    private readonly T4SlipParser _parser;
    private readonly T4LineMapper _lineMapper;

    /// <summary>Creates a processor that resolves box→line maps through <paramref name="lineMapProvider"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="lineMapProvider"/> is null.</exception>
    public T4CaseProcessor(IT4LineMapProvider lineMapProvider)
    {
        ArgumentNullException.ThrowIfNull(lineMapProvider);

        _parser = new T4SlipParser();
        _lineMapper = new T4LineMapper(lineMapProvider);
    }

    /// <summary>
    /// Processes one validated extraction, returning the processed result on
    /// success or the ordered validation errors on any failure — never both, and
    /// never an exception.
    /// <para>
    /// The eight stages run in fixed order. Stages 3 and 4 accumulate together
    /// because they are independent — an unsupported year and a malformed box
    /// collection are separate defects, and a caller fixing one should learn
    /// about the other in the same pass. Every other stage short-circuits,
    /// because each strictly depends on the previous one having succeeded;
    /// continuing would mean guessing what an absent value "meant", which is the
    /// coercion this contract forbids.
    /// </para>
    /// </summary>
    public ParseResult<ProcessedT4Extraction> Process(ValidatedExtraction? extraction)
    {
        // Stage 1 — null extraction. A single error, never an exception.
        if (extraction is null)
        {
            return Failure(T4ProcessingFields.Extraction, ExtractionRequired);
        }

        // Stage 2 — kind, then payload presence. Sequential rather than
        // accumulating: a non-T4 extraction correctly carries no payload (spec
        // 007 forbids one), so also reporting "a payload is required" would
        // invent a second defect that is not there.
        // [ASSUMED] architecture.md states stage 2 as one rule yielding
        // "error(s)"; reporting the kind alone for a non-T4 kind is the reading
        // that never manufactures a false defect. One-pass human override.
        if (extraction.Kind != ExtractedDocumentKind.T4Slip)
        {
            return Failure(T4ProcessingFields.Kind, KindNotT4Slip);
        }

        var payload = extraction.T4Payload;
        if (payload is null)
        {
            return Failure(T4ProcessingFields.Payload, PayloadRequired);
        }

        var errors = new List<ValidationError>();

        // Stage 3 — the extraction's year must be a supported processing year.
        // Never coerced, and no adjacent-year fallback: the policy lives in
        // SupportedProcessingTaxYears and nowhere else.
        if (!SupportedProcessingTaxYears.IsSupported(extraction.TaxYear))
        {
            errors.Add(new ValidationError(T4ProcessingFields.TaxYear, TaxYearUnsupported));
        }

        // Stage 4 — the payload must be structurally traversable before the
        // parser touches it, because T4SlipParser.Parse dereferences these
        // members without a null guard.
        errors.AddRange(PayloadStructureGuard.Inspect(payload));

        if (errors.Count > 0)
        {
            return ParseResult<ProcessedT4Extraction>.Failure(errors);
        }

        // Stage 5 — parsing, delegated in full. On failure the parser's own
        // errors are surfaced verbatim and in order, the mapper is never
        // invoked, and the provider is never queried.
        var parsed = _parser.Parse(payload);
        if (parsed.IsFailure)
        {
            return ParseResult<ProcessedT4Extraction>.Failure(parsed.Errors);
        }

        var slip = parsed.Value!;

        // Stage 6 — year coherence between the extraction and the parsed slip,
        // with neither value coerced. Defensive today: the supported processing
        // set is [2025] and the parser accepts 2025 only, so a slip that parses
        // cannot disagree with an extraction that reached this line. The guard
        // stays because widening either policy independently is exactly the kind
        // of change that would otherwise mix years silently.
        if (slip.Year != extraction.TaxYear)
        {
            return Failure(T4ProcessingFields.SlipYear, SlipYearMismatch);
        }

        // Stage 7 — mapping, delegated in full. The mapper asks the provider for
        // the slip's year and no other; on failure its errors (or the
        // provider's) are surfaced verbatim, with no retry for another year.
        var mapped = _lineMapper.Map(new[] { slip });
        if (mapped.IsFailure)
        {
            return ParseResult<ProcessedT4Extraction>.Failure(mapped.Errors);
        }

        // Stage 8 — success. Attribution is echoed exactly as supplied: opaque to
        // this module, never trimmed, normalized, or generated.
        return ParseResult<ProcessedT4Extraction>.Success(
            new ProcessedT4Extraction(
                extraction.DocumentId,
                extraction.FirmId,
                extraction.CaseId,
                extraction.ParticipantId,
                extraction.TaxYear,
                extraction.SourceReference,
                slip,
                mapped.Value!));
    }

    /// <summary>Fixed structural facts only — this type carries no caller or taxpayer data.</summary>
    public override string ToString() => nameof(T4CaseProcessor);

    private static ParseResult<ProcessedT4Extraction> Failure(string field, string message) =>
        ParseResult<ProcessedT4Extraction>.Failure(new ValidationError(field, message));
}
