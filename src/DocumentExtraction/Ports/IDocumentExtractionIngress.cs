using DocumentExtraction.Models;
using TaxLogicEngine.Models;

namespace DocumentExtraction.Ports;

/// <summary>
/// The inward application boundary for document extraction. A provider adapter
/// depends on this contract and awaits <see cref="SubmitAsync"/>; the interface
/// is implemented by a future application layer — never by the adapter, and
/// <b>no implementation ships in spec 007</b>.
/// <para>
/// The future implementation owns caller authorization and the
/// case-belongs-to-firm / participant-belongs-to-case membership checks —
/// none of which is this module. Envelope validation itself remains the
/// synchronous, static, pure <c>ExtractionEnvelopeValidator.Validate</c>; the
/// async shape here is signature-only (the interface performs no I/O, and
/// <see cref="Task"/>/<see cref="CancellationToken"/> do not weaken the
/// deterministic boundary).
/// </para>
/// </summary>
public interface IDocumentExtractionIngress
{
    /// <summary>
    /// Submits an untrusted extraction envelope. Fail-closed: failure carries at
    /// least one <see cref="ValidationError"/> and no value; success carries a
    /// <see cref="ValidatedExtraction"/> and no errors; nothing is mutated,
    /// coerced, or partially accepted.
    /// </summary>
    Task<ParseResult<ValidatedExtraction>> SubmitAsync(
        ExtractionEnvelope envelope,
        CancellationToken cancellationToken = default);
}
