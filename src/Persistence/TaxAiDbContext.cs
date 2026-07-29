using Microsoft.EntityFrameworkCore;

namespace Persistence;

/// <summary>
/// The one database context for the modular monolith.
/// <para>
/// It exposes <strong>no</strong> set-typed member. A context that hands out a
/// queryable hands out the ability to write an unscoped query, and every tenant
/// guarantee in this system then depends on each caller remembering to add a
/// firm predicate. Repositories reach their sets internally instead, and each
/// one requires a trusted firm.
/// </para>
/// <para>
/// Schema comes only from repository-owned migrations. There is no
/// create-if-missing, no migrate-on-startup, and no schema mutation outside a
/// migration — a process that reshapes a taxpayer database on boot is a process
/// that can reshape it wrongly, at the worst moment, unattended.
/// </para>
/// </summary>
internal sealed class TaxAiDbContext : DbContext
{
    public TaxAiDbContext(DbContextOptions<TaxAiDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaxAiDbContext).Assembly);
    }

    /// <summary>Fixed type identity only; never context state or configuration.</summary>
    public override string ToString() => nameof(TaxAiDbContext);
}
