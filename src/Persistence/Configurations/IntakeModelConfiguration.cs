using ClientIntake.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Persistence.Entities;
using Tenancy.Models;

namespace Persistence.Configurations;

internal sealed class IntakeCaseConfiguration :
    IEntityTypeConfiguration<IntakeCaseEntity>
{
    public void Configure(EntityTypeBuilder<IntakeCaseEntity> builder)
    {
        builder.ToTable(
            "IntakeCases",
            table => table.HasCheckConstraint(
                "CK_IntakeCases_TaxYear",
                "[TaxYear] = 2025"));
        builder.HasKey(entity => new { entity.FirmId, entity.CaseId });
        ConfigureScope(builder);
        builder.Property(entity => entity.PrimaryParticipantId)
            .HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.PrimaryTaxpayerDisplayName)
            .HasMaxLength(PersistedSchema.DisplayNameMaxLength)
            .IsRequired();
        builder.Property(entity => entity.TaxYear).HasColumnType("int");
        builder.Property(entity => entity.CaseStatus).HasConversion<int>();
        builder.Property(entity => entity.QuestionnaireVersion).HasColumnType("int");
        builder.Property(entity => entity.ConcurrencyVersion)
            .HasColumnType("bigint")
            .IsConcurrencyToken();
        builder.HasOne<Firm>()
            .WithMany()
            .HasForeignKey(entity => entity.FirmId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    internal static void ConfigureScope<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<Guid>("FirmId").HasColumnType("uniqueidentifier");
        builder.Property<Guid>("CaseId").HasColumnType("uniqueidentifier");
    }
}

internal sealed class IntakeExpectedItemConfiguration :
    IEntityTypeConfiguration<IntakeExpectedItemEntity>
{
    public void Configure(EntityTypeBuilder<IntakeExpectedItemEntity> builder)
    {
        builder.ToTable(
            "IntakeExpectedItems",
            table => table.HasCheckConstraint(
                "CK_IntakeExpectedItems_Category",
                "[Category] IN (0, 1)"));
        builder.HasKey(entity =>
            new { entity.FirmId, entity.CaseId, entity.ExpectedItemId });
        IntakeCaseConfiguration.ConfigureScope(builder);
        builder.Property(entity => entity.ExpectedItemId)
            .HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.Category).HasConversion<int>();
        builder.Property(entity => entity.Ordinal).HasColumnType("int");
        builder.HasIndex(entity =>
                new { entity.FirmId, entity.CaseId, entity.Category, entity.Ordinal })
            .IsUnique();
        builder.HasOne<IntakeCaseEntity>()
            .WithMany()
            .HasForeignKey(entity => new { entity.FirmId, entity.CaseId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IntakeLinkConfiguration :
    IEntityTypeConfiguration<IntakeLinkEntity>
{
    public void Configure(EntityTypeBuilder<IntakeLinkEntity> builder)
    {
        builder.ToTable("IntakeLinks");
        builder.HasKey(entity => new { entity.FirmId, entity.CaseId, entity.LinkId });
        IntakeCaseConfiguration.ConfigureScope(builder);
        builder.Property(entity => entity.LinkId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.CredentialDigest)
            .HasColumnType("binary(32)")
            .HasMaxLength(32)
            .IsRequired();
        ConfigureInstants(builder);
        builder.HasIndex(entity => entity.CredentialDigest).IsUnique();
        builder.HasIndex(entity => new { entity.FirmId, entity.CaseId })
            .IsUnique()
            .HasFilter("[RevokedAtUtc] IS NULL AND [SpentAtUtc] IS NULL");
        builder.HasOne<IntakeCaseEntity>()
            .WithMany()
            .HasForeignKey(entity => new { entity.FirmId, entity.CaseId })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureInstants(EntityTypeBuilder<IntakeLinkEntity> builder)
    {
        builder.Property(entity => entity.IssuedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(entity => entity.ExpiresAtUtc).HasColumnType("datetimeoffset");
        builder.Property(entity => entity.SpentAtUtc).HasColumnType("datetimeoffset");
        builder.Property(entity => entity.RevokedAtUtc).HasColumnType("datetimeoffset");
    }
}

internal sealed class IntakeClientSessionConfiguration :
    IEntityTypeConfiguration<IntakeClientSessionEntity>
{
    public void Configure(EntityTypeBuilder<IntakeClientSessionEntity> builder)
    {
        builder.ToTable("IntakeClientSessions");
        builder.HasKey(entity => new { entity.FirmId, entity.CaseId, entity.SessionId });
        IntakeCaseConfiguration.ConfigureScope(builder);
        builder.Property(entity => entity.SessionId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.LinkId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.SessionDigest)
            .HasColumnType("binary(32)")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(entity => entity.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(entity => entity.AbsoluteExpiresAtUtc).HasColumnType("datetimeoffset");
        builder.Property(entity => entity.IdleExpiresAtUtc).HasColumnType("datetimeoffset");
        builder.Property(entity => entity.RevokedAtUtc).HasColumnType("datetimeoffset");
        builder.HasIndex(entity => entity.SessionDigest).IsUnique();
        builder.HasOne<IntakeLinkEntity>()
            .WithMany()
            .HasForeignKey(entity =>
                new { entity.FirmId, entity.CaseId, entity.LinkId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IntakeAnswerVersionConfiguration :
    IEntityTypeConfiguration<IntakeAnswerVersionEntity>
{
    public void Configure(EntityTypeBuilder<IntakeAnswerVersionEntity> builder)
    {
        builder.ToTable(
            "IntakeAnswerVersions",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_IntakeAnswerVersions_Category",
                    "[Category] IN (0, 1)");
                table.HasCheckConstraint(
                    "CK_IntakeAnswerVersions_Answer",
                    "[Answer] IN (0, 1, 2)");
            });
        builder.HasKey(entity =>
            new { entity.FirmId, entity.CaseId, entity.Category, entity.Version });
        IntakeCaseConfiguration.ConfigureScope(builder);
        builder.Property(entity => entity.Category).HasConversion<int>();
        builder.Property(entity => entity.Version).HasColumnType("int");
        builder.Property(entity => entity.Answer).HasConversion<int>();
        builder.Property(entity => entity.ActorKind).HasConversion<int>();
        builder.Property(entity => entity.ActorReference).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.RecordedAtUtc).HasColumnType("datetimeoffset");
        builder.HasOne<IntakeCaseEntity>()
            .WithMany()
            .HasForeignKey(entity => new { entity.FirmId, entity.CaseId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IntakeDocumentConfiguration :
    IEntityTypeConfiguration<IntakeDocumentEntity>
{
    public void Configure(EntityTypeBuilder<IntakeDocumentEntity> builder)
    {
        builder.ToTable(
            "IntakeDocuments",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_IntakeDocuments_Category",
                    "[Category] IN (0, 1)");
                table.HasCheckConstraint(
                    "CK_IntakeDocuments_MediaKind",
                    "[MediaKind] IN (0, 1, 2)");
                table.HasCheckConstraint(
                    "CK_IntakeDocuments_ByteCount",
                    "[ByteCount] > 0");
            });
        builder.HasKey(entity =>
            new { entity.FirmId, entity.CaseId, entity.DocumentId });
        IntakeCaseConfiguration.ConfigureScope(builder);
        builder.Property(entity => entity.DocumentId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.ExpectedItemId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.Category).HasConversion<int>();
        builder.Property(entity => entity.Version).HasColumnType("int");
        builder.Property(entity => entity.ContentKey)
            .HasMaxLength(128)
            .UseCollation(PersistedSchema.IdentifierCollation)
            .IsRequired();
        builder.Property(entity => entity.MediaKind).HasConversion<int>();
        builder.Property(entity => entity.ByteCount).HasColumnType("bigint");
        builder.Property(entity => entity.Fingerprint)
            .HasColumnType("binary(32)")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(entity => entity.UploadedAtUtc).HasColumnType("datetimeoffset");
        builder.HasIndex(entity =>
                new { entity.FirmId, entity.CaseId, entity.Fingerprint })
            .IsUnique();
        builder.HasIndex(entity =>
                new
                {
                    entity.FirmId,
                    entity.CaseId,
                    entity.ExpectedItemId,
                    entity.Version,
                })
            .IsUnique();
        builder.HasOne<IntakeExpectedItemEntity>()
            .WithMany()
            .HasForeignKey(entity =>
                new { entity.FirmId, entity.CaseId, entity.ExpectedItemId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IntakeUploadAttemptConfiguration :
    IEntityTypeConfiguration<IntakeUploadAttemptEntity>
{
    public void Configure(EntityTypeBuilder<IntakeUploadAttemptEntity> builder)
    {
        builder.ToTable(
            "IntakeUploadAttempts",
            table => table.HasCheckConstraint(
                "CK_IntakeUploadAttempts_Outcome",
                "[Outcome] IN (0, 1, 2)"));
        builder.HasKey(entity =>
            new { entity.FirmId, entity.CaseId, entity.AttemptId });
        IntakeCaseConfiguration.ConfigureScope(builder);
        builder.Property(entity => entity.AttemptId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.ExpectedItemId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.Outcome).HasConversion<int>();
        builder.Property(entity => entity.AttemptedAtUtc).HasColumnType("datetimeoffset");
        builder.HasOne<IntakeExpectedItemEntity>()
            .WithMany()
            .HasForeignKey(entity =>
                new { entity.FirmId, entity.CaseId, entity.ExpectedItemId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IntakeSubmissionConfiguration :
    IEntityTypeConfiguration<IntakeSubmissionEntity>
{
    public void Configure(EntityTypeBuilder<IntakeSubmissionEntity> builder)
    {
        builder.ToTable("IntakeSubmissions");
        builder.HasKey(entity =>
            new { entity.FirmId, entity.CaseId, entity.Version });
        IntakeCaseConfiguration.ConfigureScope(builder);
        builder.Property(entity => entity.Version).HasColumnType("int");
        builder.Property(entity => entity.ActorKind).HasConversion<int>();
        builder.Property(entity => entity.ActorReference).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.SubmittedAtUtc).HasColumnType("datetimeoffset");
        builder.HasOne<IntakeCaseEntity>()
            .WithMany()
            .HasForeignKey(entity => new { entity.FirmId, entity.CaseId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IntakeSubmissionAnswerConfiguration :
    IEntityTypeConfiguration<IntakeSubmissionAnswerEntity>
{
    public void Configure(EntityTypeBuilder<IntakeSubmissionAnswerEntity> builder)
    {
        builder.ToTable("IntakeSubmissionAnswers");
        builder.HasKey(entity =>
            new
            {
                entity.FirmId,
                entity.CaseId,
                entity.SubmissionVersion,
                entity.Category,
            });
        IntakeCaseConfiguration.ConfigureScope(builder);
        builder.Property(entity => entity.SubmissionVersion).HasColumnType("int");
        builder.Property(entity => entity.Category).HasConversion<int>();
        builder.Property(entity => entity.AnswerVersion).HasColumnType("int");
        builder.HasOne<IntakeSubmissionEntity>()
            .WithMany()
            .HasForeignKey(entity =>
                new { entity.FirmId, entity.CaseId, Version = entity.SubmissionVersion })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IntakeAnswerVersionEntity>()
            .WithMany()
            .HasForeignKey(entity =>
                new
                {
                    entity.FirmId,
                    entity.CaseId,
                    entity.Category,
                    Version = entity.AnswerVersion,
                })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IntakeSubmissionDocumentConfiguration :
    IEntityTypeConfiguration<IntakeSubmissionDocumentEntity>
{
    public void Configure(EntityTypeBuilder<IntakeSubmissionDocumentEntity> builder)
    {
        builder.ToTable("IntakeSubmissionDocuments");
        builder.HasKey(entity =>
            new
            {
                entity.FirmId,
                entity.CaseId,
                entity.SubmissionVersion,
                entity.DocumentId,
            });
        IntakeCaseConfiguration.ConfigureScope(builder);
        builder.Property(entity => entity.SubmissionVersion).HasColumnType("int");
        builder.Property(entity => entity.DocumentId).HasColumnType("uniqueidentifier");
        builder.HasOne<IntakeSubmissionEntity>()
            .WithMany()
            .HasForeignKey(entity =>
                new { entity.FirmId, entity.CaseId, Version = entity.SubmissionVersion })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IntakeDocumentEntity>()
            .WithMany()
            .HasForeignKey(entity =>
                new { entity.FirmId, entity.CaseId, entity.DocumentId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IntakeReviewAssociationConfiguration :
    IEntityTypeConfiguration<IntakeReviewAssociationEntity>
{
    public void Configure(EntityTypeBuilder<IntakeReviewAssociationEntity> builder)
    {
        builder.ToTable("IntakeReviewAssociations");
        builder.HasKey(entity => new { entity.FirmId, entity.CaseId });
        IntakeCaseConfiguration.ConfigureScope(builder);
        builder.Property(entity => entity.RecordId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.AssociatedAtUtc).HasColumnType("datetimeoffset");
        builder.HasIndex(entity => new { entity.FirmId, entity.RecordId }).IsUnique();
        builder.HasOne<IntakeCaseEntity>()
            .WithMany()
            .HasForeignKey(entity => new { entity.FirmId, entity.CaseId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenancy.Models.PersistedReviewRecord>()
            .WithMany()
            .HasForeignKey(entity => new { entity.FirmId, entity.RecordId })
            .HasPrincipalKey(record => new { record.FirmId, record.RecordId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IntakeAuditEventConfiguration :
    IEntityTypeConfiguration<IntakeAuditEventEntity>
{
    public void Configure(EntityTypeBuilder<IntakeAuditEventEntity> builder)
    {
        builder.ToTable(
            "IntakeAuditEvents",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_IntakeAuditEvents_Kind",
                    "[Kind] BETWEEN 0 AND 12");
                table.HasCheckConstraint(
                    "CK_IntakeAuditEvents_ActorKind",
                    "[ActorKind] IN (0, 1)");
            });
        builder.HasKey(entity =>
            new { entity.FirmId, entity.CaseId, entity.EventId });
        IntakeCaseConfiguration.ConfigureScope(builder);
        builder.Property(entity => entity.EventId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.Sequence).HasColumnType("bigint");
        builder.Property(entity => entity.Kind).HasConversion<int>();
        builder.Property(entity => entity.ActorKind).HasConversion<int>();
        builder.Property(entity => entity.ActorReference).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.ExpectedItemId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.OccurredAtUtc).HasColumnType("datetimeoffset");
        builder.HasIndex(entity =>
                new { entity.FirmId, entity.CaseId, entity.Sequence })
            .IsUnique();
        builder.HasOne<IntakeCaseEntity>()
            .WithMany()
            .HasForeignKey(entity => new { entity.FirmId, entity.CaseId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
