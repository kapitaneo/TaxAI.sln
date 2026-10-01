using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Persistence.Services;

/// <summary>
/// Refuses absent or unsafe SQL Server connection configuration without
/// returning or echoing the supplied value.
/// <para>
/// Spec 016 T03 (FR-04, AC-048): transport must be encrypted with the server
/// certificate validated against the server name — no trust-server-certificate
/// bypass and no host-name override. Among Microsoft Entra modes only the
/// platform managed identity is accepted, and it never carries a password: a
/// mode that stores a secret, prompts a person, or walks a credential chain is
/// refused. SQL and Windows authentication remain available to the Development
/// and in-process verification hosts; the production profile validator
/// independently requires the managed identity with strict encryption.
/// </para>
/// <para>
/// Spec 017 T01 (D-4): the strict rule above is the default and the only rule
/// any deployable host can reach. One explicit opt-in, supplied by the
/// Development host or by the local migration tool and never read from the
/// environment here, lets a <b>loopback</b> data source present a certificate
/// that is not validated, because a local SQL Server instance has a self-signed
/// one. Every other rule still applies on that path: encryption stays explicit
/// and required, a host-name override is still refused, and the authentication
/// rules are unchanged.
/// </para>
/// </summary>
internal static class SqlConnectionStringGuard
{
    private const string MissingMessage =
        "A database connection string is required.";
    private const string InvalidMessage =
        "The database connection string is invalid.";
    private const string EncryptionMessage =
        "The database connection string must explicitly require encryption.";
    private const string CertificateMessage =
        "The database connection string must validate the server certificate.";
    private const string AuthenticationMessage =
        "The database connection string must not use a stored, interactive or chained credential.";

    public static string RequireValid(string? connectionString) =>
        RequireValid(connectionString, allowDevelopmentLoopbackCertificateTrust: false);

    public static string RequireValid(
        string? connectionString,
        bool allowDevelopmentLoopbackCertificateTrust)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(MissingMessage, nameof(connectionString));
        }

        SqlConnectionStringBuilder builder;
        DbConnectionStringBuilder explicitSettings;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
            explicitSettings = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString,
            };
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(InvalidMessage, nameof(connectionString));
        }

        if (!explicitSettings.ContainsKey("Encrypt") || !(bool)builder.Encrypt)
        {
            throw new ArgumentException(
                EncryptionMessage,
                nameof(connectionString));
        }

        var trustsLocalCertificate =
            allowDevelopmentLoopbackCertificateTrust &&
            IsLoopbackDataSource(builder.DataSource);

        if ((builder.TrustServerCertificate && !trustsLocalCertificate) ||
            !string.IsNullOrEmpty(builder.HostNameInCertificate))
        {
            throw new ArgumentException(
                CertificateMessage,
                nameof(connectionString));
        }

        var managedIdentity = builder.Authentication is
            SqlAuthenticationMethod.ActiveDirectoryManagedIdentity or
            SqlAuthenticationMethod.ActiveDirectoryMSI;

        if (builder.Authentication is not (
                SqlAuthenticationMethod.NotSpecified or
                SqlAuthenticationMethod.SqlPassword or
                SqlAuthenticationMethod.ActiveDirectoryManagedIdentity or
                SqlAuthenticationMethod.ActiveDirectoryMSI) ||
            (managedIdentity && (!string.IsNullOrEmpty(builder.Password) || builder.IntegratedSecurity)) ||
            builder.PersistSecurityInfo)
        {
            throw new ArgumentException(
                AuthenticationMessage,
                nameof(connectionString));
        }

        return connectionString;
    }

    /// <summary>
    /// True only for a data source on this machine: a LocalDB instance, or a
    /// server addressed by a loopback name or address. A protocol prefix, port,
    /// and instance name are removed before the host is compared, so none of
    /// them can carry a remote host past the comparison.
    /// </summary>
    private static bool IsLoopbackDataSource(string? dataSource)
    {
        var candidate = (dataSource ?? string.Empty).Trim();

        if (candidate.StartsWith(LocalDbPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return candidate.Length > LocalDbPrefix.Length &&
                   candidate.IndexOfAny(HostSeparators, LocalDbPrefix.Length) < 0;
        }

        foreach (var prefix in ProtocolPrefixes)
        {
            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate[prefix.Length..];
                break;
            }
        }

        var instance = candidate.IndexOf(InstanceSeparator);
        if (instance >= 0)
        {
            candidate = candidate[..instance];
        }

        var port = candidate.LastIndexOf(',');
        if (port >= 0)
        {
            candidate = candidate[..port];
        }

        candidate = candidate.Trim();

        return LoopbackHosts.Contains(candidate, StringComparer.OrdinalIgnoreCase);
    }

    private const string LocalDbPrefix = @"(localdb)\";

    private const char InstanceSeparator = '\\';

    private static readonly char[] HostSeparators = [InstanceSeparator, ',', ':', '/'];

    private static readonly string[] ProtocolPrefixes = ["tcp:", "np:", "lpc:"];

    private static readonly string[] LoopbackHosts =
        ["localhost", "127.0.0.1", "::1", "[::1]", ".", "(local)"];
}
