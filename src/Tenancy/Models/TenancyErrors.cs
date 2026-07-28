namespace Tenancy.Models;

/// <summary>
/// Every message this module may emit, as a fixed constant.
/// <para>
/// None is a format string and none takes an argument. That is the whole point:
/// a template that cannot take an argument cannot echo a caller value, a
/// taxpayer name, an amount, a box code, a CRA line code, or a SIN into an error
/// that later reaches a log or a response body.
/// </para>
/// </summary>
public static class TenancyErrors
{
    public const string PackageRequired = "A review package is required.";
    public const string IdentifierSourceRequired = "An opaque identifier source is required.";
    public const string TrustedFirmRequired = "A trusted firm identifier is required.";
    public const string FirmMismatch = "The package's firm does not match the trusted firm.";
    public const string FirmIdNotCanonical = "The firm identifier is not in canonical form.";
    public const string TaxYearNotSupported = "The tax year is not supported for review persistence.";
    public const string SlipTaxYearDiverges = "A slip's tax year differs from the package's tax year.";
    public const string InstantNotUtc = "The supplied instant is not an unambiguous UTC instant.";
    public const string TokenEmpty = "The identifier is empty.";
    public const string TokenTooLong = "The identifier exceeds its permitted length.";
    public const string TokenCharacterNotPermitted = "The identifier contains a character outside the permitted set.";
    public const string TextTooLong = "The value exceeds its permitted length.";
    public const string TextContainsDigitRun = "The value contains a forbidden run of consecutive digits.";
    public const string AmountNotExactlyStorable = "The amount cannot be stored without altering its value.";
    public const string SlipTypeNotRecognized = "The slip type is not recognized.";
    public const string SessionRevoked = "The session has been revoked.";
    public const string SessionIdleExpired = "The session has passed its idle boundary.";
    public const string SessionAbsoluteExpired = "The session has passed its absolute boundary.";
    public const string UserNotActive = "The user is not active.";
    public const string MembershipNotFound = "No membership was found.";
    public const string MembershipNotActive = "The membership is not active.";
    public const string MembershipFirmMismatch = "The membership does not match the session's active firm.";
}
