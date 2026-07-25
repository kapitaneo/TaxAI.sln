using DocumentExtraction.Models;
using TaxLogicEngine.Models;

namespace DocumentExtraction.Services;

/// <summary>
/// The single deterministic, pure, fail-closed gate every
/// <see cref="ExtractionEnvelope"/> passes through. Responsibility: envelope
/// well-formedness and attribution <b>presence</b> — nothing else. It never
/// authenticates a caller, never queries Tax Case Core, and proves no
/// membership. It never parses, types, normalizes, redacts, converts, maps,
/// rounds, or calculates; on success the payload passes through
/// reference-identical and value-unchanged.
/// <para>
/// Error fields use <see cref="ExtractionFields"/> constants only; error
/// messages are fixed templates carrying no caller or payload value (PII by
/// omission).
/// </para>
/// </summary>
public static class ExtractionEnvelopeValidator
{
    /// <summary>
    /// Validates an untrusted envelope. Fail-closed: failure carries at least
    /// one <see cref="ValidationError"/> and no value; success carries a
    /// <see cref="ValidatedExtraction"/> and no errors; nothing is mutated,
    /// coerced, or partially accepted. A null envelope fails with a single
    /// <see cref="ExtractionFields.Envelope"/> error — it never throws.
    /// </summary>
    public static ParseResult<ValidatedExtraction> Validate(ExtractionEnvelope? envelope)
    {
        // Wave 0 contract stub — implemented by task T02.
        throw new NotImplementedException();
    }
}
