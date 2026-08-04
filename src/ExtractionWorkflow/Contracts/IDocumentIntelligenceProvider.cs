using ExtractionWorkflow.Models;

namespace ExtractionWorkflow.Contracts;

/// <summary>
/// Reads opaque source bytes and says what it thinks it saw.
/// <para>
/// The port is declared here and implemented by infrastructure, never by a
/// domain module. Everything it returns is <b>untrusted metadata</b>: a
/// classification is an opinion about bytes, an extracted value is a copy of
/// something printed on a page, and a confidence is an attention signal. None of
/// it becomes a tax fact except by surviving the existing fail-closed chain
/// unchanged and in order.
/// </para>
/// <para>
/// <b>An implementation must not throw.</b> A null return is the fail-closed
/// outcome for every failure it can encounter — an absent or unreadable stream,
/// content larger than the configured limit, a cancelled operation, or content
/// it could not decode. Handing an exception back to the caller would turn a bad
/// document into a code path the caller has to distinguish, and a partial result
/// is never returned in any case.
/// </para>
/// <para>
/// Each call consumes the supplied stream from its current position and reads it
/// once. The stream's lifetime belongs to the caller's content lease, not to the
/// provider: nothing here retains, copies, buffers to disk, or transmits the
/// bytes, and no prompt, request, or response is logged or persisted anywhere.
/// </para>
/// </summary>
public interface IDocumentIntelligenceProvider
{
    /// <summary>
    /// Says which of the two supported paths — if either — the document looks
    /// like. A document nothing recognizes classifies as
    /// <see cref="DocumentClassification.Unsupported"/> rather than being
    /// guessed into a supported kind.
    /// </summary>
    Task<ProviderClassificationResult?> ClassifyAsync(
        Stream source,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads T4 fields, or returns null when the document is not a T4. Values
    /// are copied as printed; nothing is typed, normalized, or repaired, and no
    /// box code is assigned a meaning.
    /// </summary>
    Task<ProviderT4Result?> ExtractT4Async(
        Stream source,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the approved medical fields, or returns null when the document is
    /// not a medical expense receipt. The date and amount are copied exactly as
    /// printed.
    /// </summary>
    Task<ProviderMedicalResult?> ExtractMedicalAsync(
        Stream source,
        CancellationToken cancellationToken);
}
