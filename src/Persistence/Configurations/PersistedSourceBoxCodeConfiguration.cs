using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Models;

namespace Persistence.Configurations;

/// <summary>
/// Relational shape for one source-box provenance row.
/// </summary>
internal sealed class PersistedSourceBoxCodeConfiguration
    : IEntityTypeConfiguration<PersistedSourceBoxCode>
{
    public void Configure(EntityTypeBuilder<PersistedSourceBoxCode> builder)
    {
        builder.ToTable("PersistedSourceBoxCodes");

        builder.HasKey(code => new { code.FirmId, code.SourceBoxCodeId });

        builder.Property(code => code.FirmId)
            .HasColumnType("uniqueidentifier");
        builder.Property(code => code.SourceBoxCodeId)
            .HasColumnType("uniqueidentifier");
        builder.Property(code => code.LineEntryId)
            .HasColumnType("uniqueidentifier");
        builder.Property(code => code.BoxCode)
            .HasMaxLength(PersistedSchema.BoxCodeMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation)
            .IsRequired();

        builder.HasIndex(code => new { code.FirmId, code.LineEntryId, code.Ordinal })
            .IsUnique();
    }

    public override string ToString() => nameof(PersistedSourceBoxCodeConfiguration);
}
