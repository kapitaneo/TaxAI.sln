using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Models;

namespace Persistence.Configurations;

/// <summary>
/// Relational shape for one populated, deliberately unmapped box code.
/// </summary>
internal sealed class PersistedUnmappedBoxCodeConfiguration
    : IEntityTypeConfiguration<PersistedUnmappedBoxCode>
{
    public void Configure(EntityTypeBuilder<PersistedUnmappedBoxCode> builder)
    {
        builder.ToTable("PersistedUnmappedBoxCodes");

        builder.HasKey(code => new { code.FirmId, code.UnmappedBoxCodeId });

        builder.Property(code => code.FirmId)
            .HasColumnType("uniqueidentifier");
        builder.Property(code => code.UnmappedBoxCodeId)
            .HasColumnType("uniqueidentifier");
        builder.Property(code => code.SlipId)
            .HasColumnType("uniqueidentifier");
        builder.Property(code => code.BoxCode)
            .HasMaxLength(PersistedSchema.BoxCodeMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation)
            .IsRequired();

        builder.HasIndex(code => new { code.FirmId, code.SlipId, code.Ordinal })
            .IsUnique();
    }

    public override string ToString() => nameof(PersistedUnmappedBoxCodeConfiguration);
}
