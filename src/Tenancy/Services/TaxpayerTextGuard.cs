using TaxLogicEngine.Models;

namespace Tenancy.Services;

/// <summary>
/// Guards the two taxpayer-content strings this module stores — the taxpayer
/// display name and the employer display name.
/// <para>
/// An over-length value is refused, <strong>never truncated</strong>: a
/// truncated taxpayer name is a wrong record, and it is wrong silently.
/// </para>
/// <para>
/// A value containing nine consecutive digits is refused outright. No SIN in any
/// form may reach this database, and a nine-digit run arriving inside a display
/// name is precisely how one would — pasted into the wrong field upstream, then
/// stored as a "name" nobody scans.
/// </para>
/// <para>Wave 0 contract stage: behaviour is implemented in a later task.</para>
/// </summary>
public static class TaxpayerTextGuard
{
    public static ValidationError? Validate(string? value, int maxLength, string field) =>
        throw new NotImplementedException();
}
