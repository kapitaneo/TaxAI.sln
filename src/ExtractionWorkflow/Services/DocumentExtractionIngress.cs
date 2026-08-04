using DocumentExtraction.Models;
using DocumentExtraction.Ports;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;

namespace ExtractionWorkflow.Services;

/// <summary>
/// The single implementation of the ingestion port spec 007 declared and left
/// deliberately unimplemented.
/// <para>
/// It <b>delegates</b>. The gate is the existing
/// <see cref="ExtractionEnvelopeValidator.Validate"/> and nothing else: this
/// class re-implements no rule, declares no second envelope, validator,
/// provenance record, document-kind discriminator, or supported-year authority,
/// and adds no check of its own before or after. A second gate beside the first
/// is two answers to the same question, and the one that runs would be whichever
/// caller happened to be used.
/// </para>
/// <para>
/// The port's asynchronous shape is signature-only, exactly as spec 007
/// documented it. Validation is synchronous, static, and pure, so this method
/// performs no I/O and completes synchronously; the returned task is already
/// finished when it is handed back.
/// </para>
/// <para>
/// Fail-closed on the same terms as the validator: a null envelope yields one
/// error rather than an exception, failure carries at least one
/// <see cref="ValidationError"/> and no value, and success carries a
/// <see cref="ValidatedExtraction"/> whose payload is the one the envelope
/// carried — reference-identical and value-unchanged, duplicate box codes and
/// all.
/// </para>
/// </summary>
public sealed class DocumentExtractionIngress : IDocumentExtractionIngress
{
    /// <inheritdoc />
    public Task<ParseResult<ValidatedExtraction>> SubmitAsync(
        ExtractionEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ExtractionEnvelopeValidator.Validate(envelope));
    }

    /// <summary>Fixed type identity — this type carries no caller or taxpayer data.</summary>
    public override string ToString() => nameof(DocumentExtractionIngress);
}
