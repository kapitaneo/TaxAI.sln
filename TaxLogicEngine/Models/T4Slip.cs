namespace TaxLogicEngine.Models;

/// <summary>
/// A single, validated T4 (Statement of Remuneration Paid) slip for exactly one
/// tax year (2025). Built by the parser; amount boxes are stored as
/// <see cref="decimal"/> already rounded to cents. Box codes remain the canonical
/// identifiers — no CRA line number appears here (mapping is spec 004).
/// <para>
/// <see cref="ToString"/> never emits the full Box 12 SIN; it defers to
/// <see cref="RecipientIdentifier"/>'s masking. Absent optional amount boxes
/// default to <c>0m</c>, absent optional scalars to <c>null</c>, and unmarked
/// exempt indicators to <c>false</c>.
/// </para>
/// <para>
/// Wave 0 contract stage: members are declared but <see cref="ToString"/> is not
/// yet implemented — it is completed with the parser in a later task.
/// </para>
/// </summary>
public sealed class T4Slip
{
    /// <summary>The four-digit calendar year. Exactly one year per slip (2025).</summary>
    public required int Year { get; init; }

    // Identification
    public required string EmployerName { get; init; }
    public string? EmployerAddress { get; init; }
    public required string EmployeeName { get; init; }
    public string? EmployeeAddress { get; init; }

    /// <summary>Box 12 employee SIN, masked on output. May be the <c>000000000</c> no-SIN sentinel.</summary>
    public RecipientIdentifier? Sin { get; init; }

    /// <summary>Box 54 employer's account number (not on the employee copy).</summary>
    public string? EmployerAccountNumber { get; init; }

    /// <summary>Box 50 RPP or DPSP registration number.</summary>
    public string? RppDpspRegistrationNumber { get; init; }

    // Coded / constrained boxes
    /// <summary>Box 10 province of employment (two-letter abbreviation), when present.</summary>
    public string? ProvinceOfEmployment { get; init; }

    /// <summary>Box 29 employment code (11–17), when present.</summary>
    public string? EmploymentCode { get; init; }

    /// <summary>Box 45 employer-offered dental benefits code (1–5).</summary>
    public required string DentalBenefitCode { get; init; }

    // Box 28 – Exempt indicators
    public bool ExemptCppQpp { get; init; }
    public bool ExemptEi { get; init; }
    public bool ExemptPpip { get; init; }

    // Amount boxes (decimal, cents-rounded). Absent boxes default to 0.
    public decimal EmploymentIncome { get; init; }              // 14
    public decimal CppContributions { get; init; }              // 16
    public decimal SecondCppContributions { get; init; }        // 16A
    public decimal QppContributions { get; init; }              // 17
    public decimal SecondQppContributions { get; init; }        // 17A
    public decimal EiPremiums { get; init; }                    // 18
    public decimal RppContributions { get; init; }              // 20
    public decimal IncomeTaxDeducted { get; init; }             // 22
    public decimal EiInsurableEarnings { get; init; }           // 24
    public decimal CppQppPensionableEarnings { get; init; }     // 26
    public decimal UnionDues { get; init; }                     // 44
    public decimal CharitableDonations { get; init; }           // 46
    public decimal PensionAdjustment { get; init; }             // 52
    public decimal PpipPremiums { get; init; }                  // 55
    public decimal PpipInsurableEarnings { get; init; }         // 56

    /// <summary>Non-standard "Other information" code/amount pairs, preserved verbatim.</summary>
    public IReadOnlyList<T4OtherInformation> OtherInformation { get; init; } = Array.Empty<T4OtherInformation>();

    /// <summary>
    /// SIN-safe summary. Not yet implemented at the Wave 0 contract stage — the
    /// masking-backed override is completed with the parser.
    /// </summary>
    public override string ToString() => throw new NotImplementedException();
}
