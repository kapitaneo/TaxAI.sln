namespace Tenancy.Models;

/// <summary>
/// The closed set of field names this module may name in its own validation
/// errors. No caller value, taxpayer value, or payload value is ever used as a
/// field name — a field name that can carry an argument is a leak.
/// </summary>
public static class TenancyFields
{
    public const string Package = "Package";
    public const string TrustedFirmId = "TrustedFirmId";
    public const string FirmId = "FirmId";
    public const string CaseId = "CaseId";
    public const string ParticipantId = "ParticipantId";
    public const string TaxYear = "TaxYear";
    public const string SlipTaxYear = "SlipTaxYear";
    public const string SlipTypeCode = "SlipTypeCode";
    public const string TaxpayerDisplayName = "TaxpayerDisplayName";
    public const string EmployerDisplayName = "EmployerDisplayName";
    public const string BoxCode = "BoxCode";
    public const string LineCode = "LineCode";
    public const string Amount = "Amount";
    public const string EvaluationInstant = "EvaluationInstant";
    public const string CreatedAtUtc = "CreatedAtUtc";
    public const string RetentionUntilUtc = "RetentionUntilUtc";
    public const string SessionId = "SessionId";
    public const string UserId = "UserId";
    public const string MembershipId = "MembershipId";
    public const string ActiveFirmId = "ActiveFirmId";
    public const string Issuer = "Issuer";
    public const string Subject = "Subject";
    public const string IdentifierSource = "IdentifierSource";
}
