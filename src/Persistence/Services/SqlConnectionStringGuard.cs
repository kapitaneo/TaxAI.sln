using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Persistence.Services;

/// <summary>
/// Refuses absent or unsafe SQL Server connection configuration without
/// returning or echoing the supplied value.
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

        if (builder.TrustServerCertificate)
        {
            throw new ArgumentException(
                CertificateMessage,
                nameof(connectionString));
        }

        return connectionString;
    }
}
