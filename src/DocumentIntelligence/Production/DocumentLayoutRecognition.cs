using System.Text.Json;
using ExtractionWorkflow.Models;

namespace DocumentIntelligence.Production;

internal static class DocumentLayoutRecognition
{
    // Structural upper bound on pages in one result; a larger count is treated as malformed.
    private const int MaximumPages = 2_000;

    internal static DocumentClassification Recognize(JsonElement layout)
    {
        if (layout.GetProperty("apiVersion").GetString() != AzureDocumentIntelligenceOptions.ApprovedApiVersion ||
            layout.GetProperty("modelId").GetString() != AzureDocumentIntelligenceOptions.ApprovedModel ||
            !layout.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array ||
            pages.GetArrayLength() is < 1 or > MaximumPages ||
            (layout.TryGetProperty("warnings", out var warnings) && warnings.GetArrayLength() != 0))
            return DocumentClassification.Unsupported;
        var pageNumber = 0;
        foreach (var page in pages.EnumerateArray())
            if (page.GetProperty("pageNumber").GetInt32() != ++pageNumber) return DocumentClassification.Unsupported;

        if (!layout.TryGetProperty("paragraphs", out var paragraphs)) return DocumentClassification.Unsupported;
        var recognized = new List<DocumentClassification>();
        foreach (var paragraph in paragraphs.EnumerateArray())
        {
            if (!paragraph.TryGetProperty("role", out var role) || role.GetString() != "title") continue;
            var text = paragraph.GetProperty("content").GetString();
            var kind = text switch
            {
                "T4" or "T4 Statement of Remuneration Paid" => DocumentClassification.T4Slip,
                "Medical expense receipt" => DocumentClassification.MedicalExpenseEvidence,
                _ => DocumentClassification.Unsupported,
            };
            if (kind != DocumentClassification.Unsupported)
            {
                RequireGeometry(paragraph, pages.GetArrayLength());
                recognized.Add(kind);
            }
        }
        // Several supported titles can represent several documents. Never pick one.
        return recognized.Count == 1 ? recognized[0] : DocumentClassification.Unsupported;
    }

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
}
