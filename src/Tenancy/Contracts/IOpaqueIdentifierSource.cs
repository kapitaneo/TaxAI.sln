namespace Tenancy.Contracts;

/// <summary>
/// Supplies the opaque internal identifiers a persisted record needs.
/// <para>
/// It is a port rather than a call to a GUID factory because Tenancy is
/// deterministic: the same inputs must always produce the same record, and a
/// module that mints its own random values is a module whose output cannot be
/// asserted. The implementation lives in the storage layer, where
/// non-determinism is expected.
/// </para>
/// <para>
/// Identifiers are opaque by construction — never derived from a taxpayer name,
/// a case identifier, a participant identifier, or anything else a caller
/// supplied.
/// </para>
/// </summary>
public interface IOpaqueIdentifierSource
{
    Guid NewIdentifier();
}
