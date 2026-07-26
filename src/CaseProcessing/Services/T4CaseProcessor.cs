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
/// <para>
/// Wave 0 contract stage: the signatures are declared; the eight-stage pipeline
/// lands in Wave 2 (task T03).
/// </para>
/// </summary>
public sealed class T4CaseProcessor
{
    private readonly IT4LineMapProvider _lineMapProvider;

    /// <summary>Creates a processor that resolves box→line maps through <paramref name="lineMapProvider"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="lineMapProvider"/> is null.</exception>
    public T4CaseProcessor(IT4LineMapProvider lineMapProvider)
    {
        _lineMapProvider = lineMapProvider ?? throw new ArgumentNullException(nameof(lineMapProvider));
    }

    /// <summary>
    /// Processes one validated extraction, returning the processed result on
    /// success or the ordered validation errors on any failure — never both, and
    /// never an exception.
    /// </summary>
    public ParseResult<ProcessedT4Extraction> Process(ValidatedExtraction? extraction) =>
        throw new NotImplementedException("T4CaseProcessor.Process is implemented by spec 008 task T03.");

    /// <summary>Fixed structural facts only — this type carries no caller or taxpayer data.</summary>
    public override string ToString() => nameof(T4CaseProcessor);
}
