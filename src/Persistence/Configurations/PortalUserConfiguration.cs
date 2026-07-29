using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Models;

namespace Persistence.Configurations;

/// <summary>
/// Relational shape for the local OIDC identity record.
/// </summary>
internal sealed class PortalUserConfiguration : IEntityTypeConfiguration<PortalUser>
{
    public void Configure(EntityTypeBuilder<PortalUser> builder)
    {
        builder.ToTable("PortalUsers");

        builder.HasKey(user => user.UserId);

        builder.Property(user => user.UserId)
            .HasColumnType("uniqueidentifier");
        builder.Property(user => user.Issuer)
            .HasMaxLength(PersistedSchema.IssuerMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation)
            .IsRequired();
        builder.Property(user => user.Subject)
            .HasMaxLength(PersistedSchema.SubjectMaxLength)
            .UseCollation(PersistedSchema.IdentifierCollation)
            .IsRequired();
        builder.Property(user => user.CreatedAtUtc)
            .HasColumnType("datetimeoffset");
        builder.Property(user => user.UpdatedAtUtc)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(user => new { user.Issuer, user.Subject })
            .IsUnique();
    }

    public override string ToString() => nameof(PortalUserConfiguration);
}
