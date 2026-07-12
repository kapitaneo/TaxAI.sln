using System.Globalization;
using TaxLogicEngine.Models;

namespace TaxLogicEngine.Services;

/// <summary>
/// Turns already-extracted <see cref="T4RawInput"/> into a typed <see cref="T4Slip"/>.
/// Pure and deterministic: no I/O, no ambient state, same input → same result.
/// <para>
/// Fail-closed: input is validated first; on any problem the parser returns
/// <see cref="ParseResult{T}.Failure"/> with structured errors and <b>no</b> model,
/// so partially-valid data can never leak downstream. Validation messages never
/// echo the Box 12 SIN.
/// </para>
/// <para>
/// Scoped to tax year <b>2025 only</b>. A slip whose year is absent or is anything
/// other than 2025 fails closed — never coerced, never an adjacent-year fallback
/// (CLAUDE.md per-tax-year rule).
/// </para>
/// </summary>
public sealed class T4SlipParser
{
    /// <summary>The only tax year this parser has box definitions for.</summary>
    private const int ExpectedTaxYear = 2025;

    // Box 10 – province/territory of employment (US = employed in the US;
    // ZZ = outside Canada/US or beyond a province/territory). CRA-verified 2025.
    private static readonly HashSet<string> ProvinceCodes = new(StringComparer.Ordinal)
    {
        "AB", "BC", "MB", "NB", "NL", "NT", "NS", "NU", "ON", "PE", "QC", "SK", "YT", "US", "ZZ",
    };

    // Box 29 – employment code (blank allowed). CRA-verified 2025.
    private static readonly HashSet<string> EmploymentCodes = new(StringComparer.Ordinal)
    {
        "11", "12", "13", "14", "15", "16", "17",
    };

    // Box 45 – employer-offered dental benefits. CRA-verified 2025.
    private static readonly HashSet<string> DentalBenefitCodes = new(StringComparer.Ordinal)
    {
        "1", "2", "3", "4", "5",
    };

    // Monetary boxes: decimal, cents-rounded, must be >= 0.
    private static readonly HashSet<string> AmountBoxCodes = new(StringComparer.Ordinal)
    {
        T4BoxCodes.EmploymentIncome,
        T4BoxCodes.CppContributions,
        T4BoxCodes.SecondCppContributions,
        T4BoxCodes.QppContributions,
        T4BoxCodes.SecondQppContributions,
        T4BoxCodes.EiPremiums,
        T4BoxCodes.RppContributions,
        T4BoxCodes.IncomeTaxDeducted,
        T4BoxCodes.EiInsurableEarnings,
        T4BoxCodes.CppQppPensionableEarnings,
        T4BoxCodes.UnionDues,
        T4BoxCodes.CharitableDonations,
        T4BoxCodes.PensionAdjustment,
        T4BoxCodes.PpipPremiums,
        T4BoxCodes.PpipInsurableEarnings,
    };

    // Non-amount boxes: strings/codes, not monetary values (so exempt from amount checks).
    private static readonly HashSet<string> ScalarBoxCodes = new(StringComparer.Ordinal)
    {
        T4BoxCodes.ProvinceOfEmployment,
        T4BoxCodes.SocialInsuranceNumber,
        T4BoxCodes.EmploymentCode,
        T4BoxCodes.DentalBenefits,
        T4BoxCodes.RppDpspRegistrationNumber,
        T4BoxCodes.EmployerAccountNumber,
    };

    /// <summary>
    /// Validates and parses one slip's extracted boxes. Returns the typed slip on
    /// success, or all collected validation errors (and no model) on failure.
    /// </summary>
    public ParseResult<T4Slip> Parse(T4RawInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var errors = new List<ValidationError>();

        ValidateNoDuplicateBoxes(input, errors);
        ValidateTaxYear(input, errors);
        ValidateRequiredIdentification(input, errors);
        ValidateRequiredBoxes(input, errors);
        ValidateAmounts(input, errors);
        ValidateConstrainedValues(input, errors);
        ValidateConditionalCompanions(input, errors);

        return errors.Count > 0
            ? ParseResult<T4Slip>.Failure(errors)
            : ParseResult<T4Slip>.Success(BuildSlip(input));
    }

    private static void ValidateNoDuplicateBoxes(T4RawInput input, List<ValidationError> errors)
    {
        foreach (var group in input.Boxes.GroupBy(b => b.Code, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add(new ValidationError($"Box {group.Key}", $"Box {group.Key} appears more than once."));
        }
    }

    private static void ValidateTaxYear(T4RawInput input, List<ValidationError> errors)
    {
        if (input.Year is null)
        {
            errors.Add(new ValidationError(nameof(T4RawInput.Year), "Year is required."));
        }
        else if (input.Year != ExpectedTaxYear)
        {
            errors.Add(new ValidationError(
                nameof(T4RawInput.Year),
                $"This parser handles tax year {ExpectedTaxYear} only; year {input.Year} has no configuration and is not parsed."));
        }
    }

    private static void ValidateRequiredIdentification(T4RawInput input, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(input.EmployerName))
        {
            errors.Add(new ValidationError(nameof(T4RawInput.EmployerName), "Employer name is required."));
        }

        if (string.IsNullOrWhiteSpace(input.EmployerAddress))
        {
            errors.Add(new ValidationError(nameof(T4RawInput.EmployerAddress), "Employer address is required."));
        }

        if (string.IsNullOrWhiteSpace(input.EmployeeName))
        {
            errors.Add(new ValidationError(nameof(T4RawInput.EmployeeName), "Employee name is required."));
        }

        if (string.IsNullOrWhiteSpace(input.EmployeeAddress))
        {
            errors.Add(new ValidationError(nameof(T4RawInput.EmployeeAddress), "Employee address is required."));
        }
    }

    // A1 required-present boxes (grounded in the RC4120 identification area + Box 45).
    // Message for Box 12 never echoes the SIN value.
    private static void ValidateRequiredBoxes(T4RawInput input, List<ValidationError> errors)
    {
        if (!HasValue(input, T4BoxCodes.SocialInsuranceNumber))
        {
            errors.Add(new ValidationError("Box 12", "Box 12 (SIN) is required."));
        }

        if (!HasValue(input, T4BoxCodes.EmployerAccountNumber))
        {
            errors.Add(new ValidationError("Box 54", "Box 54 (employer account number) is required."));
        }

        if (!HasValue(input, T4BoxCodes.DentalBenefits))
        {
            errors.Add(new ValidationError("Box 45", "Box 45 (employer-offered dental benefits) is required."));
        }
    }

    private static void ValidateAmounts(T4RawInput input, List<ValidationError> errors)
    {
        foreach (var box in input.Boxes)
        {
            // Scalar boxes are not monetary; blank values mean "absent", not garbled.
            if (ScalarBoxCodes.Contains(box.Code) || string.IsNullOrWhiteSpace(box.Value))
            {
                continue;
            }

            if (!decimal.TryParse(box.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                errors.Add(new ValidationError($"Box {box.Code}", $"Box {box.Code} must be a numeric amount."));
            }
            else if (amount < 0)
            {
                errors.Add(new ValidationError($"Box {box.Code}", $"Box {box.Code} must not be negative."));
            }
        }
    }

    private static void ValidateConstrainedValues(T4RawInput input, List<ValidationError> errors)
    {
        var province = FirstValue(input, T4BoxCodes.ProvinceOfEmployment);
        if (!string.IsNullOrWhiteSpace(province) && !ProvinceCodes.Contains(province))
        {
            errors.Add(new ValidationError(
                "Box 10",
                "Box 10 (province of employment) must be one of AB, BC, MB, NB, NL, NT, NS, NU, ON, PE, QC, SK, YT, US, ZZ."));
        }

        var employmentCode = FirstValue(input, T4BoxCodes.EmploymentCode);
        if (!string.IsNullOrWhiteSpace(employmentCode) && !EmploymentCodes.Contains(employmentCode))
        {
            errors.Add(new ValidationError(
                "Box 29",
                "Box 29 (employment code) must be blank or one of 11, 12, 13, 14, 15, 16, 17."));
        }

        var dental = FirstValue(input, T4BoxCodes.DentalBenefits);
        if (!string.IsNullOrWhiteSpace(dental) && !DentalBenefitCodes.Contains(dental))
        {
            errors.Add(new ValidationError(
                "Box 45",
                "Box 45 (employer-offered dental benefits) must be one of 1, 2, 3, 4, 5."));
        }
    }

    // Verified conditional companion boxes: if the trigger box has a value, the
    // companion must be present (its value may be 0).
    private static void ValidateConditionalCompanions(T4RawInput input, List<ValidationError> errors)
    {
        RequireCompanion(input, errors, T4BoxCodes.CppContributions, T4BoxCodes.CppQppPensionableEarnings);
        RequireCompanion(input, errors, T4BoxCodes.EiPremiums, T4BoxCodes.EiInsurableEarnings);
        RequireCompanion(input, errors, T4BoxCodes.PpipPremiums, T4BoxCodes.PpipInsurableEarnings);
        RequireCompanion(input, errors, T4BoxCodes.PensionAdjustment, T4BoxCodes.RppDpspRegistrationNumber);
    }

    private static void RequireCompanion(T4RawInput input, List<ValidationError> errors, string trigger, string companion)
    {
        if (HasValue(input, trigger) && !HasValue(input, companion))
        {
            errors.Add(new ValidationError(
                $"Box {companion}",
                $"Box {companion} is required when Box {trigger} is present."));
        }
    }

    // Builds the slip from already-validated input (happy-path mapping).
    private static T4Slip BuildSlip(T4RawInput input)
    {
        var scalars = new Dictionary<string, string?>(StringComparer.Ordinal);
        var other = new List<T4OtherInformation>();

        foreach (var box in input.Boxes)
        {
            if (AmountBoxCodes.Contains(box.Code) || ScalarBoxCodes.Contains(box.Code))
            {
                scalars[box.Code] = box.Value;
            }
            else
            {
                other.Add(new T4OtherInformation(box.Code, ParseAmount(box.Value)));
            }
        }

        var sin = Scalar(scalars, T4BoxCodes.SocialInsuranceNumber);

        return new T4Slip
        {
            Year = input.Year ?? 0,
            EmployerName = input.EmployerName ?? string.Empty,
            EmployerAddress = input.EmployerAddress,
            EmployeeName = input.EmployeeName ?? string.Empty,
            EmployeeAddress = input.EmployeeAddress,
            Sin = string.IsNullOrWhiteSpace(sin) ? null : new RecipientIdentifier(sin),
            EmployerAccountNumber = Scalar(scalars, T4BoxCodes.EmployerAccountNumber),
            RppDpspRegistrationNumber = Scalar(scalars, T4BoxCodes.RppDpspRegistrationNumber),

            ProvinceOfEmployment = Scalar(scalars, T4BoxCodes.ProvinceOfEmployment),
            EmploymentCode = Scalar(scalars, T4BoxCodes.EmploymentCode),
            DentalBenefitCode = Scalar(scalars, T4BoxCodes.DentalBenefits) ?? string.Empty,

            ExemptCppQpp = input.ExemptCppQpp ?? false,
            ExemptEi = input.ExemptEi ?? false,
            ExemptPpip = input.ExemptPpip ?? false,

            EmploymentIncome = Amount(scalars, T4BoxCodes.EmploymentIncome),
            CppContributions = Amount(scalars, T4BoxCodes.CppContributions),
            SecondCppContributions = Amount(scalars, T4BoxCodes.SecondCppContributions),
            QppContributions = Amount(scalars, T4BoxCodes.QppContributions),
            SecondQppContributions = Amount(scalars, T4BoxCodes.SecondQppContributions),
            EiPremiums = Amount(scalars, T4BoxCodes.EiPremiums),
            RppContributions = Amount(scalars, T4BoxCodes.RppContributions),
            IncomeTaxDeducted = Amount(scalars, T4BoxCodes.IncomeTaxDeducted),
            EiInsurableEarnings = Amount(scalars, T4BoxCodes.EiInsurableEarnings),
            CppQppPensionableEarnings = Amount(scalars, T4BoxCodes.CppQppPensionableEarnings),
            UnionDues = Amount(scalars, T4BoxCodes.UnionDues),
            CharitableDonations = Amount(scalars, T4BoxCodes.CharitableDonations),
            PensionAdjustment = Amount(scalars, T4BoxCodes.PensionAdjustment),
            PpipPremiums = Amount(scalars, T4BoxCodes.PpipPremiums),
            PpipInsurableEarnings = Amount(scalars, T4BoxCodes.PpipInsurableEarnings),

            OtherInformation = other,
        };
    }

    private static bool HasValue(T4RawInput input, string code) =>
        input.Boxes.Any(b => string.Equals(b.Code, code, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(b.Value));

    private static string? FirstValue(T4RawInput input, string code) =>
        input.Boxes.FirstOrDefault(b => string.Equals(b.Code, code, StringComparison.Ordinal))?.Value;

    private static string? Scalar(IReadOnlyDictionary<string, string?> boxes, string code) =>
        boxes.TryGetValue(code, out var value) ? value : null;

    private static decimal Amount(IReadOnlyDictionary<string, string?> boxes, string code) =>
        boxes.TryGetValue(code, out var value) ? ParseAmount(value) : 0m;

    private static decimal ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? MoneyRounding.ToCents(d)
            : 0m;
}
