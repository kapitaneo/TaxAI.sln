using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Models;

namespace Persistence.Configurations;

/// <summary>
/// Relational shape for one active-or-disabled firm membership.
/// </summary>
internal sealed class FirmMembershipConfiguration
    : IEntityTypeConfiguration<FirmMembership>
{
    public void Configure(EntityTypeBuilder<FirmMembership> builder)
    {
        builder.ToTable("FirmMemberships");

        builder.HasKey(membership => membership.MembershipId);

        builder.Property(membership => membership.MembershipId)
            .HasColumnType("uniqueidentifier");
        builder.Property(membership => membership.FirmId)
            .HasColumnType("uniqueidentifier");
        builder.Property(membership => membership.UserId)
            .HasColumnType("uniqueidentifier");
        builder.Property(membership => membership.CreatedAtUtc)
            .HasColumnType("datetimeoffset");
        builder.Property(membership => membership.UpdatedAtUtc)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(membership => new { membership.FirmId, membership.UserId })
            .IsUnique();
        builder.HasIndex(membership => new
            {
                membership.UserId,
                membership.IsActive,
                membership.FirmId,
            });

        builder.HasOne<PortalUser>()
            .WithMany()
            .HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Firm>()
            .WithMany()
            .HasForeignKey(membership => membership.FirmId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public override string ToString() => nameof(FirmMembershipConfiguration);
}
