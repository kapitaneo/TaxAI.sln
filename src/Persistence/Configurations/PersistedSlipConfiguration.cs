using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Models;

namespace Persistence.Configurations;

/// <summary>
/// Relational shape for one persisted slip, including its ordered child rows.
/// </summary>
internal sealed class PersistedSlipConfiguration : IEntityTypeConfiguration<PersistedSlip>
{
    public void Configure(EntityTypeBuilder<PersistedSlip> builder)
    {
        builder.ToTable("PersistedSlips");

        builder.HasKey(slip => new { slip.FirmId, slip.SlipId });

        builder.Property(slip => slip.FirmId)
            .HasColumnType("uniqueidentifier");
        builder.Property(slip => slip.SlipId)
            .HasColumnType("uniqueidentifier");
        builder.Property(slip => slip.RecordId)
            .HasColumnType("uniqueidentifier");
        builder.Property(slip => slip.DocumentReference)
            .HasColumnType("uniqueidentifier");
        builder.Property(slip => slip.SlipTypeCode)
            .HasMaxLength(PersistedSchema.SlipTypeCodeMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation)
            .IsRequired();
        builder.Property(slip => slip.TaxpayerDisplayName)
            .HasMaxLength(PersistedSchema.DisplayNameMaxLength)
            .IsRequired();
        builder.Property(slip => slip.EmployerDisplayName)
            .HasMaxLength(PersistedSchema.DisplayNameMaxLength)
            .IsRequired();

        builder.HasIndex(slip => new { slip.FirmId, slip.RecordId, slip.Ordinal })
            .IsUnique();

        builder.HasMany(slip => slip.BoxAmounts)
            .WithOne()
            .HasForeignKey(amount => new { amount.FirmId, amount.SlipId })
            .HasPrincipalKey(slip => new { slip.FirmId, slip.SlipId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(slip => slip.LineEntries)
            .WithOne()
            .HasForeignKey(entry => new { entry.FirmId, entry.SlipId })
            .HasPrincipalKey(slip => new { slip.FirmId, slip.SlipId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(slip => slip.UnmappedBoxCodes)
            .WithOne()
            .HasForeignKey(code => new { code.FirmId, code.SlipId })
            .HasPrincipalKey(slip => new { slip.FirmId, slip.SlipId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(slip => slip.BoxAmounts)
            .HasField("_boxAmounts")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(slip => slip.LineEntries)
            .HasField("_lineEntries")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(slip => slip.UnmappedBoxCodes)
            .HasField("_unmappedBoxCodes")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    public override string ToString() => nameof(PersistedSlipConfiguration);
}
