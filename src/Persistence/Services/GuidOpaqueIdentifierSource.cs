using Tenancy.Contracts;

namespace Persistence.Services;

/// <summary>
/// Mints opaque identifiers that carry no taxpayer or caller-derived meaning.
/// </summary>
internal sealed class GuidOpaqueIdentifierSource : IOpaqueIdentifierSource
{
    public Guid NewIdentifier() => Guid.NewGuid();

    /// <summary>Fixed type identity only; never a generated identifier.</summary>
    public override string ToString() => nameof(GuidOpaqueIdentifierSource);
}
