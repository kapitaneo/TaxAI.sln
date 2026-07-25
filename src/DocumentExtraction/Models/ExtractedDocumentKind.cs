namespace DocumentExtraction.Models;

/// <summary>
/// The kind of extracted document an <see cref="ExtractionEnvelope"/> claims to
/// carry. Values start at 1 so <c>default(ExtractedDocumentKind)</c> (0) is not
/// a defined value and fails closed like any other undefined kind.
/// <para>
/// There is deliberately <b>no T5 value</b>: T5 extraction is deferred by
/// approved scope (spec 007). A casted unknown value such as
/// <c>(ExtractedDocumentKind)99</c> is rejected by the validator as an
/// unsupported kind.
/// </para>
/// </summary>
public enum ExtractedDocumentKind
{
    /// <summary>A T4 slip extraction carrying a raw <c>T4RawInput</c> payload.</summary>
    T4Slip = 1,

    /// <summary>
    /// Generic evidence (initially medical-expense evidence). Carries no payload;
    /// its required <see cref="ExtractionProvenance.SourceReference"/> is the
    /// opaque reference to the stored document.
    /// </summary>
    GenericEvidence = 2,
}
