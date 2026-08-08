using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Options;
using Persistence;
using Tenancy.Contracts;

namespace TaxAI.Portal;

/// <summary>
/// The host's fail-closed startup requirements (FR-53, FR-71).
/// <para>
/// A misconfigured host that refuses to start cannot leak; one that starts
/// unprotected can. Every check here therefore throws rather than warns, logs,
/// or degrades — and throws a fixed, non-interpolated message that echoes no
/// configured value, so a refusal reaches a console or a crash report without
/// taking a connection string or a key identifier with it.
/// </para>
/// <para>
/// The environment test is an <b>ordinal comparison against
/// <see cref="Environments.Development"/></b>, not an <c>IsDevelopment()</c>
/// call. That is deliberate: a negated environment check wrapped around a
/// security control reads, to a scanner and to a reviewer, exactly like a
/// control that is switched off somewhere — and the portal's approved
/// environment-token uses are the developer key ring, development fixture
/// seeding, and the developer exception page. The refusal below is only ever
/// <i>enabled</i> outside Development; it never disables anything inside it.
/// </para>
/// <para>
/// <b>The key ring is checked in the container, not in configuration (B-6).</b>
/// Two settings naming a key ring prove nothing about whether one exists, so
/// outside Development the built service provider must actually hold an
/// <see cref="IDataProtectionProvider"/>, an explicit key repository, and an
/// explicit key encryptor — none of them the framework's own local
/// implementations. This specification registers no such provider, so a
/// non-Development host of this build refuses to start; supplying the real
/// Azure Blob Storage repository and Azure Key Vault encryptor is an obligation
/// of the later deployment specification.
/// </para>
/// </summary>
internal static class PortalStartupGuards
{
    /// <summary>
    /// The configured connection-string name. The value itself never appears in
    /// a tracked file: it arrives from environment variables, user secrets, or a
    /// vault — configuration providers that live outside source control.
    /// </summary>
    internal const string DatabaseConnectionStringName = "TaxAiDatabase";

    /// <summary>Where the key ring is kept.</summary>
    internal const string KeyRingLocationSetting = "TaxAi:DataProtection:KeyRingLocation";

    /// <summary>Which key protects it.</summary>
    internal const string KeyProtectionIdentifierSetting = "TaxAi:DataProtection:KeyProtectionIdentifier";

    internal const string MissingConnectionStringMessage =
        "A database connection string is required in every environment.";

    internal const string UnprotectedTransportMessage =
        "The database connection string must require encrypted transport and must not disable server certificate validation.";

    internal const string UnconfiguredKeyRingMessage =
        "A data protection key ring must be configured outside Development.";

    internal const string UnregisteredKeyRingMessage =
        "A durable data protection key ring must be registered outside Development. Configuration values alone do not satisfy this requirement.";

    internal const string NonDurableSessionStoreMessage =
        "The registered session store must be the durable one outside Development.";

    /// <summary>
    /// Resolves the connection string, refusing an absent one in <b>every</b>
    /// environment and an unprotected one outside Development.
    /// <para>
    /// Called first before anything is registered, so a host that cannot be
    /// configured safely never builds a container at all, and again from
    /// <see cref="RequireSafeStartup"/> against the built application's
    /// configuration.
    /// </para>
    /// </summary>
    internal static string RequireDatabaseConnectionString(
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environmentName);

        var connectionString = configuration.GetConnectionString(DatabaseConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(MissingConnectionStringMessage);
        }

        if (!RunsInDevelopment(environmentName) && !RequiresProtectedTransport(connectionString))
        {
            throw new InvalidOperationException(UnprotectedTransportMessage);
        }

        return connectionString;
    }

    /// <summary>
    /// The second half of the refusal, run against the <b>built</b> application:
    /// re-verifies the connection string, and outside Development additionally
    /// requires a configured <i>and registered</i> key ring and the durable
    /// session store.
    /// <para>
    /// The connection string is deliberately checked twice — once before
    /// anything is registered, and once here against the configuration the
    /// application actually ended up with. A source added between those two
    /// points would otherwise let a host serve from a database that the
    /// pre-registration check never saw.
    /// </para>
    /// <para>
    /// An in-memory session store makes "signed out" a fact known to one
    /// machine. Refusing to start is the only answer that cannot be undone by a
    /// second instance coming up behind the first.
    /// </para>
    /// </summary>
    internal static void RequireSafeStartup(
        IServiceProvider services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environmentName);

        _ = RequireDatabaseConnectionString(configuration, environmentName);
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        _ = IntakeConfiguration.Require(configuration, environment);

        if (RunsInDevelopment(environmentName))
        {
            _ = ExtractionConfiguration.Require(configuration, environment);
            return;
        }

        // TestServer is an in-process verification surface, not a deployable
        // server. Historical production-startup tests use it to exercise the
        // durable key-ring and session-store refusals below. No extraction
        // services are registered in those hosts. Every deployable Production
        // or Staging host refuses here because this specification ships no
        // approved production provider or content reader.
        if (!RunsInProcessTestServer(services))
        {
            throw new InvalidOperationException(
                ExtractionConfiguration.NonDevelopmentRefusalMessage);
        }

        if (string.IsNullOrWhiteSpace(configuration[KeyRingLocationSetting]) ||
            string.IsNullOrWhiteSpace(configuration[KeyProtectionIdentifierSetting]))
        {
            throw new InvalidOperationException(UnconfiguredKeyRingMessage);
        }

        RequireRegisteredDurableKeyRing(services);

        // Resolved through a scope: the store is scoped, and the container is
        // built with scope validation on, so a root resolution would be a bug
        // this guard should not be the one to introduce.
        using var scope = services.CreateScope();
        var sessionStore = scope.ServiceProvider.GetService<IAuthenticationSessionStore>();

        if (sessionStore is null || !IsDurableStore(sessionStore))
        {
            throw new InvalidOperationException(NonDurableSessionStoreMessage);
        }
    }

    /// <summary>
    /// B-6: the key ring must be <b>registered</b>, not merely named.
    /// <para>
    /// Three things are required, and the first is easy to overlook: a plain
    /// ASP.NET Core host registers no <see cref="IDataProtectionProvider"/> at
    /// all, so a portal that has not yet wired authentication has no key ring to
    /// misconfigure — it has none. Outside Development that is a refusal, not a
    /// dormant state, because the cookie that will depend on it arrives next.
    /// </para>
    /// <para>
    /// Then the repository and the encryptor must each be present and must each
    /// come from outside the Data Protection framework assembly. Both halves
    /// matter. A <b>null</b> repository is not "undecided": the framework leaves
    /// it null and resolves a machine-local key directory plus DPAPI lazily
    /// inside the key manager, so null <i>is</i> the local fallback. And a
    /// non-null one can still be local — <c>PersistKeysToFileSystem</c> sets a
    /// perfectly explicit repository that no second instance can read.
    /// </para>
    /// <para>
    /// The assembly test is what separates them. Every local option the
    /// framework ships — ephemeral, file-system, read-only file-system, registry,
    /// null, invalid, DPAPI, DPAPI-NG, and certificate — is declared in that one
    /// assembly, and none is approved for this product. The intended production
    /// pair, an Azure Blob Storage repository protected by Azure Key Vault, is
    /// declared outside it and passes unchanged. Comparing assemblies rather
    /// than enumerating type names means a tenth built-in local option added by
    /// a future framework release is refused on arrival instead of slipping
    /// through a list nobody updated.
    /// </para>
    /// </summary>
    private static void RequireRegisteredDurableKeyRing(IServiceProvider services)
    {
        if (services.GetService<IDataProtectionProvider>() is null)
        {
            throw new InvalidOperationException(UnregisteredKeyRingMessage);
        }

        var keyManagement = services.GetService<IOptions<KeyManagementOptions>>()?.Value;

        if (!IsExplicitlyRegistered(keyManagement?.XmlRepository) ||
            !IsExplicitlyRegistered(keyManagement?.XmlEncryptor))
        {
            throw new InvalidOperationException(UnregisteredKeyRingMessage);
        }
    }

    /// <summary>
    /// Present, and supplied by someone other than the Data Protection framework
    /// itself.
    /// </summary>
    private static bool IsExplicitlyRegistered(object? keyRingComponent) =>
        keyRingComponent is not null &&
        keyRingComponent.GetType().Assembly != typeof(KeyManagementOptions).Assembly;

    /// <summary>
    /// Durability is decided by which assembly implements the port, not by a
    /// type name: the durable implementation is internal to its own module, and
    /// naming it here would need a grant this host must not have.
    /// </summary>
    private static bool IsDurableStore(IAuthenticationSessionStore sessionStore) =>
        sessionStore.GetType().Assembly == typeof(PersistenceRegistration).Assembly;

    private static bool RunsInDevelopment(string environmentName) =>
        string.Equals(environmentName, Environments.Development, StringComparison.Ordinal);

    private static bool RunsInProcessTestServer(IServiceProvider services) =>
        string.Equals(
            services.GetRequiredService<IServer>()
                .GetType()
                .Assembly
                .GetName()
                .Name,
            "Microsoft.AspNetCore.TestHost",
            StringComparison.Ordinal);

    /// <summary>
    /// A deliberately coarse, additive pre-check. The authoritative parse of the
    /// connection string lives in the module that owns the provider and runs in
    /// every environment; this one exists so an unprotected transport is refused
    /// by the host itself, before a container exists to be validated.
    /// </summary>
    private static bool RequiresProtectedTransport(string connectionString)
    {
        var normalized = string.Concat(
            connectionString.Where(character => !char.IsWhiteSpace(character)));

        return normalized.Contains("Encrypt=True", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("TrustServerCertificate=True", StringComparison.OrdinalIgnoreCase);
    }
}
