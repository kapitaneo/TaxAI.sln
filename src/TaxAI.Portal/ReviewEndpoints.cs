using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.Portal;

/// <summary>
/// The portal's single read-only taxpayer-data surface.
/// <para>
/// It consumes only the persisted, SIN-free Tenancy model. The route selector
/// chooses one immutable stored version; it is never rendered, copied into the
/// DTO, or used as tenant identity. The trusted firm comes only from the
/// authorization state established from the durable server-side session.
/// </para>
/// </summary>
internal static class ReviewEndpoints
{
    internal const string RoutePattern = "/reviews/{recordId:guid}";

    private const string CacheControlValue = "no-store, no-cache, must-revalidate";

    private const string PragmaValue = "no-cache";

    private const string EmptyValue = "Not provided";

    private static readonly Regex NineDigitRun = new(
        @"[0-9]{9}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex MaskedNineDigitShape = new(
        @"\*{3}-\*{2}-[0-9]{3}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints
            .MapGet(RoutePattern, HandleAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy);
    }

    private static async Task<IResult> HandleAsync(
        Guid recordId,
        HttpContext context,
        IReviewRecordRepository repository)
    {
        SetPrivateResponseHeaders(context.Response);

        var authorization = PortalAuthorization.BootstrapStateFor(context);
        if (authorization?.TrustedFirmId is not Guid activeFirmId ||
            activeFirmId == Guid.Empty)
        {
            return Results.NotFound();
        }

        var record = await repository.FindAsync(
            TrustedFirmId.FromGuid(activeFirmId),
            recordId,
            context.RequestAborted);

        if (record is null || !IsBrowserSafe(record))
        {
            return Results.NotFound();
        }

        var page = Project(record);
        var html = Render(page);

        return Results.Content(html, PortalContent.HtmlContentType);
    }

    private static void SetPrivateResponseHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = CacheControlValue;
        response.Headers.Pragma = PragmaValue;
        response.Headers.Expires = "0";
    }

    /// <summary>
    /// A closed browser projection. Persistence identifiers, tenant and session
    /// values, timestamps, retention facts, and every non-approved field are
    /// omitted by construction.
    /// </summary>
    private static ReviewPageDto Project(PersistedReviewRecord record) =>
        new(
            record.TaxYear,
            record.PackageVersion,
            Array.AsReadOnly(
                record.Slips
                    .Select(slip =>
                        new ReviewSlipDto(
                            slip.SlipTypeCode,
                            slip.TaxpayerDisplayName,
                            slip.EmployerDisplayName,
                            Array.AsReadOnly(
                                slip.BoxAmounts
                                    .Select(amount =>
                                        new ReviewBoxDto(
                                            amount.BoxCode,
                                            Invariant(amount.Amount)))
                                    .ToArray()),
                            Array.AsReadOnly(
                                slip.LineEntries
                                    .Select(entry =>
                                        new ReviewLineDto(
                                            entry.LineCode,
                                            Invariant(entry.Amount),
                                            Array.AsReadOnly(
                                                entry.SourceBoxCodes
                                                    .Select(source => source.BoxCode)
                                                    .ToArray())))
                                    .ToArray()),
                            Array.AsReadOnly(
                                slip.UnmappedBoxCodes
                                    .Select(code => code.BoxCode)
                                    .ToArray())))
                    .ToArray()));

    private static string Invariant(IFormattable amount) =>
        amount.ToString(format: null, CultureInfo.InvariantCulture);

    private static bool IsBrowserSafe(PersistedReviewRecord record)
    {
        foreach (var slip in record.Slips)
        {
            if (IsForbiddenText(slip.SlipTypeCode) ||
                IsForbiddenText(slip.TaxpayerDisplayName) ||
                IsForbiddenText(slip.EmployerDisplayName))
            {
                return false;
            }

            foreach (var amount in slip.BoxAmounts)
            {
                if (IsForbiddenText(amount.BoxCode))
                {
                    return false;
                }
            }

            foreach (var entry in slip.LineEntries)
            {
                if (IsForbiddenText(entry.LineCode) ||
                    entry.SourceBoxCodes.Any(source => IsForbiddenText(source.BoxCode)))
                {
                    return false;
                }
            }

            if (slip.UnmappedBoxCodes.Any(code => IsForbiddenText(code.BoxCode)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsForbiddenText(string value) =>
        NineDigitRun.IsMatch(value) ||
        MaskedNineDigitShape.IsMatch(value);

    private static string Render(ReviewPageDto page)
    {
        var html = new StringBuilder();

        html.Append("<!DOCTYPE html>\n");
        html.Append("<html lang=\"en\">\n");
        html.Append("<head>\n");
        html.Append("<meta charset=\"utf-8\" />\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n");
        html.Append("<title>Accountant review | TaxAI</title>\n");
        html.Append("<style>\n");
        html.Append(":root{color-scheme:light;--ink:#17202a;--muted:#5b6573;--line:#d9dee6;--paper:#fff;--wash:#f4f6f8;--accent:#155e75;--warn:#fff6dd}\n");
        html.Append("*{box-sizing:border-box}body{margin:0;background:var(--wash);color:var(--ink);font:16px/1.5 system-ui,sans-serif}\n");
        html.Append("main{max-width:76rem;margin:0 auto;padding:2rem 1rem 4rem}.topline{color:var(--accent);font-weight:700;letter-spacing:.08em;text-transform:uppercase}\n");
        html.Append("h1{font-size:clamp(2rem,5vw,3.6rem);line-height:1.05;margin:.2rem 0 1.5rem}h2{margin:0 0 1rem}h3{margin:1.5rem 0 .5rem}\n");
        html.Append(".summary,.slip{background:var(--paper);border:1px solid var(--line);border-radius:1rem;box-shadow:0 .4rem 1.4rem rgba(23,32,42,.06)}\n");
        html.Append(".summary{display:flex;gap:2rem;padding:1rem 1.25rem;margin-bottom:1.5rem}.summary div{min-width:8rem}.summary dt{color:var(--muted);font-size:.85rem}.summary dd{font-size:1.35rem;font-weight:750;margin:0}\n");
        html.Append(".slip{padding:1.4rem;margin:1rem 0 1.5rem}.identity{display:grid;grid-template-columns:repeat(auto-fit,minmax(14rem,1fr));gap:1rem;margin:0 0 1.5rem}.identity div{border-left:.25rem solid var(--accent);padding-left:.75rem}.label{display:block;color:var(--muted);font-size:.82rem;text-transform:uppercase;letter-spacing:.05em}\n");
        html.Append(".table-wrap{overflow-x:auto}table{border-collapse:collapse;width:100%;margin:.4rem 0 1rem}th,td{border-bottom:1px solid var(--line);padding:.65rem;text-align:left;vertical-align:top}th{background:#edf4f6;font-size:.88rem}.amount{font-variant-numeric:tabular-nums;white-space:nowrap}\n");
        html.Append(".codes{display:flex;flex-wrap:wrap;gap:.35rem;list-style:none;padding:0;margin:0}.codes li{background:#edf4f6;border-radius:999px;padding:.15rem .55rem}.empty{background:var(--warn);border-radius:.6rem;padding:.75rem 1rem;color:#5f4b12}\n");
        html.Append("@media(max-width:40rem){main{padding-top:1.25rem}.summary{display:grid;grid-template-columns:1fr 1fr}.slip{padding:1rem}th,td{padding:.5rem}}\n");
        html.Append("</style>\n");
        html.Append("</head>\n");
        html.Append("<body>\n");
        html.Append("<main>\n");
        html.Append("<p class=\"topline\">Accountant review</p>\n");
        html.Append("<h1>Review package</h1>\n");
        html.Append("<dl class=\"summary\">\n");
        html.Append("<div><dt>Tax year</dt><dd>");
        AppendEncoded(html, page.Year.ToString(CultureInfo.InvariantCulture));
        html.Append("</dd></div>\n");
        html.Append("<div><dt>Package version</dt><dd>");
        AppendEncoded(html, page.Version.ToString(CultureInfo.InvariantCulture));
        html.Append("</dd></div>\n");
        html.Append("</dl>\n");

        if (page.Slips.Count == 0)
        {
            html.Append("<p class=\"empty\">This review package contains no slips.</p>\n");
        }

        foreach (var slip in page.Slips)
        {
            html.Append("<article class=\"slip\">\n");
            html.Append("<h2>Slip</h2>\n");
            html.Append("<div class=\"identity\">\n");
            AppendFact(html, "Slip type", slip.Type);
            AppendFact(html, "Taxpayer", slip.Taxpayer);
            AppendFact(html, "Employer", slip.Employer);
            html.Append("</div>\n");

            html.Append("<h3>Boxes and values</h3>\n");
            if (slip.Boxes.Count == 0)
            {
                html.Append("<p class=\"empty\">No populated boxes.</p>\n");
            }
            else
            {
                html.Append("<div class=\"table-wrap\"><table><thead><tr><th scope=\"col\">Box</th><th scope=\"col\">Value</th></tr></thead><tbody>\n");
                foreach (var box in slip.Boxes)
                {
                    html.Append("<tr><td>");
                    AppendEncoded(html, box.Code);
                    html.Append("</td><td class=\"amount\">");
                    AppendEncoded(html, box.Amount);
                    html.Append("</td></tr>\n");
                }

                html.Append("</tbody></table></div>\n");
            }

            html.Append("<h3>Tax lines</h3>\n");
            if (slip.Lines.Count == 0)
            {
                html.Append("<p class=\"empty\">No tax lines.</p>\n");
            }
            else
            {
                html.Append("<div class=\"table-wrap\"><table><thead><tr><th scope=\"col\">Tax line</th><th scope=\"col\">Value</th><th scope=\"col\">Source boxes</th></tr></thead><tbody>\n");
                foreach (var line in slip.Lines)
                {
                    html.Append("<tr><td>");
                    AppendEncoded(html, line.Code);
                    html.Append("</td><td class=\"amount\">");
                    AppendEncoded(html, line.Amount);
                    html.Append("</td><td>");
                    AppendCodes(html, line.SourceBoxes, "No source-box references.");
                    html.Append("</td></tr>\n");
                }

                html.Append("</tbody></table></div>\n");
            }

            html.Append("<h3>Unmapped codes</h3>\n");
            AppendCodes(html, slip.UnmappedCodes, "No unmapped codes.");
            html.Append("</article>\n");
        }

        html.Append("</main>\n");
        html.Append("</body>\n");
        html.Append("</html>\n");

        return html.ToString();
    }

    private static void AppendFact(StringBuilder html, string label, string value)
    {
        html.Append("<div><span class=\"label\">");
        html.Append(label);
        html.Append("</span><strong>");
        AppendEncoded(
            html,
            string.IsNullOrWhiteSpace(value) ? EmptyValue : value);
        html.Append("</strong></div>\n");
    }

    private static void AppendCodes(
        StringBuilder html,
        IReadOnlyList<string> codes,
        string emptyMessage)
    {
        if (codes.Count == 0)
        {
            html.Append("<p class=\"empty\">");
            html.Append(emptyMessage);
            html.Append("</p>");
            return;
        }

        html.Append("<ul class=\"codes\">");
        foreach (var code in codes)
        {
            html.Append("<li>");
            AppendEncoded(html, code);
            html.Append("</li>");
        }

        html.Append("</ul>");
    }

    private static void AppendEncoded(StringBuilder html, string value) =>
        html.Append(HtmlEncoder.Default.Encode(value));

    private sealed record ReviewPageDto(
        int Year,
        int Version,
        IReadOnlyList<ReviewSlipDto> Slips);

    private sealed record ReviewSlipDto(
        string Type,
        string Taxpayer,
        string Employer,
        IReadOnlyList<ReviewBoxDto> Boxes,
        IReadOnlyList<ReviewLineDto> Lines,
        IReadOnlyList<string> UnmappedCodes);

    private sealed record ReviewBoxDto(
        string Code,
        string Amount);

    private sealed record ReviewLineDto(
        string Code,
        string Amount,
        IReadOnlyList<string> SourceBoxes);
}
