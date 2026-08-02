using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace TaxAI.Portal;

/// <summary>
/// The shared HTTP refusal, browser-header, and finite rate-limit boundary for
/// every intake route.
/// </summary>
internal static class IntakeHttpSecurity
{
    internal const string PublicPolicy = "TaxAiIntakePublic";
    internal const string SessionPolicy = "TaxAiIntakeSession";
    internal const string AnswerPolicy = "TaxAiIntakeAnswer";
    internal const string UploadPolicy = "TaxAiIntakeUpload";
    internal const string SubmissionPolicy = "TaxAiIntakeSubmission";
    internal const string AntiforgeryHeaderName = "X-TaxAI-Antiforgery";

    internal const string RateLimitedBody = "Too many requests.";
    internal const string InvalidBody = "Request was not accepted.";
    internal const string ConflictBody = "Request could not be completed.";
    internal const string DuplicateBody = "Upload already received.";

    private const string CacheControlValue = "no-store, no-cache, must-revalidate";
    private const string ContentSecurityPolicyValue =
        "default-src 'none'; base-uri 'none'; connect-src 'self'; "
        + "form-action 'self'; frame-ancestors 'none'; img-src 'self' data:; "
        + "script-src 'self'; style-src 'self'; object-src 'none'";

    internal static IServiceCollection AddIntakeRateLimiting(
        this IServiceCollection services,
        IntakeRuntimeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                ApplyResponseHeaders(context.HttpContext.Response);
                context.HttpContext.Response.ContentType =
                    PortalContent.PlainTextContentType;
                await context.HttpContext.Response.WriteAsync(
                    RateLimitedBody,
                    cancellationToken);
            };

            AddFixedWindowPolicy(
                options,
                PublicPolicy,
                configuration,
                configuration.Rates.PublicPermitLimit,
                configuration.Rates.PublicWindow);
            AddFixedWindowPolicy(
                options,
                SessionPolicy,
                configuration,
                configuration.Rates.SessionPermitLimit,
                configuration.Rates.SessionWindow);
            AddFixedWindowPolicy(
                options,
                AnswerPolicy,
                configuration,
                configuration.Rates.AnswerPermitLimit,
                configuration.Rates.AnswerWindow);
            AddUploadPolicy(options, configuration);
            AddFixedWindowPolicy(
                options,
                SubmissionPolicy,
                configuration,
                configuration.Rates.SubmissionPermitLimit,
                configuration.Rates.SubmissionWindow);
        });

        return services;
    }

    internal static async Task ApplyResponseHeadersAsync(
        HttpContext context,
        RequestDelegate next)
    {
        if (IsIntakePath(context.Request.Path))
        {
            ApplyResponseHeaders(context.Response);
        }

        await next(context);
    }

    internal static void ApplyResponseHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = CacheControlValue;
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
        response.Headers.ContentSecurityPolicy = ContentSecurityPolicyValue;
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers["Permissions-Policy"] =
            "camera=(), geolocation=(), microphone=(), payment=(), usb=()";
        response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        response.Headers["X-Frame-Options"] = "DENY";
    }

    internal static IResult NotFound(HttpContext context)
    {
        ApplyResponseHeaders(context.Response);
        return Results.StatusCode(StatusCodes.Status404NotFound);
    }

    internal static IResult Invalid(HttpContext context) =>
        Fixed(
            context,
            StatusCodes.Status400BadRequest,
            InvalidBody);

    internal static IResult Conflict(HttpContext context) =>
        Fixed(
            context,
            StatusCodes.Status409Conflict,
            ConflictBody);

    internal static IResult Duplicate(HttpContext context) =>
        Fixed(
            context,
            StatusCodes.Status409Conflict,
            DuplicateBody);

    internal static bool IsIntakePath(PathString path) =>
        path.StartsWithSegments("/intake-cases", StringComparison.Ordinal) ||
        path.StartsWithSegments("/client-intake", StringComparison.Ordinal);

    private static IResult Fixed(
        HttpContext context,
        int statusCode,
        string body)
    {
        ApplyResponseHeaders(context.Response);
        return Results.Text(
            body,
            PortalContent.PlainTextContentType,
            statusCode: statusCode);
    }

    private static void AddFixedWindowPolicy(
        RateLimiterOptions options,
        string policyName,
        IntakeRuntimeConfiguration configuration,
        int permitLimit,
        TimeSpan window)
    {
        options.AddPolicy<IntakeRatePartition>(
            policyName,
            context =>
            {
                var partition = CreatePartition(
                    context,
                    configuration.DigestKey,
                    policyName);
                return RateLimitPartition.GetFixedWindowLimiter(
                    partition,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = permitLimit,
                        QueueLimit = 0,
                        Window = window,
                    });
            });
    }

    private static void AddUploadPolicy(
        RateLimiterOptions options,
        IntakeRuntimeConfiguration configuration)
    {
        options.AddPolicy<IntakeRatePartition>(
            UploadPolicy,
            context =>
            {
                var partition = CreatePartition(
                    context,
                    configuration.DigestKey,
                    UploadPolicy);
                return RateLimitPartition.Get(
                    partition,
                    _ => RateLimiter.CreateChained(
                        new FixedWindowRateLimiter(
                            new FixedWindowRateLimiterOptions
                            {
                                AutoReplenishment = true,
                                PermitLimit =
                                    configuration.Rates.UploadPermitLimit,
                                QueueLimit = 0,
                                Window = configuration.Rates.UploadWindow,
                            }),
                        new ConcurrencyLimiter(
                            new ConcurrencyLimiterOptions
                            {
                                PermitLimit =
                                    configuration.Rates.UploadConcurrencyLimit,
                                QueueLimit = 0,
                            })));
            });
    }

    private static IntakeRatePartition CreatePartition(
        HttpContext context,
        byte[] key,
        string purpose)
    {
        var network = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var opaqueCredential = context.Request.Cookies[ClientIntakeCookie.Name];

        if (string.Equals(
                purpose,
                PublicPolicy,
                StringComparison.Ordinal) &&
            context.Request.RouteValues.TryGetValue(
                "credential",
                out var routeCredential))
        {
            opaqueCredential = routeCredential as string;
        }

        var material = string.Concat(
            purpose,
            "\n",
            network,
            "\n",
            opaqueCredential ?? "absent");
        var digest = HMACSHA256.HashData(
            key,
            Encoding.UTF8.GetBytes(material));
        return new IntakeRatePartition(
            Convert.ToBase64String(digest));
    }

    private sealed class IntakeRatePartition : IEquatable<IntakeRatePartition>
    {
        private readonly string _digest;

        internal IntakeRatePartition(string digest)
        {
            _digest = digest;
        }

        public bool Equals(IntakeRatePartition? other) =>
            other is not null &&
            string.Equals(_digest, other._digest, StringComparison.Ordinal);

        public override bool Equals(object? obj) =>
            obj is IntakeRatePartition other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(_digest);

        public override string ToString() => nameof(IntakeRatePartition);
    }
}
