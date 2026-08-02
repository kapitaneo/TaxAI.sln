using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using TaxAI.Portal.Components.Pages;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.Portal;

/// <summary>
/// The portal's single read-only taxpayer-data surface.
/// <para>
/// It consumes only the persisted, SIN-free Tenancy model. The route selector
/// chooses one immutable stored version; it is never rendered, copied into the
/// model, or used as tenant identity. The trusted firm comes only from the
/// authorization state established from the durable server-side session.
/// </para>
/// <para>
/// Spec 014 T01 replaced this endpoint's renderer and <b>nothing else</b>. The
/// route, the policy, the private response headers, the trusted-firm extraction,
/// the browser-safety guard, the closed projection, the invariant amount
/// formatting, the collection order, the single <c>FindAsync</c> call with the
/// request cancellation token, and the identical empty <c>404</c> for missing,
/// foreign, and browser-unsafe records are all unchanged. What disappeared is the
/// <c>Render</c> method, its <c>StringBuilder</c>, its append-based markup, and
/// its inline style assembly; the success branch now returns a strongly typed,
/// statically server-rendered component.
/// </para>
/// <para>
/// The order of operations is the security contract. The refusal is returned
/// <b>before</b> a taxpayer component is instantiated, so a refused request cannot
/// render a partial page.
/// </para>
/// </summary>
internal static class ReviewEndpoints
{
    internal const string RoutePattern = "/reviews/{recordId:guid}";

    private const string CacheControlValue = "no-store, no-cache, must-revalidate";

    private const string PragmaValue = "no-cache";

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

        // The component is constructed only here, on the success branch, from one
        // closed model. It receives no entity, identifier, or repository.
        //
        // The parameters are an anonymous object rather than a dictionary on
        // purpose: this project is scanned for `Dictionary<`, because a keyed
        // lookup in the web layer is how a second box-to-line mapping authority
        // would arrive. One approved parameter does not need a dictionary.
        return new RazorComponentResult<ReviewPage>(
            new { Model = Project(record) });
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
    private static ReviewPageModel Project(PersistedReviewRecord record) =>
        new(
            record.TaxYear,
            record.PackageVersion,
            Array.AsReadOnly(
                record.Slips
                    .Select(slip =>
                        new ReviewSlipModel(
                            slip.SlipTypeCode,
                            slip.TaxpayerDisplayName,
                            slip.EmployerDisplayName,
                            Array.AsReadOnly(
                                slip.BoxAmounts
                                    .Select(amount =>
                                        new ReviewBoxModel(
                                            amount.BoxCode,
                                            Invariant(amount.Amount)))
                                    .ToArray()),
                            Array.AsReadOnly(
                                slip.LineEntries
                                    .Select(entry =>
                                        new ReviewLineModel(
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

    /// <summary>
    /// Formats the stored value exactly as stored, in the invariant culture. No
    /// rounding, no aggregation, no currency symbol, no grouping — the portal
    /// computes no tax, so it also does not reshape a tax amount on the way out.
    /// </summary>
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
}
