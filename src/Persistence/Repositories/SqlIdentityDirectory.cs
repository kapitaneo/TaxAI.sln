using Microsoft.EntityFrameworkCore;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Persistence.Repositories;

/// <summary>
/// Relational reads over the identity records that establish a trusted firm.
/// </summary>
internal sealed class SqlIdentityDirectory : IIdentityDirectory
{
    private readonly TaxAiDbContext _context;

    public SqlIdentityDirectory(TaxAiDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    public async Task<PortalUser?> FindUserAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(subject);

        var user = await _context.Set<PortalUser>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    EF.Functions.Collate(
                        candidate.Issuer,
                        PersistedSchema.IdentifierCollation) == issuer &&
                    EF.Functions.Collate(
                        candidate.Subject,
                        PersistedSchema.IdentifierCollation) == subject,
                cancellationToken);

        if (user is null)
        {
            return null;
        }

        return string.Equals(user.Issuer, issuer, StringComparison.Ordinal) &&
               string.Equals(user.Subject, subject, StringComparison.Ordinal)
            ? user
            : null;
    }

    public async Task<IReadOnlyList<FirmMembership>> FindActiveMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var memberships = await _context.Set<FirmMembership>()
            .AsNoTracking()
            .Where(membership =>
                membership.UserId == userId &&
                membership.IsActive)
            .OrderBy(membership => membership.MembershipId)
            .ToArrayAsync(cancellationToken);

        return Array.AsReadOnly(memberships);
    }

    public Task<FirmMembership?> FindMembershipAsync(
        Guid userId,
        Guid membershipId,
        CancellationToken cancellationToken) =>
        _context.Set<FirmMembership>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                membership =>
                    membership.UserId == userId &&
                    membership.MembershipId == membershipId,
                cancellationToken);

    public async Task<IReadOnlyList<Firm>> FindFirmsAsync(
        IReadOnlyList<Guid> firmIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firmIds);

        if (firmIds.Count == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Array.Empty<Firm>();
        }

        var requestedFirmIds = firmIds.Distinct().ToArray();
        var firms = await _context.Set<Firm>()
            .AsNoTracking()
            .Where(firm => requestedFirmIds.Contains(firm.FirmId))
            .OrderBy(firm => firm.FirmId)
            .ToArrayAsync(cancellationToken);

        return Array.AsReadOnly(firms);
    }

    /// <summary>Fixed type identity only; never identity-directory state.</summary>
    public override string ToString() => nameof(SqlIdentityDirectory);
}
