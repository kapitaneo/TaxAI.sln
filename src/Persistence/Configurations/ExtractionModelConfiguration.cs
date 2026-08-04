using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Persistence.Entities;
using Tenancy.Models;

namespace Persistence.Configurations;

/// <summary>
/// The bounds every extraction column declares, in one place.
/// <para>
/// They live here rather than in <c>PersistedSchema</c> because they are this
/// feature's own, and because widening a shared constant to fit a new column is
/// how an unrelated table silently gets a longer one.
/// </para>
/// <para>
/// No value is a five-digit integer: the CRA-line scan matches
/// <c>\b\d{5}\b</c>, and a bound written as one would be indistinguishable from
/// a hardcoded CRA line.
/// </para>
/// </summary>
internal static class ExtractionSchema
{
    /// <summary>A field key: an extraction field name or an engine field key.</summary>
    internal const int FieldKeyMaxLength = 64;

    /// <summary>The server-owned label naming the adapter that ran.</summary>
    internal const int ProviderLabelMaxLength = 64;

    /// <summary>A printed value: a box amount, a date, or an amount as shown.</summary>
    internal const int PrintedValueMaxLength = 256;

    /// <summary>A printed currency, exactly as the receipt showed it.</summary>
    internal const int CurrencyCodeMaxLength = 32;

    /// <summary>A receipt or invoice reference.</summary>
    internal const int ReceiptReferenceMaxLength = 128;

    /// <summary>
    /// Provider confidence. It is transported metadata, never a monetary
    /// amount, so it declares its own precision rather than borrowing the
    /// monetary one — the two would then be impossible to tell apart later.
    /// </summary>
    internal const int ConfidencePrecision = 9;

    internal const int ConfidenceScale = 6;

    /// <summary>
    /// The scope columns every extraction table carries. Firm first, always:
    /// a tenant comparison that depends on column order is a tenant comparison
    /// somebody will get wrong.
    /// </summary>
    internal static void ConfigureScope<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<Guid>("FirmId").HasColumnType("uniqueidentifier");
        builder.Property<Guid>("CaseId").HasColumnType("uniqueidentifier");
        builder.Property<DateTimeOffset>("RetentionUntilUtc")
            .HasColumnType("datetimeoffset");
    }

    /// <summary>
    /// Ties every extraction row to the server-stored intake case it belongs to,
    /// firm first. Restrict, never cascade: taxpayer history is not something a
    /// delete elsewhere may quietly take with it.
    /// </summary>
    internal static void ConfigureCaseOwnership<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.HasOne<IntakeCaseEntity>()
            .WithMany()
            .HasForeignKey("FirmId", "CaseId")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExtractionVersionConfiguration :
    IEntityTypeConfiguration<ExtractionVersionEntity>
{
    public void Configure(EntityTypeBuilder<ExtractionVersionEntity> builder)
    {
        builder.ToTable("ExtractionVersions");

        // The version chain is the identity: one row per document per version,
        // so a second append of the same version loses on the primary key rather
        // than overwriting the first.
        builder.HasKey(entity =>
            new { entity.FirmId, entity.CaseId, entity.DocumentId, entity.Version });
        ExtractionSchema.ConfigureScope(builder);

        builder.Property(entity => entity.DocumentId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.Version).HasColumnType("int");
        builder.Property(entity => entity.ExtractionVersionId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.ParticipantId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.AutomatedClassification).HasConversion<int>();
        builder.Property(entity => entity.OverriddenClassification).HasConversion<int>();
        builder.Property(entity => entity.OverrideActorReference)
            .HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.OverriddenAtUtc).HasColumnType("datetimeoffset");
        builder.Property(entity => entity.EmployerDisplayName)
            .HasMaxLength(PersistedSchema.DisplayNameMaxLength);
        builder.Property(entity => entity.EmployeeDisplayName)
            .HasMaxLength(PersistedSchema.DisplayNameMaxLength);
        builder.Property(entity => entity.ProviderLabel)
            .HasMaxLength(ExtractionSchema.ProviderLabelMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation);
        builder.Property(entity => entity.Confidence)
            .HasPrecision(ExtractionSchema.ConfidencePrecision, ExtractionSchema.ConfidenceScale);
        builder.Property(entity => entity.AcceptanceState).HasConversion<int>();
        builder.Property(entity => entity.PrecedingVersionId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.RecordedAtUtc).HasColumnType("datetimeoffset");

        builder.HasIndex(entity =>
                new { entity.FirmId, entity.ExtractionVersionId })
            .IsUnique();
        ExtractionSchema.ConfigureCaseOwnership(builder);
    }
}

internal sealed class ExtractionBoxValueConfiguration :
    IEntityTypeConfiguration<ExtractionBoxValueEntity>
{
    public void Configure(EntityTypeBuilder<ExtractionBoxValueEntity> builder)
    {
        builder.ToTable("ExtractionBoxValues");
        builder.HasKey(entity => new
        {
            entity.FirmId,
            entity.CaseId,
            entity.DocumentId,
            entity.Version,
            entity.Ordinal,
        });
        ExtractionSchema.ConfigureScope(builder);

        builder.Property(entity => entity.DocumentId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.Version).HasColumnType("int");

        // Ordinal, not a set: a document that shows the same box twice must
        // still reach the engine's own duplicate-box rejection.
        builder.Property(entity => entity.Ordinal).HasColumnType("int");
        builder.Property(entity => entity.BoxCode)
            .HasMaxLength(PersistedSchema.BoxCodeMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation)
            .IsRequired();
        builder.Property(entity => entity.PrintedValue)
            .HasMaxLength(ExtractionSchema.PrintedValueMaxLength);

        builder.HasOne<ExtractionVersionEntity>()
            .WithMany()
            .HasForeignKey(entity => new
            {
                entity.FirmId,
                entity.CaseId,
                entity.DocumentId,
                entity.Version,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExtractionFieldOutcomeConfiguration :
    IEntityTypeConfiguration<ExtractionFieldOutcomeEntity>
{
    public void Configure(EntityTypeBuilder<ExtractionFieldOutcomeEntity> builder)
    {
        builder.ToTable("ExtractionFieldOutcomes");
        builder.HasKey(entity => new
        {
            entity.FirmId,
            entity.CaseId,
            entity.DocumentId,
            entity.Version,
            entity.Ordinal,
        });
        ExtractionSchema.ConfigureScope(builder);

        builder.Property(entity => entity.DocumentId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.Version).HasColumnType("int");
        builder.Property(entity => entity.Ordinal).HasColumnType("int");
        builder.Property(entity => entity.FieldKey)
            .HasMaxLength(ExtractionSchema.FieldKeyMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation)
            .IsRequired();
        builder.Property(entity => entity.OutcomeKind).HasConversion<int>();

        // Outcomes belong to whichever approved immutable version shape owns
        // this document/version. A document takes one classification path, so
        // the shared key is unambiguous. Case ownership keeps every row scoped
        // without inventing an eleventh table or a polymorphic foreign key.
        ExtractionSchema.ConfigureCaseOwnership(builder);
    }
}

internal sealed class MedicalEvidenceVersionConfiguration :
    IEntityTypeConfiguration<MedicalEvidenceVersionEntity>
{
    public void Configure(EntityTypeBuilder<MedicalEvidenceVersionEntity> builder)
    {
        builder.ToTable("MedicalEvidenceVersions");
        builder.HasKey(entity =>
            new { entity.FirmId, entity.CaseId, entity.DocumentId, entity.Version });
        ExtractionSchema.ConfigureScope(builder);

        builder.Property(entity => entity.DocumentId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.Version).HasColumnType("int");
        builder.Property(entity => entity.MedicalEvidenceVersionId)
            .HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.ParticipantId).HasColumnType("uniqueidentifier");

        // As printed, and stored as text: a date this feature parsed would be a
        // date this feature decided something about, and it decides nothing.
        builder.Property(entity => entity.ServiceDateAsPrinted)
            .HasMaxLength(ExtractionSchema.PrintedValueMaxLength);
        builder.Property(entity => entity.GrossAmountAsPrinted)
            .HasMaxLength(ExtractionSchema.PrintedValueMaxLength);

        // "Not stated" is its own column rather than a null a later reader could
        // quietly treat as the local currency.
        builder.Property(entity => entity.CurrencyIsKnown).HasColumnType("bit");
        builder.Property(entity => entity.CurrencyCodeAsPrinted)
            .HasMaxLength(ExtractionSchema.CurrencyCodeMaxLength);
        builder.Property(entity => entity.ProviderDisplayName)
            .HasMaxLength(PersistedSchema.DisplayNameMaxLength);
        builder.Property(entity => entity.PatientDisplayName)
            .HasMaxLength(PersistedSchema.DisplayNameMaxLength);
        builder.Property(entity => entity.ReceiptReference)
            .HasMaxLength(ExtractionSchema.ReceiptReferenceMaxLength);
        builder.Property(entity => entity.ProviderLabel)
            .HasMaxLength(ExtractionSchema.ProviderLabelMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation);
        builder.Property(entity => entity.Confidence)
            .HasPrecision(ExtractionSchema.ConfidencePrecision, ExtractionSchema.ConfidenceScale);
        builder.Property(entity => entity.AcceptanceState).HasConversion<int>();
        builder.Property(entity => entity.PrecedingVersionId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.RecordedAtUtc).HasColumnType("datetimeoffset");

        builder.HasIndex(entity =>
                new { entity.FirmId, entity.MedicalEvidenceVersionId })
            .IsUnique();
        ExtractionSchema.ConfigureCaseOwnership(builder);
    }
}

internal sealed class ExtractionCorrectionConfiguration :
    IEntityTypeConfiguration<ExtractionCorrectionEntity>
{
    public void Configure(EntityTypeBuilder<ExtractionCorrectionEntity> builder)
    {
        builder.ToTable("ExtractionCorrections");
        builder.HasKey(entity => new { entity.FirmId, entity.CaseId, entity.CorrectionId });
        ExtractionSchema.ConfigureScope(builder);

        builder.Property(entity => entity.CorrectionId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.DocumentId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.Field).HasConversion<int>();
        builder.Property(entity => entity.BoxCode)
            .HasMaxLength(PersistedSchema.BoxCodeMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation);

        // The only place an address may be stored, as opaque text the closed
        // field vocabulary identifies. There is no address column anywhere.
        builder.Property(entity => entity.PriorValue)
            .HasMaxLength(ExtractionSchema.PrintedValueMaxLength);
        builder.Property(entity => entity.CorrectedValue)
            .HasMaxLength(ExtractionSchema.PrintedValueMaxLength);
        builder.Property(entity => entity.ActorKind).HasConversion<int>();
        builder.Property(entity => entity.ActorReference).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.SuppliedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(entity => entity.PrecedingVersionId).HasColumnType("uniqueidentifier");

        builder.HasIndex(entity =>
            new { entity.FirmId, entity.CaseId, entity.DocumentId, entity.SuppliedAtUtc });
        ExtractionSchema.ConfigureCaseOwnership(builder);
    }
}

internal sealed class ExtractionClassificationOverrideConfiguration :
    IEntityTypeConfiguration<ExtractionClassificationOverrideEntity>
{
    public void Configure(EntityTypeBuilder<ExtractionClassificationOverrideEntity> builder)
    {
        builder.ToTable("ExtractionClassificationOverrides");
        builder.HasKey(entity => new { entity.FirmId, entity.CaseId, entity.OverrideId });
        ExtractionSchema.ConfigureScope(builder);

        builder.Property(entity => entity.OverrideId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.DocumentId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.AutomatedClassification).HasConversion<int>();
        builder.Property(entity => entity.OverriddenClassification).HasConversion<int>();
        builder.Property(entity => entity.ActorKind).HasConversion<int>();
        builder.Property(entity => entity.ActorReference).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.OverriddenAtUtc).HasColumnType("datetimeoffset");

        builder.HasIndex(entity =>
            new { entity.FirmId, entity.CaseId, entity.DocumentId, entity.OverriddenAtUtc });
        ExtractionSchema.ConfigureCaseOwnership(builder);
    }
}

internal sealed class ExtractionDuplicateCandidateConfiguration :
    IEntityTypeConfiguration<ExtractionDuplicateCandidateEntity>
{
    public void Configure(EntityTypeBuilder<ExtractionDuplicateCandidateEntity> builder)
    {
        builder.ToTable("ExtractionDuplicateCandidates");
        builder.HasKey(entity => new { entity.FirmId, entity.CaseId, entity.CandidateId });
        ExtractionSchema.ConfigureScope(builder);

        builder.Property(entity => entity.CandidateId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.DocumentId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.ComparedDocumentId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.DetectedAtUtc).HasColumnType("datetimeoffset");

        // One determination per ordered pair, within one firm and one case. The
        // comparison never crosses a firm, a case, or a participant.
        builder.HasIndex(entity => new
            {
                entity.FirmId,
                entity.CaseId,
                entity.DocumentId,
                entity.ComparedDocumentId,
            })
            .IsUnique();
        ExtractionSchema.ConfigureCaseOwnership(builder);
    }
}

internal sealed class ExtractionDuplicateResolutionConfiguration :
    IEntityTypeConfiguration<ExtractionDuplicateResolutionEntity>
{
    public void Configure(EntityTypeBuilder<ExtractionDuplicateResolutionEntity> builder)
    {
        builder.ToTable("ExtractionDuplicateResolutions");
        builder.HasKey(entity => new { entity.FirmId, entity.CaseId, entity.ResolutionId });
        ExtractionSchema.ConfigureScope(builder);

        builder.Property(entity => entity.ResolutionId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.CandidateId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.Resolution).HasConversion<int>();
        builder.Property(entity => entity.ActorKind).HasConversion<int>();
        builder.Property(entity => entity.ActorReference).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.ResolvedAtUtc).HasColumnType("datetimeoffset");

        // One decision per candidate. A second is refused by the database, not
        // merely by the caller.
        builder.HasIndex(entity => new { entity.FirmId, entity.CaseId, entity.CandidateId })
            .IsUnique();
        builder.HasOne<ExtractionDuplicateCandidateEntity>()
            .WithMany()
            .HasForeignKey(entity => new { entity.FirmId, entity.CaseId, entity.CandidateId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExtractionAcceptanceConfiguration :
    IEntityTypeConfiguration<ExtractionAcceptanceEntity>
{
    public void Configure(EntityTypeBuilder<ExtractionAcceptanceEntity> builder)
    {
        builder.ToTable("ExtractionAcceptances");
        builder.HasKey(entity => new { entity.FirmId, entity.CaseId, entity.AcceptanceId });
        ExtractionSchema.ConfigureScope(builder);

        builder.Property(entity => entity.AcceptanceId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.Scope).HasConversion<int>();
        builder.Property(entity => entity.DocumentId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.AcceptedVersionId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.ActorKind).HasConversion<int>();
        builder.Property(entity => entity.ActorReference).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.AcceptedAtUtc).HasColumnType("datetimeoffset");

        builder.HasIndex(entity =>
            new { entity.FirmId, entity.CaseId, entity.Scope, entity.AcceptedAtUtc });
        ExtractionSchema.ConfigureCaseOwnership(builder);
    }
}

internal sealed class ExtractionAuditEventConfiguration :
    IEntityTypeConfiguration<ExtractionAuditEventEntity>
{
    public void Configure(EntityTypeBuilder<ExtractionAuditEventEntity> builder)
    {
        builder.ToTable("ExtractionAuditEvents");
        builder.HasKey(entity => new { entity.FirmId, entity.CaseId, entity.EventId });
        ExtractionSchema.ConfigureScope(builder);

        builder.Property(entity => entity.EventId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.Sequence).HasColumnType("bigint");
        builder.Property(entity => entity.Kind).HasConversion<int>();
        builder.Property(entity => entity.ActorKind).HasConversion<int>();
        builder.Property(entity => entity.ActorReference).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.OccurredAtUtc).HasColumnType("datetimeoffset");

        // The sequence is unique within the case, so the audit trail has one
        // order and an appended event cannot quietly take an existing position.
        builder.HasIndex(entity => new { entity.FirmId, entity.CaseId, entity.Sequence })
            .IsUnique();
        ExtractionSchema.ConfigureCaseOwnership(builder);
    }
}
