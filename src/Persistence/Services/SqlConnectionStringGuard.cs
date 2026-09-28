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

    public static string RequireValid(string? connectionString)
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

        if (builder.TrustServerCertificate ||
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
}
