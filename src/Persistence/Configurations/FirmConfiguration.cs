using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Models;

namespace Persistence.Configurations;

/// <summary>
/// Relational shape for a firm. Its primary key is its tenant identifier.
/// </summary>
internal sealed class FirmConfiguration : IEntityTypeConfiguration<Firm>
{
    public void Configure(EntityTypeBuilder<Firm> builder)
    {
        builder.ToTable("Firms");

        builder.HasKey(firm => firm.FirmId);

        builder.Property(firm => firm.FirmId)
            .HasColumnType("uniqueidentifier");
        builder.Property(firm => firm.DisplayName)
            .HasMaxLength(PersistedSchema.FirmDisplayNameMaxLength)
            .IsRequired();
        builder.Property(firm => firm.CreatedAtUtc)
            .HasColumnType("datetimeoffset");
        builder.Property(firm => firm.UpdatedAtUtc)
            .HasColumnType("datetimeoffset");
    }

    public override string ToString() => nameof(FirmConfiguration);
}
