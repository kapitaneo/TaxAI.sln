using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Models;

namespace Persistence.Configurations;

/// <summary>
/// Relational shape for the durable, server-side session of record.
/// </summary>
internal sealed class AuthenticationSessionConfiguration
    : IEntityTypeConfiguration<AuthenticationSession>
{
    public void Configure(EntityTypeBuilder<AuthenticationSession> builder)
    {
        builder.ToTable("AuthenticationSessions");

        builder.HasKey(session => session.SessionId);

        builder.Property(session => session.SessionId)
            .HasColumnType("uniqueidentifier");
        builder.Property(session => session.UserId)
            .HasColumnType("uniqueidentifier");
        builder.Property(session => session.ActiveFirmId)
            .HasColumnType("uniqueidentifier");
        builder.Property(session => session.CreatedAtUtc)
            .HasColumnType("datetimeoffset");
        builder.Property(session => session.LastSeenAtUtc)
            .HasColumnType("datetimeoffset");
        builder.Property(session => session.IdleExpiresAtUtc)
            .HasColumnType("datetimeoffset");
        builder.Property(session => session.AbsoluteExpiresAtUtc)
            .HasColumnType("datetimeoffset");
        builder.Property(session => session.RevokedAtUtc)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(session => new { session.UserId, session.RevokedAtUtc });
        builder.HasIndex(session => session.ActiveFirmId);

        builder.HasOne<PortalUser>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Firm>()
            .WithMany()
            .HasForeignKey(session => session.ActiveFirmId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public override string ToString() => nameof(AuthenticationSessionConfiguration);
}
