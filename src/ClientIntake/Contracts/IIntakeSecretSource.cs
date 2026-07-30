using ClientIntake.Models;

namespace ClientIntake.Contracts;

/// <summary>
/// The single source of opaque identifiers, credentials, and keyed digests.
/// <para>
/// It is a port rather than a static helper so the workflows stay deterministic
/// functions of their arguments: randomness and key material are the only
/// non-deterministic inputs the intake workflow has, and both arrive here. A
/// workflow that called <c>Guid.NewGuid()</c> or <c>RandomNumberGenerator</c>
/// directly could not be tested for the properties that matter.
/// </para>
/// <para>
/// There is deliberately no member that stores, returns, or verifies a raw
/// credential. Digesting is one-way and keyed, and the purpose argument enforces
/// domain separation so a bootstrap-link digest can never be replayed as a
/// session digest.
/// </para>
/// </summary>
public interface IIntakeSecretSource
{
    /// <summary>Creates one opaque, non-sequential identifier.</summary>
    Guid CreateIdentifier();

    /// <summary>Creates one 256-bit credential from a cryptographic source.</summary>
    IntakeSecret CreateCredential();

    /// <summary>
    /// Derives the keyed digest that is stored and looked up. The purpose is
    /// mixed into the derivation, not appended to the output.
    /// </summary>
    IntakeTokenDigest Digest(
        IntakeSecret credential,
        IntakeTokenPurpose purpose);
}
