using ExtractionWorkflow.Models;

namespace DocumentIntelligence.Tests.Production;

/// <summary>
/// Field association follows the printed CRA T4 (25) slip-face labels in
/// English and French (spec 016 evidence/T05.md). Values are copied as read.
/// </summary>
public sealed class LayoutResultInterpreterTests
{
    // Every box printed on the 2025 slip face, with its English and French label.
    public static TheoryData<string, string> SlipFaceLabels() => new()
    {
        { "Province of employment", "10" }, { "Province d'emploi", "10" },
        { "Social insurance number", "12" }, { "Numéro d'assurance sociale", "12" },
        { "Employment income", "14" }, { "Revenus d'emploi", "14" },
        { "Employee's CPP contributions – see over", "16" }, { "Cotisations de l'employé au RPC – voir au verso", "16" },
        { "Employee's second CPP contributions – see over", "16A" }, { "Deuxièmes cotisations de l'employé au RPC – voir au verso", "16A" },
        { "Employee's QPP contributions – see over", "17" }, { "Cotisations de l'employé au RRQ – voir au verso", "17" },
        { "Employee's second QPP contributions – see over", "17A" }, { "Deuxièmes cotisations de l'employé au RRQ – voir au verso", "17A" },
        { "Employee's EI premiums", "18" }, { "Cotisations de l'employé à l'AE", "18" },
        { "RPP contributions", "20" }, { "Cotisations à un RPA", "20" },
        { "Income tax deducted", "22" }, { "Impôt sur le revenu retenu", "22" },
        { "EI insurable earnings", "24" }, { "Gains assurables d'AE", "24" },
        { "CPP/QPP pensionable earnings", "26" }, { "Gains ouvrant droit à pension – RPC/RRQ", "26" },
        { "Employment code", "29" }, { "Code d'emploi", "29" },
        { "Union dues", "44" }, { "Cotisations syndicales", "44" },
        { "Employer-offered dental benefits", "45" }, { "Prestations dentaires offertes par l'employeur", "45" },
        { "Charitable donations", "46" }, { "Dons de bienfaisance", "46" },
        { "RPP or DPSP registration number", "50" }, { "Numéro d'agrément d'un RPA ou d'un RPDB", "50" },
        { "N° d'agrément d'un RPA ou d'un RPDB", "50" },
        { "Pension adjustment", "52" }, { "Facteur d'équivalence", "52" },
        { "Employer's account number", "54" }, { "Numéro de compte de l'employeur", "54" },
        { "Employee's PPIP premiums – see over", "55" }, { "Cotisations de l'employé au RPAP – voir au verso", "55" },
        { "PPIP insurable earnings", "56" }, { "Gains assurables du RPAP", "56" },
    };

    [Theory]
    [MemberData(nameof(SlipFaceLabels))]
    public async Task EverySupportedSlipFaceLabelIdentifiesItsPrintedBox(string label, string code)
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(ProviderWire.Pair(label, "as printed"));
        using var provider = wire.Provider();
        var result = Assert.IsType<ProviderT4Result>(await wire.Invoke(provider));
        var box = Assert.Single(result.Boxes!, candidate => candidate!.Value == "as printed");
        Assert.Equal(code, box!.Code);
    }

    [Theory]
    [InlineData("Employment income – Revenus d'emploi")]
    [InlineData("Revenus d'emploi Employment income")]
    [InlineData("14 Employment income")]
    [InlineData("Employment income 14")]
    [InlineData("Box 14: Employment income")]
    [InlineData("Case 14 : Revenus d'emploi")]
    [InlineData("Box 14")]
    [InlineData("Case 14")]
    [InlineData("14")]
    [InlineData("EMPLOYMENT  INCOME")]
    [InlineData("Employment income / Revenus d’emploi")]
    public async Task PresentationDifferencesKeepTheFieldIdentity(string label)
    {
        using var wire = new ProviderWire();
        wire.Pairs.Clear();
        wire.Pairs.Add(ProviderWire.Pair(label, "50000.00"));
        using var provider = wire.Provider();
        var box = Assert.Single(Assert.IsType<ProviderT4Result>(await wire.Invoke(provider)).Boxes!);
        Assert.Equal("14", box!.Code);
        Assert.Equal("50000.00", box.Value);
    }

    [Theory]
    [InlineData("Employment income 22")]
    [InlineData("Box 22: Employment income")]
    [InlineData("Box 40")]
    [InlineData("Case 99")]
    [InlineData("Box 14: Salary")]
    public async Task AContradictoryOrOffFaceBoxRefusesInsteadOfGuessing(string label)
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(ProviderWire.Pair(label, "1.00"));
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
        Assert.Equal("DELETE", wire.Methods.Last());
    }

    [Theory]
    [InlineData("Salary")]
    [InlineData("Gross pay")]
    [InlineData("Wages")]
    [InlineData("Protected B when completed")]
    public async Task UnprintedSynonymsAreNeverAliasedToABox(string label)
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(ProviderWire.Pair(label, "1.00"));
        using var provider = wire.Provider();
        var result = Assert.IsType<ProviderT4Result>(await wire.Invoke(provider));
        Assert.Equal("14", Assert.Single(result.Boxes!)!.Code);
    }

    [Fact]
    public async Task AnEmployeeCopyWithoutBoxFiftyFourIsExtractedWithoutIt()
    {
        using var wire = new ProviderWire();
        wire.Pairs.Clear();
        wire.Pairs.AddRange(
        [
            ProviderWire.Pair("Year – Année", "2025"),
            ProviderWire.Pair("Employer's name – Nom de l'employeur", "Synthetic Employer"),
            ProviderWire.Pair("Last name (in capital letters) – Nom de famille (en lettres moulées)", "EMPLOYEE"),
            ProviderWire.Pair("First name – Prénom", "Synthetic"),
            ProviderWire.Pair("Initial – Initiale", "Q"),
            ProviderWire.Pair("Social insurance number – Numéro d'assurance sociale", "synthetic placeholder"),
            ProviderWire.Pair("Employment income – Revenus d'emploi", "50000.00"),
            ProviderWire.Pair("Employer-offered dental benefits – Prestations dentaires offertes par l'employeur", "1"),
            ProviderWire.Pair("Employer's account number – Numéro de compte de l'employeur", null),
        ]);
        using var provider = wire.Provider();
        var result = Assert.IsType<ProviderT4Result>(await wire.Invoke(provider));
        Assert.Equal(new[] { "12", "14", "45" }, result.Boxes!.Select(box => box!.Code));
        Assert.Equal(2025, result.Year);
        Assert.Equal("Synthetic Employer", result.EmployerName);
        Assert.Equal("EMPLOYEE Synthetic Q", result.EmployeeName);
        Assert.Null(result.EmployerAddress);
        Assert.Null(result.EmployeeAddress);
    }

    [Fact]
    public async Task APrintedBoxFiftyFourIsCopiedWhenPresent()
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(ProviderWire.Pair("54 Employer's account number / Numéro de compte de l'employeur", "synthetic account"));
        using var provider = wire.Provider();
        var result = Assert.IsType<ProviderT4Result>(await wire.Invoke(provider));
        Assert.Equal("synthetic account", Assert.Single(result.Boxes!, box => box!.Code == "54")!.Value);
    }

    [Fact]
    public async Task CopiesPrintedValuesAndDuplicatesWithoutRepairOrThreshold()
    {
        using var wire = new ProviderWire();
        wire.Pairs.Clear();
        wire.Pairs.Add(ProviderWire.Pair("Year", "2025"));
        wire.Pairs.Add(ProviderWire.Pair("Employer's name", "  Synthetic employer  "));
        wire.Pairs.Add(ProviderWire.Pair("Employment income", "$1,234.567", 1.7m));
        wire.Pairs.Add(ProviderWire.Pair("Box 14", "-2,00", -0.2m));
        using var provider = wire.Provider();
        var result = Assert.IsType<ProviderT4Result>(await wire.Invoke(provider));
        Assert.Equal(2025, result.Year);
        Assert.Equal("  Synthetic employer  ", result.EmployerName);
        Assert.Equal(new[] { "14", "14" }, result.Boxes!.Select(box => box!.Code));
        Assert.Equal(new[] { "$1,234.567", "-2,00" }, result.Boxes!.Select(box => box!.Value));
        Assert.Equal(new decimal?[] { 1.7m, -0.2m }, result.Boxes!.Select(box => box!.Confidence));
        Assert.Null(result.Confidence);
        Assert.Null(result.EmployeeName);
    }

    [Theory]
    [InlineData("CPP/QPP", ":selected:", true)]
    [InlineData("RPC/RRQ", ":unselected:", false)]
    public async Task ExemptSelectionsAreCopiedFromTheirPrintedMarks(string label, string mark, bool expected)
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(ProviderWire.Pair(label, mark));
        using var provider = wire.Provider();
        Assert.Equal(expected, Assert.IsType<ProviderT4Result>(await wire.Invoke(provider)).ExemptCppQpp);
    }

    [Theory]
    [InlineData("Box – Case", "40")]
    [InlineData("Amount – Montant", "12.00")]
    [InlineData("Other information (see over)", "40 12.00")]
    public async Task AnOccupiedOtherInformationCellRefusesInsteadOfPairingByPosition(string label, string value)
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(ProviderWire.Pair(label, value));
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
    }

    [Fact]
    public async Task EmptyOtherInformationCellsAreNotFields()
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(ProviderWire.Pair("Box – Case", null));
        wire.Pairs.Add(ProviderWire.Pair("Amount – Montant", null));
        using var provider = wire.Provider();
        Assert.Single(Assert.IsType<ProviderT4Result>(await wire.Invoke(provider)).Boxes!);
    }

    [Theory]
    [InlineData("Year")]
    [InlineData("Employer's name")]
    [InlineData("First name – Prénom")]
    public async Task AmbiguousScalarFieldsRefuseInsteadOfPickingOne(string key)
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(ProviderWire.Pair(key, "2025"));
        wire.Pairs.Add(ProviderWire.Pair(key, "2025"));
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
        Assert.Equal("DELETE", wire.Methods.Last());
    }

    [Theory]
    [InlineData("20O5")]
    [InlineData(" 2025 ")]
    [InlineData("2025/2026")]
    public async Task UnrepresentablePrintedYearIsNeverRepaired(string year)
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(ProviderWire.Pair("Year", year));
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
    }

    [Fact]
    public async Task FieldAssociationAcrossPagesRefuses()
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(new
        {
            key = new { content = "Box 14", boundingRegions = ProviderWire.Geometry() },
            value = new { content = "5", boundingRegions = ProviderWire.Geometry(2) },
        });
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
    }
}
