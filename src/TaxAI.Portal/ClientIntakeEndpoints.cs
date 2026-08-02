using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClientIntake.Contracts;
using ClientIntake.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using TaxAI.Portal.Components.Pages;
using TaxCases.Models;

namespace TaxAI.Portal;

/// <summary>
/// The identifier-free client intake HTTP boundary.
/// </summary>
internal static class ClientIntakeEndpoints
{
    internal const string LinkPath = "/client-intake/link/{credential}";
    internal const string RedeemPath = "/client-intake/redeem";
    internal const string IntakePath = "/client-intake";
    internal const string AnswersPath = "/client-intake/answers";
    internal const string DocumentsPath = "/client-intake/documents";
    internal const string SubmissionsPath = "/client-intake/submissions";

    private const string RedemptionPurpose =
        "TaxAI.Portal.ClientIntake.Redemption.v1";
    private const int ProofLength = 1 + sizeof(long) + 32;
    private const int MaximumControlValueLength = 128;

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(LinkPath, InspectLinkAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IntakeHttpSecurity.PublicPolicy);
        endpoints.MapPost(RedeemPath, RedeemAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IntakeHttpSecurity.PublicPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
        endpoints.MapGet(IntakePath, GetAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IntakeHttpSecurity.SessionPolicy);
        endpoints.MapPost(AnswersPath, SaveAnswersAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IntakeHttpSecurity.AnswerPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
        endpoints.MapPost(DocumentsPath, UploadAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IntakeHttpSecurity.UploadPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
        endpoints.MapPost(SubmissionsPath, SubmitAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IntakeHttpSecurity.SubmissionPolicy)
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
    }

    private static async Task<IResult> InspectLinkAsync(
        string credential,
        HttpContext context,
        IClientIntakeWorkflow workflow,
        IDataProtectionProvider dataProtection,
        TimeProvider clock)
    {
        if (!IntakeSecret.TryParse(credential, out var parsed))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.InspectLinkAsync(
            parsed!,
            clock.GetUtcNow(),
            context.RequestAborted);
        if (!result.IsSuccess || result.Value is null)
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var inspection = result.Value;
        var handle = ProtectProof(dataProtection, inspection.Proof);
        return new RazorComponentResult<ClientLinkConfirmationPage>(
            new { inspection.TaxYear, RedemptionHandle = handle });
    }

    private static async Task<IResult> RedeemAsync(
        HttpContext context,
        IClientIntakeWorkflow workflow,
        IDataProtectionProvider dataProtection,
        ClientIntakeCookie cookie,
        TimeProvider clock,
        [FromForm(Name = "redemptionHandle")] string? redemptionHandle)
    {
        if (!TryUnprotectProof(
                dataProtection,
                redemptionHandle,
                out var proof))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.RedeemLinkAsync(
            proof!,
            clock.GetUtcNow(),
            context.RequestAborted);
        if (!result.IsSuccess || result.Value is null)
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        cookie.Append(context.Response, result.Value);
        context.Response.StatusCode = StatusCodes.Status303SeeOther;
        context.Response.Headers.Location = IntakePath;
        return Results.Empty;
    }

    private static async Task<IResult> GetAsync(
        HttpContext context,
        IClientIntakeWorkflow workflow,
        ClientIntakeCookie cookie,
        TimeProvider clock)
    {
        if (!cookie.TryRead(context.Request, out var credential))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.GetAsync(
            credential!,
            clock.GetUtcNow(),
            context.RequestAborted);
        return ClientPage(context, result);
    }

    private static async Task<IResult> SaveAnswersAsync(
        HttpContext context,
        IClientIntakeWorkflow workflow,
        ClientIntakeCookie cookie,
        TimeProvider clock,
        [FromForm(Name = "t4Answer")] string? t4Answer,
        [FromForm(Name = "medicalExpenseAnswer")] string? medicalAnswer)
    {
        if (!cookie.TryRead(context.Request, out var credential))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var answers = new List<IntakeAnswerInput>(capacity: 2);
        if (t4Answer is not null)
        {
            var answer = ParseAnswer(t4Answer);
            if (answer is null)
            {
                return IntakeHttpSecurity.Invalid(context);
            }

            answers.Add(
                new IntakeAnswerInput(
                    DocumentCategory.T4Slip,
                    answer.Value));
        }

        if (medicalAnswer is not null)
        {
            var answer = ParseAnswer(medicalAnswer);
            if (answer is null)
            {
                return IntakeHttpSecurity.Invalid(context);
            }

            answers.Add(
                new IntakeAnswerInput(
                    DocumentCategory.MedicalExpenseEvidence,
                    answer.Value));
        }

        if (answers.Count == 0)
        {
            return IntakeHttpSecurity.Invalid(context);
        }

        var result = await workflow.SaveAnswersAsync(
            credential!,
            new SaveIntakeAnswersCommand(answers),
            clock.GetUtcNow(),
            context.RequestAborted);
        return ClientPage(context, result);
    }

    private static async Task<IResult> UploadAsync(
        HttpContext context,
        IClientIntakeWorkflow workflow,
        ClientIntakeCookie cookie,
        IntakeRuntimeConfiguration configuration,
        TimeProvider clock)
    {
        if (!cookie.TryRead(context.Request, out var credential))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        if (!TryBoundary(context.Request.ContentType, out var boundary))
        {
            return IntakeHttpSecurity.Invalid(context);
        }

        try
        {
            var reader = new MultipartReader(boundary!, context.Request.Body);
            DocumentCategory? category = null;
            int? ordinal = null;
            IntakeMediaKind? mediaKind = null;
            long? declaredLength = null;
            var fileCount = 0;
            IntakeResult<ClientIntakeView>? uploadResult = null;

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(
                       context.RequestAborted)) is not null)
            {
                if (!ContentDispositionHeaderValue.TryParse(
                        section.ContentDisposition,
                        out var disposition) ||
                    !string.Equals(
                        disposition.DispositionType.Value,
                        "form-data",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return IntakeHttpSecurity.Invalid(context);
                }

                var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
                if (string.Equals(name, "document", StringComparison.Ordinal))
                {
                    fileCount++;
                    if (fileCount > configuration.Workflow.MaximumFilesPerRequest ||
                        uploadResult is not null ||
                        category is null ||
                        ordinal is null ||
                        declaredLength is null)
                    {
                        return IntakeHttpSecurity.Invalid(context);
                    }

                    var command = new IntakeUploadCommand(
                        category,
                        ordinal.Value,
                        mediaKind,
                        declaredLength.Value);
                    using var content = new FinalMultipartSectionStream(
                        section.Body,
                        reader,
                        context.RequestAborted);
                    uploadResult = await workflow.UploadAsync(
                        credential!,
                        command,
                        content,
                        clock.GetUtcNow(),
                        context.RequestAborted);
                    break;
                }

                var value = await ReadControlValueAsync(
                    section.Body,
                    context.RequestAborted);
                if (value is null)
                {
                    return IntakeHttpSecurity.Invalid(context);
                }

                switch (name)
                {
                    case "category":
                        category = ParseCategory(value);
                        break;
                    case "ordinal":
                        ordinal = ParsePositiveInt(value);
                        break;
                    case "declaredMediaKind":
                        mediaKind = ParseMediaKind(value);
                        break;
                    case "declaredLength":
                        declaredLength = ParseNonNegativeLong(value);
                        break;
                    default:
                        return IntakeHttpSecurity.Invalid(context);
                }
            }

            if (fileCount == 0 || uploadResult is null)
            {
                return IntakeHttpSecurity.Invalid(context);
            }

            return uploadResult.Code == IntakeResultCode.Duplicate
                ? IntakeHttpSecurity.Duplicate(context)
                : ClientPage(context, uploadResult);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or
                FormatException)
        {
            return IntakeHttpSecurity.Invalid(context);
        }
    }

    private static async Task<IResult> SubmitAsync(
        HttpContext context,
        IClientIntakeWorkflow workflow,
        ClientIntakeCookie cookie,
        TimeProvider clock)
    {
        if (!cookie.TryRead(context.Request, out var credential))
        {
            return IntakeHttpSecurity.NotFound(context);
        }

        var result = await workflow.SubmitAsync(
            credential!,
            clock.GetUtcNow(),
            context.RequestAborted);
        return ClientPage(context, result);
    }

    private static IResult ClientPage(
        HttpContext context,
        IntakeResult<ClientIntakeView> result)
    {
        if (!result.IsSuccess || result.Value is null)
        {
            return result.Code switch
            {
                IntakeResultCode.NotFound =>
                    IntakeHttpSecurity.NotFound(context),
                IntakeResultCode.Duplicate =>
                    IntakeHttpSecurity.Duplicate(context),
                IntakeResultCode.Conflict =>
                    IntakeHttpSecurity.Conflict(context),
                _ => IntakeHttpSecurity.Invalid(context),
            };
        }

        return new RazorComponentResult<ClientIntakePage>(
            new { Model = result.Value });
    }

    private static string ProtectProof(
        IDataProtectionProvider provider,
        IntakeRedemptionProof proof)
    {
        var payload = new byte[ProofLength];
        payload[0] = 1;
        BinaryPrimitives.WriteInt64BigEndian(
            payload.AsSpan(1, sizeof(long)),
            proof.LinkExpiresAtUtc.UtcTicks);
        proof.LinkDigest.Bytes
            .ToArray()
            .CopyTo(payload, 1 + sizeof(long));
        var protectedBytes = provider
            .CreateProtector(RedemptionPurpose)
            .Protect(payload);
        return WebEncoders.Base64UrlEncode(protectedBytes);
    }

    private static bool TryUnprotectProof(
        IDataProtectionProvider provider,
        string? protectedValue,
        out IntakeRedemptionProof? proof)
    {
        proof = null;
        if (string.IsNullOrEmpty(protectedValue))
        {
            return false;
        }

        try
        {
            var protectedBytes = WebEncoders.Base64UrlDecode(protectedValue);
            var payload = provider
                .CreateProtector(RedemptionPurpose)
                .Unprotect(protectedBytes);
            if (payload.Length != ProofLength || payload[0] != 1)
            {
                return false;
            }

            var utcTicks = BinaryPrimitives.ReadInt64BigEndian(
                payload.AsSpan(1, sizeof(long)));
            var expiresAt = new DateTimeOffset(utcTicks, TimeSpan.Zero);
            proof = new IntakeRedemptionProof(
                new IntakeTokenDigest(payload[(1 + sizeof(long))..]),
                expiresAt);
            return true;
        }
        catch (Exception exception) when (
            exception is CryptographicException or FormatException or
                ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryBoundary(
        string? contentType,
        out string? boundary)
    {
        boundary = null;
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed) ||
            !string.Equals(
                parsed.MediaType.Value,
                "multipart/form-data",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        boundary = HeaderUtilities.RemoveQuotes(parsed.Boundary).Value;
        return !string.IsNullOrEmpty(boundary) &&
               boundary.Length <= 128;
    }

    private static async Task<string?> ReadControlValueAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[MaximumControlValueLength + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(count, buffer.Length - count),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        return count > MaximumControlValueLength
            ? null
            : Encoding.UTF8.GetString(buffer, 0, count);
    }

    private static AvailabilityAnswerCode? ParseAnswer(string value) =>
        Enum.GetValues<AvailabilityAnswerCode>()
            .Cast<AvailabilityAnswerCode?>()
            .SingleOrDefault(candidate =>
                string.Equals(
                    candidate?.ToString(),
                    value,
                    StringComparison.Ordinal));

    private static DocumentCategory? ParseCategory(string value) =>
        value switch
        {
            nameof(DocumentCategory.T4Slip) => DocumentCategory.T4Slip,
            nameof(DocumentCategory.MedicalExpenseEvidence) =>
                DocumentCategory.MedicalExpenseEvidence,
            _ => null,
        };

    private static IntakeMediaKind? ParseMediaKind(string value) =>
        value switch
        {
            nameof(IntakeMediaKind.Pdf) => IntakeMediaKind.Pdf,
            nameof(IntakeMediaKind.Jpeg) => IntakeMediaKind.Jpeg,
            nameof(IntakeMediaKind.Png) => IntakeMediaKind.Png,
            _ => null,
        };

    private static int? ParsePositiveInt(string value) =>
        int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed) &&
        parsed > 0
            ? parsed
            : null;

    private static long? ParseNonNegativeLong(string value) =>
        long.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed) &&
        parsed >= 0
            ? parsed
            : null;

    /// <summary>
    /// Exposes one file section as a forward-only stream and withholds its EOF
    /// until the multipart reader proves that no second section exists. A valid
    /// upload workflow must consume the stream before it can commit, so an
    /// above-limit multipart request fails before successful persistence
    /// without buffering source bytes.
    /// </summary>
    private sealed class FinalMultipartSectionStream : Stream
    {
        private readonly Stream _inner;
        private readonly MultipartReader _reader;
        private readonly CancellationToken _requestAborted;
        private bool _endVerified;

        internal FinalMultipartSectionStream(
            Stream inner,
            MultipartReader reader,
            CancellationToken requestAborted)
        {
            _inner = inner;
            _reader = reader;
            _requestAborted = requestAborted;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            if (read != 0 || _endVerified)
            {
                return read;
            }

            if (await _reader.ReadNextSectionAsync(_requestAborted) is not null)
            {
                throw new InvalidDataException(
                    "The multipart request contains too many sections.");
            }

            _endVerified = true;
            return 0;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(
                buffer.AsMemory(offset, count),
                cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
