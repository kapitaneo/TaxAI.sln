using System.Globalization;
using System.Text.RegularExpressions;
using TaxLogicEngine.Models;
using Tenancy.Models;
using Tenancy.Services;

namespace Tenancy.Tests.Services;

public sealed class MonetaryValueGuardTests
{
    public static TheoryData<decimal> ExactlyStorableAmounts =>
        new()
        {
            0m,
            1.005m,
            -1.005m,
            0.0001m,
            1.2345m,
            12345678_1234567.1234m,
            -12345678_1234567.1234m,
        };

    public static TheoryData<decimal> AmountsRequiringAlteration =>
        new()
        {
            0.00001m,
            -0.00001m,
            1.23000m,
            12345678_12345678m,
            12345678_1234567.12345m,
            decimal.MaxValue,
            decimal.MinValue,
        };

    [Theory]
    [MemberData(nameof(ExactlyStorableAmounts))]
    public void Validate_AcceptsAmountsThatFitTheDeclaredPrecisionAndScale(decimal amount)
    {
        var error = MonetaryValueGuard.Validate(amount, TenancyFields.Amount);

        Assert.Null(error);
    }

    [Theory]
    [MemberData(nameof(AmountsRequiringAlteration))]
    public void Validate_RefusesAmountsThatWouldRequireAlteration(decimal amount)
    {
        var error = MonetaryValueGuard.Validate(amount, TenancyFields.Amount);

        Assert.Equal(
            new ValidationError(TenancyFields.Amount, TenancyErrors.AmountNotExactlyStorable),
            error);
    }

    [Fact]
    public void Validate_UsesInvariantCultureForTheInspectedRepresentation()
    {
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var error = MonetaryValueGuard.Validate(0.00001m, TenancyFields.Amount);

            Assert.Equal(TenancyErrors.AmountNotExactlyStorable, error?.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Implementation_InspectsTextWithoutMonetaryArithmeticOrAdjustment()
    {
        var source = File.ReadAllText(FindImplementationFile());
        var executable = Regex.Replace(
            source,
            @"//.*?$|/\*.*?\*/",
            string.Empty,
            RegexOptions.Multiline | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));

        Assert.Contains("CultureInfo.InvariantCulture", executable, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.", executable, StringComparison.Ordinal);
        Assert.DoesNotContain("Round", executable, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Truncate", executable, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("decimal.", executable, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(
                @"(?:\bamount\s*(?:\+\+|--|[+\-*/%]=?|=)|(?:\+\+|--|[+\-*/%])\s*amount\b)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            executable);
    }

    private static string FindImplementationFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Tenancy",
                "Services",
                "MonetaryValueGuard.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the monetary guard source file.");
    }
}
