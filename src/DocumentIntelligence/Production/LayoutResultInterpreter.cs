using System.Globalization;
using System.Text.Json;
using ExtractionWorkflow.Models;
using Field = DocumentIntelligence.Production.DocumentLayoutRecognition.Field;

namespace DocumentIntelligence.Production;

/// <summary>
/// Copies printed T4 slip-face fields into the existing untrusted shapes. Values
/// are copied exactly as read; nothing is repaired, normalized, inferred or
/// judged. Medical interpretation is not implemented: no CRA-printed medical
/// title or layout exists to recognize, so it stays refused (spec 016 T05
/// pending Class A item).
/// </summary>
internal static class LayoutResultInterpreter
{
    internal const string ProviderLabel = "Document Intelligence layout";

    internal static ProviderClassificationResult Classify(JsonElement layout)
    {
        RequireUniqueProperties(layout);
        var classification = DocumentLayoutRecognition.Structural(layout, out _)
            ? DocumentLayoutRecognition.Recognize(layout, Read(layout).Boxes.Count)
            : DocumentClassification.Unsupported;
        return new ProviderClassificationResult
        {
            Classification = classification,
            ProviderLabel = ProviderLabel,
            // Layout has no document-classification confidence. Word or field
            // confidence is not a matching signal and must not be substituted.
            Confidence = null,
        };
    }

    internal static ProviderT4Result? T4(JsonElement layout)
    {
        if (Classify(layout).Classification != DocumentClassification.T4Slip) return null;
        var printed = Read(layout);

        int? year = null;
        if (printed.Single(Field.Year) is { } printedYear)
        {
            if (!int.TryParse(printedYear, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) return null;
            year = parsed;
        }

        return new ProviderT4Result
        {
            Year = year,
            EmployerName = printed.Single(Field.EmployerName),
            // The slip face prints no separate employer address, and the
            // employee's name-and-address block is not split into its parts.
            EmployerAddress = null,
            EmployeeName = printed.EmployeeName(),
            EmployeeAddress = null,
            ExemptCppQpp = Selection(printed.Single(Field.ExemptCppQpp)),
            ExemptEi = Selection(printed.Single(Field.ExemptEi)),
            ExemptPpip = Selection(printed.Single(Field.ExemptPpip)),
            Boxes = printed.Boxes.AsReadOnly(),
            Confidence = null,
        };
    }

    private static PrintedSlip Read(JsonElement layout)
    {
        var slip = new PrintedSlip();
        if (!layout.TryGetProperty("keyValuePairs", out var pairs)) return slip;
        var pages = layout.GetProperty("pages").GetArrayLength();
        foreach (var pair in pairs.EnumerateArray())
        {
            var key = pair.GetProperty("key");
            var label = key.GetProperty("content").GetString() ?? throw new FormatException();
            var page = DocumentLayoutRecognition.RequireGeometry(key, pages);
            string? value = null;
            if (pair.TryGetProperty("value", out var printed) && printed.ValueKind != JsonValueKind.Null)
            {
                if (DocumentLayoutRecognition.RequireGeometry(printed, pages) != page) throw new FormatException();
                value = printed.GetProperty("content").GetString() ?? throw new FormatException();
            }
            decimal? confidence = pair.TryGetProperty("confidence", out var signal) && signal.ValueKind != JsonValueKind.Null
                ? signal.GetDecimal() : null;

            if (DocumentLayoutRecognition.NamedField(label) is { } field)
            {
                // The other-information cells carry employer-entered codes and
                // amounts whose pairing is positional; an occupied cell cannot
                // be associated without guessing, so the layout refuses.
                if (field is Field.OtherInformation or Field.OtherBox or Field.OtherAmount &&
                    !string.IsNullOrWhiteSpace(value)) throw new FormatException();
                slip.Add(field, value);
            }
            else if (DocumentLayoutRecognition.BoxCode(label) is { } code && !string.IsNullOrWhiteSpace(value))
            {
                // Duplicates stay a list, so the engine's own duplicate-box rule sees them.
                slip.Boxes.Add(new ProviderBoxValue { Code = code, Value = value, Confidence = confidence });
            }
        }
        return slip;
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

    private sealed class PrintedSlip
    {
        private readonly List<(Field Field, string? Value)> _fields = [];
        internal List<ProviderBoxValue?> Boxes { get; } = [];

        internal void Add(Field field, string? value) => _fields.Add((field, value));

        /// <summary>One printed value per field; a repeated field is ambiguous and refuses.</summary>
        internal string? Single(Field field)
        {
            var matches = _fields.Where(entry => entry.Field == field && entry.Value is not null).ToArray();
            if (matches.Length > 1) throw new FormatException();
            return matches.SingleOrDefault().Value;
        }

        /// <summary>
        /// The printed last name, first name and initial, in slip order, each
        /// copied as read and separated by one space [ASSUMED]. Without a
        /// printed last name there is no employee name; the existing chain
        /// then refuses and the accountant correction path applies.
        /// </summary>
        internal string? EmployeeName()
        {
            string? last = Single(Field.LastName), first = Single(Field.FirstName), initial = Single(Field.Initial);
            if (last is null) return null;
            return string.Join(' ', new[] { last, first, initial }.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        public override string ToString() => nameof(PrintedSlip);
    }
}
