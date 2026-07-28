using CaseReview.Models;
using CaseReview.Services;
using TaxLogicEngine.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Tenancy.Services;

/// <summary>
/// Converts an assembled review package into persistence entities through a
/// single explicit <strong>closed allowlist</strong>.
/// <para>
/// One projection, one allowlist, one place to read. A field reaches the
/// database because it is named here or it does not reach it at all — which is
/// what makes "no SIN is stored" a property somebody can verify by reading one
/// file rather than by auditing every write path.
/// </para>
/// <para>
/// <strong>It never reads the slip's SIN.</strong> It names no recipient
/// identifier and constructs no masking, redaction, sanitizing, or scrubbing
/// helper: PII here is handled by omission, not masking, so there is no masked
/// form to accidentally widen later. The engine's own masking behaviour is
/// untouched and is not this module's business.
/// </para>
/// <para>
/// It invents no tax truth. Every box code, CRA line code, and amount is copied
/// verbatim from the package the engine produced. There is no box-to-line table,
/// no CRA line literal, no mapping-resource read, no arithmetic, and no
/// rounding.
/// </para>
/// <para>
/// Collections are copied completely and in the package's own order with an
/// explicit stored ordinal — nothing is sorted, grouped, filtered, deduplicated,
/// merged, subtotalled, aggregated, or dropped.
/// </para>
/// <para>
/// It is pure: no clock is read, and the version a record will occupy is not
/// decided here, because the existing maximum is a fact about storage rather
/// than about the package.
/// </para>
/// </summary>
public static class ReviewPackageProjection
{
    /// <summary>
    /// Fails closed. Nothing is written unless every guard passes, and the
    /// <strong>trusted</strong> firm — never the firm the package carried — is
    /// what the record stores.
    /// </summary>
    public static ParseResult<PersistedReviewRecord> Project(
        TrustedFirmId trustedFirm,
        AccountantReviewPackage package,
        DateTimeOffset evaluationInstantUtc,
        IOpaqueIdentifierSource identifiers)
    {
        var errors = new List<ValidationError>();

        if (package is null)
        {
            errors.Add(new ValidationError(
                TenancyFields.Package,
                TenancyErrors.PackageRequired));
        }

        if (identifiers is null)
        {
            errors.Add(new ValidationError(
                TenancyFields.IdentifierSource,
                TenancyErrors.IdentifierSourceRequired));
        }

        if (trustedFirm is null)
        {
            errors.Add(new ValidationError(
                TenancyFields.TrustedFirmId,
                TenancyErrors.TrustedFirmRequired));
        }

        if (package is null)
        {
            return Failure(errors);
        }

        AddIfPresent(
            errors,
            UtcInstantGuard.Validate(
                evaluationInstantUtc,
                TenancyFields.EvaluationInstant));

        if (!TrustedFirmId.TryParse(package.FirmId, out var packageFirm))
        {
            errors.Add(new ValidationError(
                TenancyFields.FirmId,
                TenancyErrors.FirmIdNotCanonical));
        }
        else if (trustedFirm is not null && !trustedFirm.Equals(packageFirm))
        {
            errors.Add(new ValidationError(
                TenancyFields.FirmId,
                TenancyErrors.FirmMismatch));
        }

        if (!SupportedReviewTaxYears.IsSupported(package.TaxYear))
        {
            errors.Add(new ValidationError(
                TenancyFields.TaxYear,
                TenancyErrors.TaxYearNotSupported));
        }

        for (var slipOrdinal = 0; slipOrdinal < package.ReviewedSlips.Count; slipOrdinal++)
        {
            if (package.ReviewedSlips[slipOrdinal].TaxYear != package.TaxYear)
            {
                errors.Add(new ValidationError(
                    TenancyFields.SlipTaxYear,
                    TenancyErrors.SlipTaxYearDiverges));
            }
        }

        AddIfPresent(
            errors,
            OpaqueTokenGuard.Validate(
                package.CaseId,
                PersistedSchema.CaseIdMaxLength,
                TenancyFields.CaseId));
        AddIfPresent(
            errors,
            OpaqueTokenGuard.Validate(
                package.ParticipantId,
                PersistedSchema.ParticipantIdMaxLength,
                TenancyFields.ParticipantId));

        for (var slipOrdinal = 0; slipOrdinal < package.ReviewedSlips.Count; slipOrdinal++)
        {
            ValidateSlip(package.ReviewedSlips[slipOrdinal], errors);
        }

        if (errors.Count > 0)
        {
            return Failure(errors);
        }

        var validatedIdentifiers = identifiers!;
        var validatedTrustedFirm = trustedFirm!;
        var recordId = validatedIdentifiers.NewIdentifier();
        var slips = new List<PersistedSlip>(package.ReviewedSlips.Count);

        for (var slipOrdinal = 0; slipOrdinal < package.ReviewedSlips.Count; slipOrdinal++)
        {
            slips.Add(BuildSlip(
                package.ReviewedSlips[slipOrdinal],
                validatedTrustedFirm.Value,
                recordId,
                slipOrdinal,
                validatedIdentifiers));
        }

        var record = new PersistedReviewRecord(
            recordId,
            validatedTrustedFirm.Value,
            package.CaseId,
            package.ParticipantId,
            package.TaxYear,
            1,
            evaluationInstantUtc,
            evaluationInstantUtc,
            RetentionPolicy.ComputeRetentionUntilUtc(package.TaxYear),
            slips);

        return ParseResult<PersistedReviewRecord>.Success(record);
    }

    private static void ValidateSlip(
        ReviewedSlip reviewedSlip,
        ICollection<ValidationError> errors)
    {
        AddIfPresent(
            errors,
            TaxpayerTextGuard.Validate(
                reviewedSlip.Slip.EmployeeName,
                PersistedSchema.DisplayNameMaxLength,
                TenancyFields.TaxpayerDisplayName));
        AddIfPresent(
            errors,
            TaxpayerTextGuard.Validate(
                reviewedSlip.Slip.EmployerName,
                PersistedSchema.DisplayNameMaxLength,
                TenancyFields.EmployerDisplayName));

        if (!SlipTypeCodes.IsRecognized(SlipTypeCodes.T4))
        {
            errors.Add(new ValidationError(
                TenancyFields.SlipTypeCode,
                TenancyErrors.SlipTypeNotRecognized));
        }

        for (var boxOrdinal = 0; boxOrdinal < reviewedSlip.BoxAmounts.Count; boxOrdinal++)
        {
            var boxAmount = reviewedSlip.BoxAmounts[boxOrdinal];

            AddIfPresent(
                errors,
                OpaqueTokenGuard.Validate(
                    boxAmount.BoxCode,
                    PersistedSchema.BoxCodeMaxLength,
                    TenancyFields.BoxCode));
            AddIfPresent(
                errors,
                MonetaryValueGuard.Validate(
                    boxAmount.Amount,
                    TenancyFields.Amount));
        }

        for (var lineOrdinal = 0; lineOrdinal < reviewedSlip.LineEntries.Count; lineOrdinal++)
        {
            var lineEntry = reviewedSlip.LineEntries[lineOrdinal];

            AddIfPresent(
                errors,
                OpaqueTokenGuard.Validate(
                    lineEntry.LineNumber,
                    PersistedSchema.LineCodeMaxLength,
                    TenancyFields.LineCode));
            AddIfPresent(
                errors,
                MonetaryValueGuard.Validate(
                    lineEntry.Amount,
                    TenancyFields.Amount));

            for (var sourceOrdinal = 0;
                 sourceOrdinal < lineEntry.SourceBoxCodes.Count;
                 sourceOrdinal++)
            {
                AddIfPresent(
                    errors,
                    OpaqueTokenGuard.Validate(
                        lineEntry.SourceBoxCodes[sourceOrdinal],
                        PersistedSchema.BoxCodeMaxLength,
                        TenancyFields.BoxCode));
            }
        }

        for (var unmappedOrdinal = 0;
             unmappedOrdinal < reviewedSlip.UnmappedBoxCodes.Count;
             unmappedOrdinal++)
        {
            AddIfPresent(
                errors,
                OpaqueTokenGuard.Validate(
                    reviewedSlip.UnmappedBoxCodes[unmappedOrdinal],
                    PersistedSchema.BoxCodeMaxLength,
                    TenancyFields.BoxCode));
        }
    }

    private static PersistedSlip BuildSlip(
        ReviewedSlip reviewedSlip,
        Guid firmId,
        Guid recordId,
        int slipOrdinal,
        IOpaqueIdentifierSource identifiers)
    {
        var slipId = identifiers.NewIdentifier();
        var documentReference = identifiers.NewIdentifier();
        var boxAmounts = new List<PersistedBoxAmount>(reviewedSlip.BoxAmounts.Count);
        var lineEntries = new List<PersistedLineEntry>(reviewedSlip.LineEntries.Count);
        var unmappedBoxCodes =
            new List<PersistedUnmappedBoxCode>(reviewedSlip.UnmappedBoxCodes.Count);

        for (var boxOrdinal = 0; boxOrdinal < reviewedSlip.BoxAmounts.Count; boxOrdinal++)
        {
            var boxAmount = reviewedSlip.BoxAmounts[boxOrdinal];
            boxAmounts.Add(new PersistedBoxAmount(
                identifiers.NewIdentifier(),
                firmId,
                slipId,
                boxOrdinal,
                boxAmount.BoxCode,
                boxAmount.Amount));
        }

        for (var lineOrdinal = 0; lineOrdinal < reviewedSlip.LineEntries.Count; lineOrdinal++)
        {
            var lineEntry = reviewedSlip.LineEntries[lineOrdinal];
            var lineEntryId = identifiers.NewIdentifier();
            var sourceBoxCodes =
                new List<PersistedSourceBoxCode>(lineEntry.SourceBoxCodes.Count);

            for (var sourceOrdinal = 0;
                 sourceOrdinal < lineEntry.SourceBoxCodes.Count;
                 sourceOrdinal++)
            {
                sourceBoxCodes.Add(new PersistedSourceBoxCode(
                    identifiers.NewIdentifier(),
                    firmId,
                    lineEntryId,
                    sourceOrdinal,
                    lineEntry.SourceBoxCodes[sourceOrdinal]));
            }

            lineEntries.Add(new PersistedLineEntry(
                lineEntryId,
                firmId,
                slipId,
                lineOrdinal,
                lineEntry.LineNumber,
                lineEntry.Amount,
                sourceBoxCodes));
        }

        for (var unmappedOrdinal = 0;
             unmappedOrdinal < reviewedSlip.UnmappedBoxCodes.Count;
             unmappedOrdinal++)
        {
            unmappedBoxCodes.Add(new PersistedUnmappedBoxCode(
                identifiers.NewIdentifier(),
                firmId,
                slipId,
                unmappedOrdinal,
                reviewedSlip.UnmappedBoxCodes[unmappedOrdinal]));
        }

        return new PersistedSlip(
            slipId,
            firmId,
            recordId,
            slipOrdinal,
            SlipTypeCodes.T4,
            documentReference,
            reviewedSlip.Slip.EmployeeName,
            reviewedSlip.Slip.EmployerName,
            boxAmounts,
            lineEntries,
            unmappedBoxCodes);
    }

    private static void AddIfPresent(
        ICollection<ValidationError> errors,
        ValidationError? error)
    {
        if (error is not null)
        {
            errors.Add(error);
        }
    }

    private static ParseResult<PersistedReviewRecord> Failure(
        IReadOnlyCollection<ValidationError> errors) =>
        ParseResult<PersistedReviewRecord>.Failure(
            Array.AsReadOnly(errors.ToArray()));
}
