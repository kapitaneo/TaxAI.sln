using System.Globalization;
using System.Text;
using ExtractionWorkflow.Models;

namespace ExtractionWorkflow.Services;

/// <summary>
/// Finds a probable second copy of a document already extracted in the same
/// case.
/// <para>
/// <b>It flags; it never decides.</b> Nothing here rejects, discards, merges,
/// accepts, or excludes a document. A machine dropping a genuine second slip
/// loses income and a machine merging two scans doubles it, so the only output
/// is a relationship an accountant is asked about.
/// </para>
/// <para>
/// <b>The comparison is confined to one firm and one case by construction.</b>
/// It reads the history the caller already loaded through a firm-scoped,
/// case-scoped store member; there is no lookup, index, or query here that could
/// see another firm's, case's, or participant's document, so no comparison can
/// become a cross-tenant oracle.
/// </para>
/// <para>
/// <b>No comparison input is the SIN or is derived from it.</b> Box 12 cannot be
/// represented by <see cref="ExtractionBoxValue"/> at all, so the box multiset
/// this compares is non-SIN by construction rather than by a filter someone must
/// remember to write. Two documents that differ only in Box 12 therefore compare
/// exactly like two that agree on it, which is the point.
/// </para>
/// <para>
/// The tax year is a comparison input, and it is satisfied structurally: every
/// document compared here belongs to the same server-stored case, and a case has
/// exactly one tax year. Restating it as a literal would be a second year
/// authority.
/// </para>
/// <para>
/// <b>Box 54 is neither required nor special.</b> The payroll account number is
/// absent from employee copies, so the comparison is over whatever boxes each
/// document actually showed. Two scans of the same employee copy match without
/// it; two genuinely different slips from the same employer do not match merely
/// because the employer and taxpayer names agree, because their box multisets
/// differ.
/// </para>
/// <para>
/// Normalization is trim, case-fold, and inner-whitespace collapse — enough to
/// see through a re-scan, and applied only to the comparison. Nothing stored is
/// rewritten, and no normalized form is persisted: the stored determination is a
/// relationship between two opaque identifiers and carries no
/// taxpayer-data-derived value at all.
/// </para>
/// </summary>
public static class DuplicateCandidateDetector
{
    /// <summary>
    /// The unit separator, used only to keep two adjacent comparison fields from
    /// running together. It is a comparison detail: nothing built here is
    /// stored, rendered, logged, or returned.
    /// </summary>
    private const char FieldSeparator = '\u001F';

    /// <summary>
    /// Returns the identifier of an already-extracted document in the same case
    /// whose latest T4 version matches, or null when none does.
    /// </summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static Guid? FindT4Candidate(
        Guid documentId,
        ExtractionVersion candidate,
        StoredCaseExtractions caseExtractions)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(caseExtractions);

        var candidateIdentity = T4Identity(candidate);

        foreach (var stored in caseExtractions.Documents)
        {
            if (stored.DocumentId == documentId)
            {
                continue;
            }

            if (LatestT4Version(stored) is not { } comparison)
            {
                continue;
            }

            if (string.Equals(T4Identity(comparison), candidateIdentity, StringComparison.Ordinal))
            {
                return stored.DocumentId;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the identifier of an already-extracted document in the same case
    /// whose latest medical evidence version matches, or null when none does.
    /// </summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static Guid? FindMedicalCandidate(
        Guid documentId,
        MedicalEvidenceVersion candidate,
        StoredCaseExtractions caseExtractions)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(caseExtractions);

        var candidateIdentity = MedicalIdentity(candidate);

        foreach (var stored in caseExtractions.Documents)
        {
            if (stored.DocumentId == documentId)
            {
                continue;
            }

            if (LatestMedicalVersion(stored) is not { } comparison)
            {
                continue;
            }

            if (string.Equals(
                    MedicalIdentity(comparison), candidateIdentity, StringComparison.Ordinal))
            {
                return stored.DocumentId;
            }
        }

        return null;
    }

    // ------------------------------------------------------------- identity

    /// <summary>
    /// The approved T4 comparison inputs, in one canonical string: the employer
    /// display name, the employee display name, and the complete non-SIN box
    /// code/value multiset. The box list is ordered before comparison so the
    /// order two scans happened to be read in cannot make them differ.
    /// </summary>
    private static string T4Identity(ExtractionVersion version)
    {
        var builder = new StringBuilder();

        Append(builder, version.EmployerDisplayName);
        Append(builder, version.EmployeeDisplayName);

        var boxes = version.BoxValues
            .Select(box => string.Concat(
                Normalize(box.Code),
                "=",
                Normalize(box.Value)))
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        builder.Append(boxes.Length.ToString(CultureInfo.InvariantCulture)).Append(FieldSeparator);

        foreach (var box in boxes)
        {
            builder.Append(box).Append(FieldSeparator);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The approved medical comparison inputs, in one canonical string: date,
    /// amount, currency when present, provider display name, patient display
    /// name, and receipt reference when present. "When present" is honoured by
    /// an absent field comparing equal only to another absent field — never by
    /// treating absence as a match against anything.
    /// </summary>
    private static string MedicalIdentity(MedicalEvidenceVersion version)
    {
        var builder = new StringBuilder();

        Append(builder, version.ServiceDateAsPrinted);
        Append(builder, version.GrossAmountAsPrinted);
        Append(builder, version.Currency.IsKnown ? version.Currency.CodeAsPrinted : null);
        Append(builder, version.ProviderDisplayName);
        Append(builder, version.PatientDisplayName);
        Append(builder, version.ReceiptReference);

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string? value) =>
        builder.Append(Normalize(value)).Append(FieldSeparator);

    /// <summary>
    /// Trim, case-fold, and collapse inner whitespace. Applied to the comparison
    /// only: what was extracted is stored exactly as extracted.
    /// </summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }

    private static ExtractionVersion? LatestT4Version(StoredDocumentExtraction stored)
    {
        ExtractionVersion? latest = null;

        foreach (var version in stored.Versions)
        {
            if (latest is null || version.Version > latest.Version)
            {
                latest = version;
            }
        }

        return latest;
    }

    private static MedicalEvidenceVersion? LatestMedicalVersion(StoredDocumentExtraction stored)
    {
        MedicalEvidenceVersion? latest = null;

        foreach (var version in stored.MedicalEvidenceVersions)
        {
            if (latest is null || version.Version > latest.Version)
            {
                latest = version;
            }
        }

        return latest;
    }
}
