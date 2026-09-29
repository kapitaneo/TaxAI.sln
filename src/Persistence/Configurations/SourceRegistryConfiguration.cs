using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Persistence.Entities;
using Tenancy.Models;

namespace Persistence.Configurations;

/// <summary>
/// The source registry table (spec 016 T04). Firm/case scoped like every other
/// intake table: the key leads with the firm, the content key is unique within
/// its case, and a bound row must reference a committed intake document of the
/// same firm and case. Every key and index leads with the firm. The resource
/// identifier is a fresh random GUID, and a storage object is created only if it
/// does not already exist, so no second row can ever address an existing object.
/// </summary>
internal sealed class SourceRegistrationConfiguration :
    IEntityTypeConfiguration<SourceRegistrationEntity>
{
    public void Configure(EntityTypeBuilder<SourceRegistrationEntity> builder)
    {
        builder.ToTable(
            "SourceRegistrations",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_SourceRegistrations_State",
                    "[State] IN (1, 2, 3, 4)");
                table.HasCheckConstraint(
                    "CK_SourceRegistrations_Binding",
                    "([DocumentId] IS NULL AND [BoundAtUtc] IS NULL) OR " +
                    "([DocumentId] IS NOT NULL AND [BoundAtUtc] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_SourceRegistrations_BoundState",
                    "[State] <> 2 OR [DocumentId] IS NOT NULL");
                table.HasCheckConstraint(
                    "CK_SourceRegistrations_PurgeState",
                    "([State] IN (3, 4) AND [PurgeRecordedAtUtc] IS NOT NULL) OR " +
                    "([State] IN (1, 2) AND [PurgeRecordedAtUtc] IS NULL)");
            });
        builder.HasKey(entity => new { entity.FirmId, entity.CaseId, entity.ResourceId });
        IntakeCaseConfiguration.ConfigureScope(builder);
        builder.Property(entity => entity.ResourceId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.ContentKey)
            .HasMaxLength(128)
            .UseCollation(PersistedSchema.IdentifierCollation)
            .IsRequired();
        builder.Property(entity => entity.State).HasConversion<int>();
        builder.Property(entity => entity.StagedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(entity => entity.DocumentId).HasColumnType("uniqueidentifier");
        builder.Property(entity => entity.BoundAtUtc).HasColumnType("datetimeoffset");
        builder.Property(entity => entity.PurgeRecordedAtUtc).HasColumnType("datetimeoffset");

        builder.HasIndex(entity => new { entity.FirmId, entity.CaseId, entity.ContentKey })
            .IsUnique();
        builder.HasIndex(entity => new { entity.FirmId, entity.State, entity.StagedAtUtc });

        builder.HasOne<IntakeCaseEntity>()
            .WithMany()
            .HasForeignKey(entity => new { entity.FirmId, entity.CaseId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IntakeDocumentEntity>()
            .WithMany()
            .HasForeignKey(entity => new { entity.FirmId, entity.CaseId, entity.DocumentId })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
