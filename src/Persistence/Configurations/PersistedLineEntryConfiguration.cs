using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Models;

namespace Persistence.Configurations;

/// <summary>
/// Relational shape for one engine-produced CRA line entry.
/// </summary>
internal sealed class PersistedLineEntryConfiguration
    : IEntityTypeConfiguration<PersistedLineEntry>
{
    public void Configure(EntityTypeBuilder<PersistedLineEntry> builder)
    {
        builder.ToTable("PersistedLineEntries");

        builder.HasKey(entry => new { entry.FirmId, entry.LineEntryId });

        builder.Property(entry => entry.FirmId)
            .HasColumnType("uniqueidentifier");
        builder.Property(entry => entry.LineEntryId)
            .HasColumnType("uniqueidentifier");
        builder.Property(entry => entry.SlipId)
            .HasColumnType("uniqueidentifier");
        builder.Property(entry => entry.LineCode)
            .HasMaxLength(PersistedSchema.LineCodeMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation)
            .IsRequired();
        builder.Property(entry => entry.Amount)
            .HasPrecision(PersistedSchema.MonetaryPrecision, PersistedSchema.MonetaryScale);

        builder.HasIndex(entry => new { entry.FirmId, entry.SlipId, entry.Ordinal })
            .IsUnique();

        builder.HasMany(entry => entry.SourceBoxCodes)
            .WithOne()
            .HasForeignKey(code => new { code.FirmId, code.LineEntryId })
            .HasPrincipalKey(entry => new { entry.FirmId, entry.LineEntryId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(entry => entry.SourceBoxCodes)
            .HasField("_sourceBoxCodes")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    public override string ToString() => nameof(PersistedLineEntryConfiguration);
}
