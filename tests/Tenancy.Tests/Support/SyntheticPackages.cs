using System.Reflection;
using CaseReview.Models;
using CaseReview.Services;
using TaxLogicEngine.Models;
using Tenancy.Contracts;

namespace Tenancy.Tests.Support;

internal static class SyntheticPackages
{
    internal static readonly Guid FirmIdentifier =
        Guid.Parse("12ab34cd-56ef-78ab-90cd-ef12ab34cd56");

    internal static readonly Guid OtherFirmIdentifier =
        Guid.Parse("ab12cd34-ef56-78ab-90cd-12ef34ab56cd");

    internal const string CaseIdentifier = "case-alpha";
    internal const string ParticipantIdentifier = "participant-alpha";
    internal const string TaxpayerDisplayName = "Test Taxpayer 001";
    internal const string EmployerDisplayName = "Example Employer A";

    internal static int SupportedTaxYear => SupportedReviewTaxYears.All.Single();

    internal static AccountantReviewPackage Package(
        Guid? firmIdentifier = null,
        string? firmText = null,
        string caseIdentifier = CaseIdentifier,
        string participantIdentifier = ParticipantIdentifier,
        int? taxYear = null,
        IReadOnlyList<ReviewedSlip>? reviewedSlips = null)
    {
        var year = taxYear ?? SupportedTaxYear;
        var slips = reviewedSlips ?? new[]
        {
            Slip(taxYear: year),
        };

        return InvokeNonPublic<AccountantReviewPackage>(
            firmText ?? (firmIdentifier ?? FirmIdentifier).ToString("D"),
            caseIdentifier,
            participantIdentifier,
            year,
            slips,
            null);
    }

    internal static ReviewedSlip Slip(
        int? taxYear = null,
        string taxpayerDisplayName = TaxpayerDisplayName,
        string employerDisplayName = EmployerDisplayName,
        IReadOnlyList<TaxLineEntry>? lineEntries = null,
        IReadOnlyList<ReviewedBoxAmount>? boxAmounts = null,
        IReadOnlyList<string>? unmappedBoxCodes = null,
        string documentMarker = "caller-document-marker",
        string sourceMarker = "caller-source-marker")
    {
        var year = taxYear ?? SupportedTaxYear;
        var slip = new T4Slip
        {
            Year = year,
            EmployeeName = taxpayerDisplayName,
            EmployerName = employerDisplayName,
            DentalBenefitCode = "1",
        };

        return InvokeNonPublic<ReviewedSlip>(
            documentMarker,
            year,
            sourceMarker,
            slip,
            lineEntries ??
            [
                Line("line-beta", 2.3456m, ["source-beta", "source-alpha"]),
                Line("line-alpha", 0m, []),
            ],
            boxAmounts ??
            [
                Box("box-beta", 1.005m),
                Box("box-alpha", -1.005m),
            ],
            unmappedBoxCodes ?? new[] { "unmapped-beta", "unmapped-alpha" });
    }

    internal static ReviewedBoxAmount Box(string code, decimal amount) =>
        InvokeNonPublic<ReviewedBoxAmount>(code, amount);

    internal static TaxLineEntry Line(
        string code,
        decimal amount,
        IReadOnlyList<string>? sourceBoxCodes = null) =>
        new(code, amount, sourceBoxCodes ?? Array.Empty<string>());

    private static T InvokeNonPublic<T>(params object?[] arguments)
    {
        var constructor = typeof(T)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == arguments.Length);

        return (T)constructor.Invoke(arguments);
    }
}

internal sealed class SequenceIdentifierSource : IOpaqueIdentifierSource
{
    private readonly List<Guid> _issued = [];
    private readonly IReadOnlyList<Guid> _issuedView;
    private int _next = 1;

    internal SequenceIdentifierSource()
    {
        _issuedView = _issued.AsReadOnly();
    }

    internal int CallCount => _issued.Count;

    internal IReadOnlyList<Guid> Issued => _issuedView;

    public Guid NewIdentifier()
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(_next).CopyTo(bytes, 0);
        _next++;

        var identifier = new Guid(bytes);
        _issued.Add(identifier);
        return identifier;
    }
}
