using CaseReview.Models;
using TaxLogicEngine.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Tenancy.Services;

/// <summary>
/// Converts an assembled review package into persistence entities through a
/// single explicit <strong>closed allowlist</strong>.
/// <para>
/// One projection, one allowlist, one place to read. A field reaches the
/// database because it is named here or it does not reach it at all — which is
/// what makes "no SIN is stored" a property somebody can verify by reading one
/// file rather than by auditing every write path.
/// </para>
/// <para>
/// <strong>It never reads the slip's SIN.</strong> It names no recipient
/// identifier and constructs no masking, redaction, sanitizing, or scrubbing
/// helper: PII here is handled by omission, not masking, so there is no masked
/// form to accidentally widen later. The engine's own masking behaviour is
/// untouched and is not this module's business.
/// </para>
/// <para>
/// It invents no tax truth. Every box code, CRA line code, and amount is copied
/// verbatim from the package the engine produced. There is no box-to-line table,
/// no CRA line literal, no mapping-resource read, no arithmetic, and no
/// rounding.
/// </para>
/// <para>
/// Collections are copied completely and in the package's own order with an
/// explicit stored ordinal — nothing is sorted, grouped, filtered, deduplicated,
/// merged, subtotalled, aggregated, or dropped.
/// </para>
/// <para>
/// It is pure: no clock is read, and the version a record will occupy is not
/// decided here, because the existing maximum is a fact about storage rather
/// than about the package.
/// </para>
/// <para>Wave 0 contract stage: behaviour is implemented in a later task.</para>
/// </summary>
public static class ReviewPackageProjection
{
    /// <summary>
    /// Fails closed. Nothing is written unless every guard passes, and the
    /// <strong>trusted</strong> firm — never the firm the package carried — is
    /// what the record stores.
    /// </summary>
    public static ParseResult<PersistedReviewRecord> Project(
        TrustedFirmId trustedFirm,
        AccountantReviewPackage package,
        DateTimeOffset evaluationInstantUtc,
        IOpaqueIdentifierSource identifiers) => throw new NotImplementedException();
}
