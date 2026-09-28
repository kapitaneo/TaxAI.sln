using System.Globalization;
using ClientIntake.Models;
using Tenancy.Models;

namespace TaxAI.Portal;

internal sealed record IntakeRateConfiguration(
    int PublicPermitLimit,
    TimeSpan PublicWindow,
    int SessionPermitLimit,
    TimeSpan SessionWindow,
    int AnswerPermitLimit,
    TimeSpan AnswerWindow,
    int UploadPermitLimit,
    TimeSpan UploadWindow,
    int UploadConcurrencyLimit,
    int SubmissionPermitLimit,
    TimeSpan SubmissionWindow);

internal sealed class IntakeRuntimeConfiguration
{
    internal IntakeRuntimeConfiguration(
        Uri publicOrigin,
        IntakeWorkflowOptions workflow,
        IntakeRateConfiguration rates,
        byte[] digestKey,
        byte[] fingerprintKey,
        byte[] contentEncryptionKey,
        string? developmentContentRoot)
    {
        PublicOrigin = publicOrigin;
        Workflow = workflow;
        Rates = rates;
        DigestKey = digestKey.ToArray();
        FingerprintKey = fingerprintKey.ToArray();
        ContentEncryptionKey = contentEncryptionKey.ToArray();
        DevelopmentContentRoot = developmentContentRoot;
    }

    internal Uri PublicOrigin { get; }
    internal IntakeWorkflowOptions Workflow { get; }
    internal IntakeRateConfiguration Rates { get; }
    internal byte[] DigestKey { get; }
    internal byte[] FingerprintKey { get; }
    internal byte[] ContentEncryptionKey { get; }
    internal string? DevelopmentContentRoot { get; }

    public override string ToString() => nameof(IntakeRuntimeConfiguration);
}

internal static class IntakeConfiguration
{
    internal const string Prefix = "TaxAi:Intake:";
    internal const string PublicOriginSetting = Prefix + "PublicOrigin";
    internal const string LinkLifetimeSetting = Prefix + "LinkLifetime";
    internal const string ClientSessionIdleLifetimeSetting =
        Prefix + "ClientSessionIdleLifetime";
    internal const string MaximumFileBytesSetting = Prefix + "MaximumFileBytes";
    internal const string MaximumFilesPerRequestSetting =
        Prefix + "MaximumFilesPerRequest";
    internal const string DigestKeySetting = Prefix + "DigestKey";
    internal const string FingerprintKeySetting = Prefix + "FingerprintKey";
    internal const string ContentEncryptionKeySetting =
        Prefix + "ContentEncryptionKey";
    internal const string DevelopmentContentRootSetting =
        Prefix + "DevelopmentContentRoot";
    internal const string PublicPermitLimitSetting = Prefix + "PublicPermitLimit";
    internal const string PublicWindowSetting = Prefix + "PublicWindow";
    internal const string SessionPermitLimitSetting = Prefix + "SessionPermitLimit";
    internal const string SessionWindowSetting = Prefix + "SessionWindow";
    internal const string AnswerPermitLimitSetting = Prefix + "AnswerPermitLimit";
    internal const string AnswerWindowSetting = Prefix + "AnswerWindow";
    internal const string UploadPermitLimitSetting = Prefix + "UploadPermitLimit";
    internal const string UploadWindowSetting = Prefix + "UploadWindow";
    internal const string UploadConcurrencyLimitSetting =
        Prefix + "UploadConcurrencyLimit";
    internal const string SubmissionPermitLimitSetting =
        Prefix + "SubmissionPermitLimit";
    internal const string SubmissionWindowSetting = Prefix + "SubmissionWindow";

    internal const string InvalidConfigurationMessage =
        "Client intake configuration is missing or invalid.";

    internal static IntakeRuntimeConfiguration Require(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (!TryOrigin(configuration[PublicOriginSetting], out var publicOrigin) ||
            !TryPositiveDuration(configuration[LinkLifetimeSetting], out var linkLifetime) ||
            !TryPositiveDuration(
                configuration[ClientSessionIdleLifetimeSetting],
                out var idleLifetime) ||
            !TryPositiveLong(
                configuration[MaximumFileBytesSetting],
                out var maximumFileBytes) ||
            !TryPositiveInt(
                configuration[MaximumFilesPerRequestSetting],
                out var maximumFilesPerRequest) ||
            !TryKey(configuration[DigestKeySetting], out var digestKey) ||
            !TryKey(configuration[FingerprintKeySetting], out var fingerprintKey) ||
            !TryContentEncryptionKey(
                configuration[ContentEncryptionKeySetting],
                environment,
                out var contentEncryptionKey) ||
            digestKey.AsSpan().SequenceEqual(fingerprintKey) ||
            digestKey.AsSpan().SequenceEqual(contentEncryptionKey) ||
            fingerprintKey.AsSpan().SequenceEqual(contentEncryptionKey) ||
            !TryRates(configuration, out var rates))
        {
            throw new InvalidOperationException(InvalidConfigurationMessage);
        }

        string? contentRoot = null;
        if (string.Equals(
                environment.EnvironmentName,
                Environments.Development,
                StringComparison.Ordinal))
        {
            contentRoot = RequireDevelopmentRoot(
                configuration[DevelopmentContentRootSetting],
                environment);
        }

        return new IntakeRuntimeConfiguration(
            publicOrigin!,
            new IntakeWorkflowOptions(
                linkLifetime,
                idleLifetime,
                maximumFileBytes,
                maximumFilesPerRequest),
            rates!,
            digestKey,
            fingerprintKey,
            contentEncryptionKey,
            contentRoot);
    }

    /// <summary>
    /// Spec 016 T01: the production shape of intake configuration.
    /// <para>
    /// The two Development-store settings describe the local encrypted
    /// filesystem content store. Production source content belongs in the
    /// approved private Canadian store under a Key Vault-wrapped envelope, so
    /// either setting in a production profile is a local-store refusal; it is
    /// never silently ignored. Every Spec 014 bound above is unchanged and still
    /// enforced by <see cref="Require"/>.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<ProductionAdmissionRefusal> ProductionShapeRefusals(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration[DevelopmentContentRootSetting] is not null ||
               configuration[ContentEncryptionKeySetting] is not null
            ? new[] { ProductionAdmissionRefusal.LocalSourceStoreConfigured }
            : Array.Empty<ProductionAdmissionRefusal>();
    }

    /// <summary>
    /// Spec 016 T01: the content-encryption key protects only the Development
    /// encrypted filesystem store, so it is required in Development and not
    /// required elsewhere. When supplied it is validated exactly as before and
    /// stays distinct from the digest and fingerprint keys.
    /// </summary>
    private static bool TryContentEncryptionKey(
        string? candidate,
        IWebHostEnvironment environment,
        out byte[] key)
    {
        if (candidate is null &&
            !string.Equals(
                environment.EnvironmentName,
                Environments.Development,
                StringComparison.Ordinal))
        {
            key = [];
            return true;
        }

        return TryKey(candidate, out key);
    }

    private static bool TryRates(
        IConfiguration configuration,
        out IntakeRateConfiguration? rates)
    {
        rates = null;
        if (!TryPositiveInt(
                configuration[PublicPermitLimitSetting],
                out var publicPermits) ||
            !TryPositiveDuration(
                configuration[PublicWindowSetting],
                out var publicWindow) ||
            !TryPositiveInt(
                configuration[SessionPermitLimitSetting],
                out var sessionPermits) ||
            !TryPositiveDuration(
                configuration[SessionWindowSetting],
                out var sessionWindow) ||
            !TryPositiveInt(
                configuration[AnswerPermitLimitSetting],
                out var answerPermits) ||
            !TryPositiveDuration(
                configuration[AnswerWindowSetting],
                out var answerWindow) ||
            !TryPositiveInt(
                configuration[UploadPermitLimitSetting],
                out var uploadPermits) ||
            !TryPositiveDuration(
                configuration[UploadWindowSetting],
                out var uploadWindow) ||
            !TryPositiveInt(
                configuration[UploadConcurrencyLimitSetting],
                out var uploadConcurrency) ||
            !TryPositiveInt(
                configuration[SubmissionPermitLimitSetting],
                out var submissionPermits) ||
            !TryPositiveDuration(
                configuration[SubmissionWindowSetting],
                out var submissionWindow))
        {
            return false;
        }

        rates = new IntakeRateConfiguration(
            publicPermits,
            publicWindow,
            sessionPermits,
            sessionWindow,
            answerPermits,
            answerWindow,
            uploadPermits,
            uploadWindow,
            uploadConcurrency,
            submissionPermits,
            submissionWindow);
        return true;
    }

    private static bool TryOrigin(string? candidate, out Uri? origin)
    {
        origin = null;
        if (candidate is null ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            string.IsNullOrEmpty(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            parsed.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            !string.Equals(candidate, parsed.AbsoluteUri, StringComparison.Ordinal))
        {
            return false;
        }

        origin = parsed;
        return true;
    }

    private static bool TryPositiveDuration(
        string? candidate,
        out TimeSpan duration) =>
        TimeSpan.TryParse(
            candidate,
            CultureInfo.InvariantCulture,
            out duration) &&
        duration > TimeSpan.Zero;

    private static bool TryPositiveInt(string? candidate, out int value) =>
        int.TryParse(
            candidate,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value) &&
        value > 0;

    private static bool TryPositiveLong(string? candidate, out long value) =>
        long.TryParse(
            candidate,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value) &&
        value > 0;

    private static bool TryKey(string? candidate, out byte[] key)
    {
        key = [];
        if (candidate is null || candidate.Length != 43)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (!(character is >= 'A' and <= 'Z' ||
                  character is >= 'a' and <= 'z' ||
                  character is >= '0' and <= '9' ||
                  character is '-' or '_'))
            {
                return false;
            }
        }

        try
        {
            var normalized = string.Concat(
                candidate.Replace('-', '+').Replace('_', '/'),
                "=");
            key = Convert.FromBase64String(normalized);
            return key.Length == 32 &&
                   string.Equals(Encode(key), candidate, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }

    private static string RequireDevelopmentRoot(
        string? candidate,
        IWebHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            !Path.IsPathFullyQualified(candidate))
        {
            throw new InvalidOperationException(InvalidConfigurationMessage);
        }

        var root = Path.GetFullPath(candidate);
        if (string.Equals(
                root,
                Path.GetPathRoot(root),
                StringComparison.OrdinalIgnoreCase) ||
            IsWithin(root, environment.WebRootPath))
        {
            throw new InvalidOperationException(InvalidConfigurationMessage);
        }

        return root;
    }

    private static bool IsWithin(string candidate, string? parent)
    {
        if (string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        var fullParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullCandidate = candidate
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase);
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
