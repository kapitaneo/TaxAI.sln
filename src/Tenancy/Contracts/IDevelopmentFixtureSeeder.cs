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
/// Everything it writes is synthetic: no real name, address, email, phone,
/// account number, or employer identifier, and no SIN field of any kind.
/// </para>
/// </summary>
public interface IDevelopmentFixtureSeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}
