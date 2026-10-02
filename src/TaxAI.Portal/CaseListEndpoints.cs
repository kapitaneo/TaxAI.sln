using ClientIntake.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using TaxAI.Portal.Components.Pages;
using Tenancy.Models;

namespace TaxAI.Portal;

/// <summary>
/// The firm-scoped accountant case list (spec 017 T03, decision D-5).
/// <para>
/// The handler binds nothing from the request: no route value, query, form,
/// body, header, or cookie is read as scope, search, filter, sort, or paging
/// input. The firm comes only from the revalidated server-side authorization
/// state, and the bound is a compile-time constant.
/// </para>
/// </summary>
internal static class CaseListEndpoints
{
    internal const string ListPath = "/intake-cases";

    /// <summary>The fixed bound of the list. A constant, never a request value.</summary>
    internal const int MaximumCases = 100;

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(ListPath, ListAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy);
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        IIntakeStore intake)
    {
        var authorization = PortalAuthorization.BootstrapStateFor(context);
        if (authorization?.TrustedFirmId is not Guid firmId ||
            firmId == Guid.Empty ||
            authorization.User.UserId == Guid.Empty)
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var listed = await intake.ListCasesAsync(
            TrustedFirmId.FromGuid(firmId),
            MaximumCases,
            context.RequestAborted);

        return new RazorComponentResult<CaseListPage>(
            new
            {
                Model = new CaseListPageModel(
                    Array.AsReadOnly(
                        listed.Cases
                            .Select(entry => new CaseListRowModel(
                                entry.CaseId,
                                entry.PrimaryTaxpayerDisplayName,
                                entry.TaxYear))
                            .ToArray()),
                    listed.HasMore),
            });
    }
}
