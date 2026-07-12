namespace TaxLogicEngine.Services;

/// <summary>
/// The single place currency amounts are rounded. All monetary values in the
/// engine flow through here so the CLAUDE.md rule — decimal math, rounded
/// half-up to cents (<see cref="MidpointRounding.AwayFromZero"/>) — is applied
/// consistently and can't drift per call site.
/// </summary>
public static class MoneyRounding
{
    /// <summary>Number of decimal places in a currency amount (cents).</summary>
    public const int CurrencyDecimals = 2;

    /// <summary>
    /// Rounds <paramref name="value"/> to cents, half-up away from zero.
    /// Examples: <c>100.005 → 100.01</c>, <c>-100.005 → -100.01</c>.
    /// </summary>
    public static decimal ToCents(decimal value) =>
        Math.Round(value, CurrencyDecimals, MidpointRounding.AwayFromZero);
}
