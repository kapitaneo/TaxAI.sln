namespace Tenancy.Contracts;

/// <summary>
/// Seeds obviously synthetic development fixtures.
/// <para>
/// Environment-gated and provably disabled outside Development. This feature
/// ships <strong>no</strong> default firm, no default administrator, no
/// production seed credential, and no hard-coded membership that exists in
/// production — a deployment of this specification alone therefore has no
/// identity rows at all, and no user can obtain a taxpayer-data session. That is
/// the intended, conservative outcome; secure provisioning is a later
/// specification.
/// </para>
/// <para>
/// Spec 017 T01: in Development only, the fixture additionally contains exactly
/// one synthetic user and exactly one active membership of the seeded firm, so
/// a local host can be signed in to without an identity provider. The user
/// carries no credential of any kind. Neither row exists in a database that
/// only had migrations applied, and nothing outside Development creates them.
/// </para>
/// <para>
/// Everything it writes is synthetic: no real name, address, email, phone,
/// account number, or employer identifier, and no SIN field of any kind.
/// </para>
/// </summary>
public interface IDevelopmentFixtureSeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}
