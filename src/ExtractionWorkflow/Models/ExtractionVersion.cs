using System.Globalization;
using System.Text.Json.Serialization;
using DocumentExtraction.Models;
using Tenancy.Services;

namespace ExtractionWorkflow.Models;

/// <summary>
/// What the machine said a document is, plus what a human said instead.
/// <para>
/// The automated value is retained forever. An override does not replace it, it
/// sits beside it, so the two can be shown together when they disagree and
/// neither silently wins.
/// </para>
/// <para>
/// An override may only ever name one of the two supported paths. Nothing can
/// override a document into <see cref="DocumentClassification.Unsupported"/>,
/// and — more importantly — nothing can override an unrecognized document into
/// tax input by naming an undefined value.
/// </para>
/// </summary>
public sealed class ClassificationOutcome
{
    private ClassificationOutcome(
        DocumentClassification automatedClassification,
        DocumentClassification? overriddenClassification,
        Guid? overrideActorReference,
        DateTimeOffset? overriddenAtUtc)
    {
        AutomatedClassification = automatedClassification;
        OverriddenClassification = overriddenClassification;
        OverrideActorReference = overrideActorReference;
        OverriddenAtUtc = overriddenAtUtc;
    }

    /// <summary>What the provider said, retained whatever happens later.</summary>
    public DocumentClassification AutomatedClassification { get; }

    /// <summary>What an accountant said instead, when one did.</summary>
    public DocumentClassification? OverriddenClassification { get; }

    /// <summary>
    /// The value that selects the extraction path. Absent an override the
    /// automated classification controls, which is what makes an unrecognized
    /// document produce nothing rather than whatever a checklist slot claimed.
    /// </summary>
    public DocumentClassification EffectiveClassification =>
        OverriddenClassification ?? AutomatedClassification;

    public bool IsOverridden => OverriddenClassification is not null;

    /// <summary>The opaque reference to whoever overrode it. Never a name.</summary>
    [JsonIgnore]
    public Guid? OverrideActorReference { get; }

    /// <summary>The instant supplied by the caller. Nothing here reads a clock.</summary>
    public DateTimeOffset? OverriddenAtUtc { get; }

    /// <summary>
    /// Accepts a provider's claimed classification, or refuses. An absent or
    /// undefined claim is refused rather than mapped onto a supported value.
    /// </summary>
    public static bool TryAutomated(
        DocumentClassification? claimedClassification,
        out ClassificationOutcome? outcome)
    {
        outcome = null;

        if (claimedClassification is not { } claimed || !Enum.IsDefined(claimed))
        {
            return false;
        }

        outcome = new ClassificationOutcome(claimed, null, null, null);
        return true;
    }

    /// <summary>
    /// Produces a new outcome carrying an accountant override, or refuses. The
    /// receiver is never mutated: correction history and classification history
    /// are append-only, and an instance already handed to someone else must
    /// still read the same afterwards.
    /// </summary>
    public bool TryOverride(
        DocumentClassification target,
        Guid actorReference,
        DateTimeOffset overriddenAtUtc,
        out ClassificationOutcome? outcome)
    {
        outcome = null;

        if (!Enum.IsDefined(target) || target == DocumentClassification.Unsupported)
        {
            return false;
        }

        if (actorReference == Guid.Empty ||
            UtcInstantGuard.Validate(overriddenAtUtc, nameof(OverriddenAtUtc)) is not null)
        {
            return false;
        }

        outcome = new ClassificationOutcome(
            AutomatedClassification, target, actorReference, overriddenAtUtc);
        return true;
    }

    /// <summary>Fixed structural facts only — closed enum values and a flag.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ClassificationOutcome(Automated: {0}, Effective: {1}, IsOverridden: {2})",
            AutomatedClassification,
            EffectiveClassification,
            IsOverridden);
}

/// <summary>
/// What happened to one field of one version.
/// <para>
/// It carries a field key and an outcome kind, and deliberately <b>no message
/// text</b>. A per-field outcome is a place to look, not an explanation: the
/// explanation for a refused T4 value is the engine's own
/// <c>ValidationError</c>, surfaced verbatim, and a second wording of the same
/// rule here would be a second rule.
/// </para>
/// <para>
/// The field key is omitted from serialization and from
/// <see cref="ToString"/>. Engine field keys are built from the box code the
/// document showed, and that code is untrusted text.
/// </para>
/// </summary>
public sealed class FieldValidationOutcome
{
    private FieldValidationOutcome(string field, FieldOutcomeKind kind)
    {
        Field = field;
        Kind = kind;
    }

    /// <summary>
    /// The field this outcome is about: an <see cref="ExtractionWorkflowFields"/>
    /// constant for structural fields, or the engine's own
    /// <c>ValidationError.Field</c> verbatim for a refused T4 value, so the
    /// correspondence between engine errors and outcomes stays one-to-one.
    /// </summary>
    [JsonIgnore]
    public string Field { get; }

    public FieldOutcomeKind Kind { get; }

    /// <summary>Creates an outcome, or refuses an absent field or an undefined kind.</summary>
    public static bool TryCreate(
        string? field,
        FieldOutcomeKind kind,
        out FieldValidationOutcome? outcome)
    {
        outcome = null;

        if (string.IsNullOrWhiteSpace(field) || !Enum.IsDefined(kind))
        {
            return false;
        }

        outcome = new FieldValidationOutcome(field, kind);
        return true;
    }

    /// <summary>Fixed structural facts only — the kind, never the field key.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "FieldValidationOutcome(Kind: {0})",
            Kind);
}

/// <summary>
/// One immutable extraction of one document.
/// <para>
/// A re-extraction or a correction produces a new version pointing back at the
/// one it followed; nothing is ever updated or deleted in place, and current
/// state is projected from the chain rather than written onto it.
/// </para>
/// <para>
/// The raw payload is not here. <c>ProcessedT4Extraction</c> deliberately omits
/// it so no second unmasked SIN carrier exists, and this type keeps that
/// property: the box values it holds cannot include Box 12, because
/// <see cref="ExtractionBoxValue"/> cannot represent it. When a correction needs
/// the complete payload again, the source is re-read and the payload rebuilt in
/// memory — it is never loaded from a store, because no store has it.
/// </para>
/// </summary>
public sealed class ExtractionVersion
{
    private readonly ExtractionBoxValue[] _boxValues;
    private readonly FieldValidationOutcome[] _fieldOutcomes;

    /// <exception cref="ArgumentNullException">A required reference is absent.</exception>
    /// <exception cref="ArgumentException">An identifier, instant, collection entry, or enum value is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is below one.</exception>
    public ExtractionVersion(
        Guid extractionVersionId,
        Guid documentId,
        int version,
        ClassificationOutcome classification,
        ExtractionProvenance? provenance,
        string? employerDisplayName,
        string? employeeDisplayName,
        string? employerAddress,
        string? employeeAddress,
        IReadOnlyList<ExtractionBoxValue> boxValues,
        IReadOnlyList<FieldValidationOutcome> fieldOutcomes,
        ExtractionAcceptanceState acceptanceState,
        Guid? precedingVersionId,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(boxValues);
        ArgumentNullException.ThrowIfNull(fieldOutcomes);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        RequireIdentifier(extractionVersionId, nameof(extractionVersionId));
        RequireIdentifier(documentId, nameof(documentId));

        if (!Enum.IsDefined(acceptanceState))
        {
            throw new ArgumentException(
                "The acceptance state is not a defined value.", nameof(acceptanceState));
        }

        if (precedingVersionId is { } preceding &&
            (preceding == Guid.Empty || preceding == extractionVersionId))
        {
            throw new ArgumentException(
                "The preceding version reference is not a distinct identifier.",
                nameof(precedingVersionId));
        }

        // The one UTC rule already in the repository, reused rather than restated.
        if (UtcInstantGuard.Validate(recordedAtUtc, nameof(recordedAtUtc)) is { } instantError)
        {
            throw new ArgumentException(instantError.Message, nameof(recordedAtUtc));
        }

        // Copied, so a caller holding the original list cannot change what was
        // validated after it was validated.
        _boxValues = RequireNoAbsentEntry(boxValues, nameof(boxValues));
        _fieldOutcomes = RequireNoAbsentEntry(fieldOutcomes, nameof(fieldOutcomes));

        ExtractionVersionId = extractionVersionId;
        DocumentId = documentId;
        Version = version;
        Classification = classification;
        Provenance = provenance;
        EmployerDisplayName = employerDisplayName;
        EmployeeDisplayName = employeeDisplayName;
        EmployerAddress = employerAddress;
        EmployeeAddress = employeeAddress;
        AcceptanceState = acceptanceState;
        PrecedingVersionId = precedingVersionId;
        RecordedAtUtc = recordedAtUtc;
    }

    [JsonIgnore]
    public Guid ExtractionVersionId { get; }

    [JsonIgnore]
    public Guid DocumentId { get; }

    /// <summary>The one-based position of this version in the document's chain.</summary>
    public int Version { get; }

    public ClassificationOutcome Classification { get; }

    /// <summary>The existing provenance contract, reused; no second one is declared.</summary>
    public ExtractionProvenance? Provenance { get; }

    /// <summary>Taxpayer display text, absent when the document did not show it.</summary>
    [JsonIgnore]
    public string? EmployerDisplayName { get; }

    [JsonIgnore]
    public string? EmployeeDisplayName { get; }

    /// <summary>
    /// Optional metadata. An address a scan omitted or garbled stays absent —
    /// nothing substitutes, infers, or copies a value in its place.
    /// </summary>
    [JsonIgnore]
    public string? EmployerAddress { get; }

    [JsonIgnore]
    public string? EmployeeAddress { get; }

    /// <summary>The extracted boxes that are safe to keep. Never Box 12.</summary>
    public IReadOnlyList<ExtractionBoxValue> BoxValues => _boxValues;

    public IReadOnlyList<FieldValidationOutcome> FieldOutcomes => _fieldOutcomes;

    /// <summary>Whether a human accepted this version. A machine never sets this.</summary>
    public ExtractionAcceptanceState AcceptanceState { get; }

    [JsonIgnore]
    public Guid? PrecedingVersionId { get; }

    /// <summary>The instant supplied by the caller. Nothing here reads a clock.</summary>
    public DateTimeOffset RecordedAtUtc { get; }

    /// <summary>Fixed structural facts only — counts, closed enum values, and flags.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ExtractionVersion(Version: {0}, Classification: {1}, BoxCount: {2}, OutcomeCount: {3}, Acceptance: {4}, HasPreceding: {5}, HasProvenance: {6})",
            Version,
            Classification.EffectiveClassification,
            _boxValues.Length,
            _fieldOutcomes.Length,
            AcceptanceState,
            PrecedingVersionId is not null,
            Provenance is not null);

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The identifier is absent.", parameterName);
        }
    }

    private static TEntry[] RequireNoAbsentEntry<TEntry>(
        IReadOnlyList<TEntry> entries,
        string parameterName)
        where TEntry : class
    {
        var copy = entries.ToArray();

        if (Array.Exists(copy, entry => entry is null))
        {
            throw new ArgumentException("A collection entry is absent.", parameterName);
        }

        return copy;
    }
}
