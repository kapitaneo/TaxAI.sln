using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Models;

namespace Persistence.Configurations;

/// <summary>
/// Relational shape for one engine-produced box amount.
/// </summary>
internal sealed class PersistedBoxAmountConfiguration
    : IEntityTypeConfiguration<PersistedBoxAmount>
{
    public void Configure(EntityTypeBuilder<PersistedBoxAmount> builder)
    {
        builder.ToTable("PersistedBoxAmounts");

        builder.HasKey(amount => new { amount.FirmId, amount.BoxAmountId });

        builder.Property(amount => amount.FirmId)
            .HasColumnType("uniqueidentifier");
        builder.Property(amount => amount.BoxAmountId)
            .HasColumnType("uniqueidentifier");
        builder.Property(amount => amount.SlipId)
            .HasColumnType("uniqueidentifier");
        builder.Property(amount => amount.BoxCode)
            .HasMaxLength(PersistedSchema.BoxCodeMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation)
            .IsRequired();
        builder.Property(amount => amount.Amount)
            .HasPrecision(PersistedSchema.MonetaryPrecision, PersistedSchema.MonetaryScale);

        builder.HasIndex(amount => new { amount.FirmId, amount.SlipId, amount.Ordinal })
            .IsUnique();
    }

    public override string ToString() => nameof(PersistedBoxAmountConfiguration);
}
