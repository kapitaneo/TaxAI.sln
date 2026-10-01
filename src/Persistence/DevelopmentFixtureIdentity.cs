namespace Persistence;

/// <summary>
/// The external identity of the one synthetic development user (spec 017 T01).
/// <para>
/// These two values are the only fact the Development-only sign-in and the
/// development fixture seeder share. They are not a credential: knowing them
/// grants nothing, because no identity provider issues them and the only code
/// that presents them to the sign-in path is mapped in Development alone. A
/// database that only had migrations applied holds no row carrying them.
/// </para>
/// <para>
/// The issuer uses the reserved <c>.invalid</c> top-level domain, so it can
/// never name a real identity provider.
/// </para>
/// </summary>
public static class DevelopmentFixtureIdentity
{
    public const string Issuer = "https://development-fixture.taxai.invalid/";

    public const string Subject = "development-fixture-accountant";
}
