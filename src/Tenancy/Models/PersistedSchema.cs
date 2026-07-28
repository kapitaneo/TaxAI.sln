namespace Tenancy.Models;

/// <summary>
/// The single source of truth for every persisted bound: column lengths, and the
/// monetary precision and scale.
/// <para>
/// Both the C# guards and the EF entity configuration read these constants, so a
/// column length can never disagree with the guard that enforces it. A guard
/// that permits 201 characters against a 200-character column is a truncation
/// waiting to happen, and a truncated taxpayer name is a wrong record.
/// </para>
/// <para>
/// No value here is a five-digit integer. Spec 010's CRA-line-number scan
/// matches <c>\b\d{5}\b</c>, and a bound written as one would be
/// indistinguishable from a hardcoded CRA line.
/// </para>
/// </summary>
public static class PersistedSchema
{
    /// <summary>Total significant digits permitted in a persisted amount.</summary>
    public const int MonetaryPrecision = 19;

    /// <summary>
    /// Digits permitted after the decimal point. Declared explicitly so no
    /// provider default decides how a taxpayer's amount is stored.
    /// </summary>
    public const int MonetaryScale = 4;

    public const int CaseIdMaxLength = 64;

    public const int ParticipantIdMaxLength = 64;

    /// <summary>Taxpayer and employer display names.</summary>
    public const int DisplayNameMaxLength = 200;

    public const int BoxCodeMaxLength = 16;

    public const int LineCodeMaxLength = 16;

    public const int SlipTypeCodeMaxLength = 8;

    public const int IssuerMaxLength = 256;

    public const int SubjectMaxLength = 256;

    /// <summary>A firm's own name. Not taxpayer data.</summary>
    public const int FirmDisplayNameMaxLength = 200;

    /// <summary>
    /// The explicit binary, case- and accent-sensitive collation every string
    /// identifier column declares, so a tenant comparison can never depend on
    /// the database's default collation.
    /// </summary>
    public const string IdentifierCollation = "Latin1_General_BIN2";
}
