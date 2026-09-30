using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExtractionWorkflow.Models;

namespace DocumentIntelligence.Production;

/// <summary>
/// Syntactic recognition of the printed T4 slip face. Every string below is the
/// bilingual wording printed on the CRA T4 (25) slip (t4-25e/t4-25f PDF and
/// e-text on canada.ca; sources recorded in spec 016 evidence/T05.md). It
/// identifies which printed field a value sits in; it carries no tax meaning,
/// no CRA line and no plausibility rule. Medical receipts have no CRA-printed
/// title or layout, so nothing here recognizes one.
/// </summary>
internal static class DocumentLayoutRecognition
{
    // Structural upper bound on pages in one result; a larger count is treated as malformed.
    private const int MaximumPages = 2_000;

    private const string EnglishTitle = "Statement of Remuneration Paid";
    private const string FrenchTitle = "État de la rémunération payée";
    private const string EnglishSummaryTitle = "Summary of Remuneration Paid";
    private const string FrenchSummaryTitle = "Sommaire de la rémunération payée";

    /// <summary>The printed box number and its English and French slip-face labels.</summary>
    internal static readonly IReadOnlyList<(string Code, string[] Labels)> BoxLabels =
    [
        ("10", ["Province of employment", "Province d'emploi"]),
        ("12", ["Social insurance number", "Numéro d'assurance sociale"]),
        ("14", ["Employment income", "Revenus d'emploi"]),
        ("16", ["Employee's CPP contributions – see over", "Cotisations de l'employé au RPC – voir au verso"]),
        ("16A", ["Employee's second CPP contributions – see over", "Deuxièmes cotisations de l'employé au RPC – voir au verso"]),
        ("17", ["Employee's QPP contributions – see over", "Cotisations de l'employé au RRQ – voir au verso"]),
        ("17A", ["Employee's second QPP contributions – see over", "Deuxièmes cotisations de l'employé au RRQ – voir au verso"]),
        ("18", ["Employee's EI premiums", "Cotisations de l'employé à l'AE"]),
        ("20", ["RPP contributions", "Cotisations à un RPA"]),
        ("22", ["Income tax deducted", "Impôt sur le revenu retenu"]),
        ("24", ["EI insurable earnings", "Gains assurables d'AE"]),
        ("26", ["CPP/QPP pensionable earnings", "Gains ouvrant droit à pension – RPC/RRQ"]),
        ("29", ["Employment code", "Code d'emploi"]),
        ("44", ["Union dues", "Cotisations syndicales"]),
        ("45", ["Employer-offered dental benefits", "Prestations dentaires offertes par l'employeur"]),
        ("46", ["Charitable donations", "Dons de bienfaisance"]),
        // The PDF face abbreviates "Numéro" to "N°"; the e-text spells it out.
        ("50", ["RPP or DPSP registration number", "Numéro d'agrément d'un RPA ou d'un RPDB", "N° d'agrément d'un RPA ou d'un RPDB"]),
        ("52", ["Pension adjustment", "Facteur d'équivalence"]),
        ("54", ["Employer's account number", "Numéro de compte de l'employeur"]),
        ("55", ["Employee's PPIP premiums – see over", "Cotisations de l'employé au RPAP – voir au verso"]),
        ("56", ["PPIP insurable earnings", "Gains assurables du RPAP"]),
    ];

    /// <summary>Printed non-box fields, each as English/French slip-face labels.</summary>
    internal enum Field { Year, EmployerName, EmployeeNameAndAddress, LastName, FirstName, Initial,
        Exempt, ExemptCppQpp, ExemptEi, ExemptPpip, OtherInformation, OtherBox, OtherAmount }

    private static readonly IReadOnlyList<(Field Field, string English, string French)> FieldLabels =
    [
        (Field.Year, "Year", "Année"),
        (Field.EmployerName, "Employer's name", "Nom de l'employeur"),
        (Field.EmployeeNameAndAddress, "Employee's name and address", "Nom et adresse de l'employé"),
        (Field.LastName, "Last name (in capital letters)", "Nom de famille (en lettres moulées)"),
        (Field.FirstName, "First name", "Prénom"),
        (Field.Initial, "Initial", "Initiale"),
        (Field.Exempt, "Exempt", "Exemption"),
        (Field.ExemptCppQpp, "CPP/QPP", "RPC/RRQ"),
        (Field.ExemptEi, "EI", "AE"),
        (Field.ExemptPpip, "PPIP", "RPAP"),
        (Field.OtherInformation, "Other information (see over)", "Autres renseignements (voir au verso)"),
        (Field.OtherBox, "Box", "Case"),
        (Field.OtherAmount, "Amount", "Montant"),
    ];

    private static readonly Dictionary<string, string> BoxByLabel = BuildBoxLabels();
    private static readonly Dictionary<string, Field> FieldByLabel = BuildFieldLabels();
    private static readonly HashSet<string> FaceCodes =
        BoxLabels.Select(entry => Canonical(entry.Code)).ToHashSet(StringComparer.Ordinal);

    private static readonly Regex Explicit = new(@"^(?:box|case) (\d{2}a?)(?: (.+))?$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex LeadingCode = new(@"^(\d{2}a?)(?: (.+))?$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex TrailingCode = new(@"^(.+) (\d{2}a?)$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Designator = new(@"(?:^| )t4(?: |$)",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// A T4 needs the printed "T4" designator, exactly one English and/or one
    /// French slip title, no T4 Summary title, and at least one printed slip-face
    /// box field. A repeated title means several slips; the result is never
    /// narrowed to one of them. Everything else is unsupported.
    /// </summary>
    internal static DocumentClassification Recognize(JsonElement layout, int recognizedBoxFields)
    {
        if (!Structural(layout, out var pageCount)) return DocumentClassification.Unsupported;
        if (!layout.TryGetProperty("paragraphs", out var paragraphs) || paragraphs.ValueKind != JsonValueKind.Array)
            return DocumentClassification.Unsupported;

        string english = Canonical(EnglishTitle), french = Canonical(FrenchTitle);
        string englishSummary = Canonical(EnglishSummaryTitle), frenchSummary = Canonical(FrenchSummaryTitle);
        int englishTitles = 0, frenchTitles = 0;
        var designated = false;
        foreach (var paragraph in paragraphs.EnumerateArray())
        {
            var text = Canonical(paragraph.GetProperty("content").GetString() ?? throw new FormatException());
            if (text.Contains(englishSummary, StringComparison.Ordinal) ||
                text.Contains(frenchSummary, StringComparison.Ordinal)) return DocumentClassification.Unsupported;
            var titles = Occurrences(text, english) + Occurrences(text, french);
            var designator = Designator.IsMatch(text);
            if (titles == 0 && !designator) continue;
            RequireGeometry(paragraph, pageCount);
            englishTitles += Occurrences(text, english);
            frenchTitles += Occurrences(text, french);
            designated |= designator;
        }

        return designated && englishTitles <= 1 && frenchTitles <= 1 &&
               englishTitles + frenchTitles >= 1 && recognizedBoxFields >= 1
            ? DocumentClassification.T4Slip
            : DocumentClassification.Unsupported;
    }

    internal static bool Structural(JsonElement layout, out int pageCount)
    {
        pageCount = 0;
        if (layout.GetProperty("apiVersion").GetString() != AzureDocumentIntelligenceOptions.ApprovedApiVersion ||
            layout.GetProperty("modelId").GetString() != AzureDocumentIntelligenceOptions.ApprovedModel ||
            !layout.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array ||
            pages.GetArrayLength() is < 1 or > MaximumPages ||
            (layout.TryGetProperty("warnings", out var warnings) && warnings.GetArrayLength() != 0))
            return false;
        foreach (var page in pages.EnumerateArray())
            if (page.GetProperty("pageNumber").GetInt32() != ++pageCount) return false;
        return true;
    }

    /// <summary>
    /// The printed box whose slip-face label and/or printed box number the key
    /// shows. A number beside a known label must be that label's number, and a
    /// key explicitly naming a "Box"/"Case" must name a number printed on the
    /// slip face; either contradiction throws, so the layout refuses. Keys that
    /// are neither a label nor an explicit box are not box fields.
    /// </summary>
    internal static string? BoxCode(string key)
    {
        var text = Canonical(key);
        if (BoxByLabel.TryGetValue(text, out var labelled)) return labelled;

        if (Explicit.Match(text) is { Success: true } named)
        {
            var code = named.Groups[1].Value;
            if (!FaceCodes.Contains(code)) throw new FormatException();
            if (named.Groups[2].Success && !(BoxByLabel.TryGetValue(named.Groups[2].Value, out var box) && Canonical(box) == code))
                throw new FormatException();
            return Printed(code);
        }

        string? number = null, remainder = null;
        if (LeadingCode.Match(text) is { Success: true } leading)
            (number, remainder) = (leading.Groups[1].Value, leading.Groups[2].Success ? leading.Groups[2].Value : null);
        else if (TrailingCode.Match(text) is { Success: true } trailing)
            (number, remainder) = (trailing.Groups[2].Value, trailing.Groups[1].Value);
        if (number is null) return null;
        if (remainder is null) return FaceCodes.Contains(number) ? Printed(number) : null;
        if (!BoxByLabel.TryGetValue(remainder, out var labelledCode)) return null;
        return Canonical(labelledCode) == number ? labelledCode : throw new FormatException();
    }

    private static string Printed(string canonicalCode) =>
        BoxLabels.Single(entry => Canonical(entry.Code) == canonicalCode).Code;

    internal static Field? NamedField(string key) =>
        FieldByLabel.TryGetValue(Canonical(key), out var field) ? field : null;

    internal static int RequireGeometry(JsonElement field, int pages)
    {
        var regions = field.GetProperty("boundingRegions");
        if (regions.GetArrayLength() != 1) throw new FormatException();
        var region = regions[0];
        var page = region.GetProperty("pageNumber").GetInt32();
        var polygon = region.GetProperty("polygon");
        if (page < 1 || page > pages || polygon.GetArrayLength() != 8) throw new FormatException();
        foreach (var coordinate in polygon.EnumerateArray())
            if (!coordinate.TryGetDecimal(out var value) || value < 0) throw new FormatException();
        return page;
    }

    /// <summary>
    /// Presentation-only normalization: Unicode compatibility form, letter
    /// case, apostrophe and dash variants, the slash, colon and parenthesis
    /// punctuation of the printed labels, and whitespace. Words are never
    /// changed, dropped or substituted.
    /// </summary>
    internal static string Canonical(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormKC))
        {
            builder.Append(character switch
            {
                '‘' or '’' or 'ʼ' or '`' => '\'',
                '-' or '‐' or '‑' or '‒' or '–' or '—' or '―' or '−' => ' ',
                '/' or ':' or '(' or ')' => ' ',
                _ when char.IsWhiteSpace(character) => ' ',
                _ => char.ToLowerInvariant(character),
            });
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int Occurrences(string text, string phrase)
    {
        var count = 0;
        for (var index = text.IndexOf(phrase, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(phrase, index + phrase.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    // The printed face shows each label in English above French; OCR may read
    // either language alone or both in either order.
    private static IEnumerable<string> Presentations(string english, string french)
    {
        string first = Canonical(english), second = Canonical(french);
        return [first, second, first + " " + second, second + " " + first];
    }

    private static Dictionary<string, string> BuildBoxLabels()
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (code, printed) in BoxLabels)
            foreach (var french in printed.Skip(1))
                foreach (var presentation in Presentations(printed[0], french))
                    labels[presentation] = code;
        return labels;
    }

    private static Dictionary<string, Field> BuildFieldLabels()
    {
        var labels = new Dictionary<string, Field>(StringComparer.Ordinal);
        foreach (var (field, english, french) in FieldLabels)
            foreach (var presentation in Presentations(english, french))
                labels[presentation] = field;
        return labels;
    }
}
