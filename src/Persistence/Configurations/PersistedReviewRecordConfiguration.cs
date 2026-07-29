using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Models;

namespace Persistence.Configurations;

/// <summary>
/// Relational shape for one immutable review-package snapshot.
/// </summary>
internal sealed class PersistedReviewRecordConfiguration
    : IEntityTypeConfiguration<PersistedReviewRecord>
{
    public void Configure(EntityTypeBuilder<PersistedReviewRecord> builder)
    {
        builder.ToTable("PersistedReviewRecords");

        builder.HasKey(record => new { record.FirmId, record.RecordId });

        builder.Property(record => record.FirmId)
            .HasColumnType("uniqueidentifier");
        builder.Property(record => record.RecordId)
            .HasColumnType("uniqueidentifier");
        builder.Property(record => record.CaseId)
            .HasMaxLength(PersistedSchema.CaseIdMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation)
            .IsRequired();
        builder.Property(record => record.ParticipantId)
            .HasMaxLength(PersistedSchema.ParticipantIdMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation)
            .IsRequired();
        builder.Property(record => record.CreatedAtUtc)
            .HasColumnType("datetimeoffset");
        builder.Property(record => record.UpdatedAtUtc)
            .HasColumnType("datetimeoffset");
        builder.Property(record => record.RetentionUntilUtc)
            .HasColumnType("datetimeoffset")
            .HasComment(
                "Exclusive UTC retention boundary: the record is purge-eligible at or after this instant.");

        builder.HasIndex(record => new
            {
                record.FirmId,
                record.CaseId,
                record.ParticipantId,
                record.TaxYear,
                record.PackageVersion,
            })
            .IsUnique();

        builder.HasIndex(record => new { record.FirmId, record.RetentionUntilUtc });

        builder.HasMany(record => record.Slips)
            .WithOne()
            .HasForeignKey(slip => new { slip.FirmId, slip.RecordId })
            .HasPrincipalKey(record => new { record.FirmId, record.RecordId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(record => record.Slips)
            .HasField("_slips")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    public override string ToString() => nameof(PersistedReviewRecordConfiguration);
}
