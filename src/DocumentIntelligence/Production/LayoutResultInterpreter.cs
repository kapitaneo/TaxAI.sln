using System.Globalization;
using System.Text.Json;
using ExtractionWorkflow.Models;

namespace DocumentIntelligence.Production;

internal static class LayoutResultInterpreter
{
    internal static ProviderClassificationResult Classify(JsonElement layout)
    {
        RequireUniqueProperties(layout);
        return new ProviderClassificationResult
        {
            Classification = DocumentLayoutRecognition.Recognize(layout),
            ProviderLabel = "Azure Document Intelligence layout",
            // Layout has no document-classification confidence. Word or field
            // confidence is not a matching signal and must not be substituted.
            Confidence = null,
        };
    }

    internal static ProviderT4Result? T4(JsonElement layout)
    {
        if (Classify(layout).Classification != DocumentClassification.T4Slip) return null;
        var fields = Fields(layout);
        var boxes = new List<ProviderBoxValue?>();
        foreach (var field in fields)
        {
            var code = field.Key.StartsWith("Box ", StringComparison.Ordinal) ? field.Key[4..] : field.Key;
            if (code.Length is 2 or 3 && code.All(character => character is >= '0' and <= '9'))
                boxes.Add(new ProviderBoxValue { Code = code, Value = field.Value, Confidence = field.Confidence });
        }
        var printedYear = Single(fields, "Year");
        int? year = null;
        if (printedYear is not null)
        {
            if (!int.TryParse(printedYear, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) return null;
            year = parsed;
        }
        return new ProviderT4Result
        {
            Year = year,
            EmployerName = Single(fields, "Employer's name"),
            EmployerAddress = Single(fields, "Employer's address"),
            EmployeeName = Single(fields, "Employee's name"),
            EmployeeAddress = Single(fields, "Employee's address"),
            ExemptCppQpp = Selection(Single(fields, "CPP/QPP exempt")),
            ExemptEi = Selection(Single(fields, "EI exempt")),
            ExemptPpip = Selection(Single(fields, "PPIP exempt")),
            Boxes = boxes.AsReadOnly(),
            Confidence = null,
        };
    }

    internal static ProviderMedicalResult? Medical(JsonElement layout)
    {
        if (Classify(layout).Classification != DocumentClassification.MedicalExpenseEvidence) return null;
        var fields = Fields(layout);
        return new ProviderMedicalResult
        {
            ServiceDateAsPrinted = Single(fields, "Service date"),
            GrossAmountAsPrinted = Single(fields, "Gross amount"),
            CurrencyCodeAsPrinted = Single(fields, "Currency"),
            ProviderDisplayName = Single(fields, "Provider name"),
            PatientDisplayName = Single(fields, "Patient name"),
            ReceiptReference = Single(fields, "Receipt reference"),
            Confidence = null,
        };
    }

    private static List<PrintedField> Fields(JsonElement layout)
    {
        var result = new List<PrintedField>();
        var pages = layout.GetProperty("pages").GetArrayLength();
        foreach (var pair in layout.GetProperty("keyValuePairs").EnumerateArray())
        {
            var key = pair.GetProperty("key");
            var label = key.GetProperty("content").GetString() ?? throw new FormatException();
            var page = DocumentLayoutRecognition.RequireGeometry(key, pages);
            string? value = null;
            if (pair.TryGetProperty("value", out var printed))
            {
                if (DocumentLayoutRecognition.RequireGeometry(printed, pages) != page) throw new FormatException();
                value = printed.GetProperty("content").GetString() ?? throw new FormatException();
            }
            decimal? confidence = pair.TryGetProperty("confidence", out var signal) && signal.ValueKind != JsonValueKind.Null
                ? signal.GetDecimal() : null;
            result.Add(new PrintedField(label, value, confidence));
        }
        return result;
    }

    private static string? Single(List<PrintedField> fields, string label)
    {
        var matches = fields.Where(field => field.Key == label).ToArray();
        if (matches.Length > 1) throw new FormatException();
        return matches.SingleOrDefault()?.Value;
    }

    private static bool? Selection(string? printed) => printed switch
    {
        null => null,
        ":selected:" => true,
        ":unselected:" => false,
        _ => throw new FormatException(),
    };

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new FormatException();
                RequireUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RequireUniqueProperties(item);
    }

    private sealed class PrintedField(string key, string? value, decimal? confidence)
    {
        internal string Key { get; } = key;
        internal string? Value { get; } = value;
        internal decimal? Confidence { get; } = confidence;
        public override string ToString() => nameof(PrintedField);
    }
}
