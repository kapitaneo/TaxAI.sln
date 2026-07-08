namespace TaxLogicEngine.Models;

/// <summary>
/// A single, validated T5 (Statement of Investment Income) slip for exactly one
/// tax year. Built by the parser; amount boxes are stored as <see cref="decimal"/>
/// already rounded to cents. Box codes remain the canonical identifiers — no CRA
/// line number appears here (mapping is a separate, JSON-driven feature).
/// <para>
/// <see cref="ToString"/> never emits the full recipient identification number
/// (Box 22); it defers to <see cref="RecipientIdentifier"/>'s masking.
/// </para>
/// </summary>
public sealed class T5Slip
{
    /// <summary>The four-digit calendar year the income was earned. Exactly one year per slip.</summary>
    public required int TaxYear { get; init; }

    // Identification
    public required string PayerName { get; init; }
    public string? PayerAddress { get; init; }
    public required string RecipientName { get; init; }
    public string? RecipientAddress { get; init; }

    /// <summary>Box 23 recipient type (1 individual, 2 joint, 3 corporation, 4 association/trust, 5 government).</summary>
    public required int RecipientType { get; init; }

    /// <summary>Box 22 recipient identification number, masked on output. Null when not provided.</summary>
    public RecipientIdentifier? RecipientId { get; init; }

    /// <summary>Box 21 report code (O original, A amended, C cancelled).</summary>
    public string? ReportCode { get; init; }

    /// <summary>Box 28 transit number.</summary>
    public string? TransitNumber { get; init; }

    /// <summary>Box 29 recipient account number.</summary>
    public string? AccountNumber { get; init; }

    /// <summary>Box 27 currency (ISO 4217). Defaults to CAD; amounts are stored as given, never converted.</summary>
    public string CurrencyCode { get; init; } = "CAD";

    // Amount boxes (decimal, cents-rounded). Absent boxes default to 0.
    public decimal OtherThanEligibleDividendsActual { get; init; }      // 10
    public decimal OtherThanEligibleDividendsTaxable { get; init; }     // 11
    public decimal OtherThanEligibleDividendsTaxCredit { get; init; }   // 12
    public decimal InterestFromCanadianSources { get; init; }           // 13
    public decimal OtherIncomeFromCanadianSources { get; init; }        // 14
    public decimal ForeignIncome { get; init; }                         // 15
    public decimal ForeignTaxPaid { get; init; }                        // 16
    public decimal RoyaltiesFromCanadianSources { get; init; }          // 17
    public decimal CapitalGainsDividends { get; init; }                 // 18
    public decimal AccruedIncomeAnnuities { get; init; }                // 19
    public decimal EligibleDividendsActual { get; init; }               // 24
    public decimal EligibleDividendsTaxable { get; init; }              // 25
    public decimal EligibleDividendsTaxCredit { get; init; }            // 26

    /// <summary>Non-standard "Other information" code/amount pairs, preserved verbatim.</summary>
    public IReadOnlyList<T5OtherInformation> OtherInformation { get; init; } = Array.Empty<T5OtherInformation>();

    /// <summary>SIN-safe summary: the recipient id is masked (or "n/a" when absent).</summary>
    public override string ToString() =>
        $"T5Slip {TaxYear} recipient={RecipientId?.Masked ?? "n/a"} type={RecipientType} currency={CurrencyCode}";
}
