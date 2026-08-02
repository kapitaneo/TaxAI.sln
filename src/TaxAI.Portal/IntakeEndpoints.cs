using ClientIntake.Contracts;
using ClientIntake.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using TaxAI.Portal.Components.Pages;
using Tenancy.Models;

namespace TaxAI.Portal;

/// <summary>
/// The firm-scoped accountant intake surface. Every handler re-enters R-1 and
/// obtains its firm and actor only from <see cref="PortalAuthorization"/>.
/// </summary>
internal static class IntakeEndpoints
{
    internal const string CreatePagePath = "/intake-cases/new";
    internal const string CreatePath = "/intake-cases";
    internal const string ProgressPath = "/intake-cases/{caseId:guid}";
    internal const string ReissuePath =
        "/intake-cases/{caseId:guid}/link/reissue";
    internal const string RevokePath =
        "/intake-cases/{caseId:guid}/link/revoke";
    internal const string ReviewAssociationPath =
        "/intake-cases/{caseId:guid}/review-association";

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(CreatePagePath, CreatePage)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy);
        endpoints.MapPost(CreatePath, CreateAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
        endpoints.MapGet(ProgressPath, ProgressAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy);
        endpoints.MapPost(ReissuePath, ReissueAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
        endpoints.MapPost(RevokePath, RevokeAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
        endpoints.MapPost(ReviewAssociationPath, AssociateReviewAsync)
            .RequireAuthorization(PortalAuthorization.FirmScopedPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
    }

    private static IResult CreatePage(HttpContext context)
    {
        if (!TryScope(context, out _, out _))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        return new RazorComponentResult<IntakeCaseCreatePage>();
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        IAccountantIntakeWorkflow workflow,
        IntakeRuntimeConfiguration configuration,
        TimeProvider clock,
        [FromForm(Name = "primaryTaxpayerDisplayName")] string? displayName,
        [FromForm(Name = "taxYear")] int? taxYear,
        [FromForm(Name = "expectedT4Count")] int? expectedT4Count,
        [FromForm(Name = "expectedMedicalExpenseCount")]
            int? expectedMedicalExpenseCount)
    {
        if (!TryScope(context, out var firm, out var actor))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.CreateAsync(
            firm!,
            actor,
            new CreateIntakeCaseCommand(
                displayName,
                taxYear,
                expectedT4Count,
                expectedMedicalExpenseCount),
            clock.GetUtcNow(),
            context.RequestAborted);

        if (!result.IsSuccess || result.Value is null)
        {
            return MapFailure(context, result.Code);
        }

        var created = result.Value;
        var clientUrl = new Uri(
            configuration.PublicOrigin,
            $"client-intake/link/{created.LinkCredential.Reveal()}");
        return new RazorComponentResult<IntakeCaseIssuedPage>(
            new
            {
                created.CaseId,
                created.LinkExpiresAtUtc,
                ClientUrl = clientUrl.AbsoluteUri,
            });
    }

    private static async Task<IResult> ProgressAsync(
        Guid caseId,
        HttpContext context,
        IAccountantIntakeWorkflow workflow,
        TimeProvider clock)
    {
        if (!TryScope(context, out var firm, out _))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.GetProgressAsync(
            firm!,
            caseId,
            clock.GetUtcNow(),
            context.RequestAborted);
        return ProgressResult(context, caseId, result);
    }

    private static async Task<IResult> ReissueAsync(
        Guid caseId,
        HttpContext context,
        IAccountantIntakeWorkflow workflow,
        IntakeRuntimeConfiguration configuration,
        TimeProvider clock)
    {
        if (!TryScope(context, out var firm, out var actor))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.ReissueLinkAsync(
            firm!,
            caseId,
            actor,
            clock.GetUtcNow(),
            context.RequestAborted);
        if (!result.IsSuccess || result.Value is null)
        {
            return MapFailure(context, result.Code);
        }

        var issued = result.Value;
        var clientUrl = new Uri(
            configuration.PublicOrigin,
            $"client-intake/link/{issued.LinkCredential.Reveal()}");
        return new RazorComponentResult<IntakeCaseIssuedPage>(
            new
            {
                CaseId = caseId,
                issued.LinkExpiresAtUtc,
                ClientUrl = clientUrl.AbsoluteUri,
            });
    }

    private static async Task<IResult> RevokeAsync(
        Guid caseId,
        HttpContext context,
        IAccountantIntakeWorkflow workflow,
        TimeProvider clock)
    {
        if (!TryScope(context, out var firm, out var actor))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.RevokeLinkAsync(
            firm!,
            caseId,
            actor,
            clock.GetUtcNow(),
            context.RequestAborted);
        return ProgressResult(context, caseId, result);
    }

    private static async Task<IResult> AssociateReviewAsync(
        Guid caseId,
        HttpContext context,
        IAccountantIntakeWorkflow workflow,
        TimeProvider clock,
        [FromForm(Name = "recordId")] Guid? recordId)
    {
        if (!TryScope(context, out var firm, out var actor))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        if (recordId is null || recordId.Value == Guid.Empty)
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.AssociateReviewAsync(
            firm!,
            caseId,
            recordId.Value,
            actor,
            clock.GetUtcNow(),
            context.RequestAborted);
        return ProgressResult(context, caseId, result);
    }

    private static IResult ProgressResult(
        HttpContext context,
        Guid caseId,
        IntakeResult<AccountantIntakeView> result)
    {
        if (!result.IsSuccess || result.Value is null)
        {
            return MapFailure(context, result.Code);
        }

        return new RazorComponentResult<IntakeCaseProgressPage>(
            new { CaseId = caseId, Model = result.Value });
    }

    private static bool TryScope(
        HttpContext context,
        out TrustedFirmId? firm,
        out Guid actor)
    {
        firm = null;
        actor = Guid.Empty;
        var authorization = PortalAuthorization.BootstrapStateFor(context);
        if (authorization?.TrustedFirmId is not Guid firmId ||
            firmId == Guid.Empty ||
            authorization.User.UserId == Guid.Empty)
        {
            return false;
        }

        firm = TrustedFirmId.FromGuid(firmId);
        actor = authorization.User.UserId;
        return true;
    }

    private static IResult MapFailure(
        HttpContext context,
        IntakeResultCode code) =>
        code switch
        {
            IntakeResultCode.NotFound => IntakeHttpSecurity.NotFound(context),
            IntakeResultCode.Conflict or IntakeResultCode.Duplicate =>
                IntakeHttpSecurity.Conflict(context),
            _ => IntakeHttpSecurity.Invalid(context),
        };
}
