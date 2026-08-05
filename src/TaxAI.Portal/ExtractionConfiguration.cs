using System.Globalization;
using DocumentIntelligence;
using ExtractionWorkflow.Models;

namespace TaxAI.Portal;

internal sealed record ExtractionRuntimeConfiguration(
    DocumentIntelligenceOptions Intelligence,
    ExtractionWorkflowOptions Workflow)
{
    public override string ToString() => nameof(ExtractionRuntimeConfiguration);
}

internal static class ExtractionConfiguration
{
    internal const string Prefix = "TaxAi:Extraction:";
    internal const string ProviderSetting = Prefix + "Provider";
    internal const string ContentReaderSetting = Prefix + "ContentReader";
    internal const string ProviderCallLimitSetting = Prefix + "ProviderCallLimit";
    internal const string MaximumSourceBytesSetting = Prefix + "MaximumSourceBytes";
    internal const string ProviderLabelSetting = Prefix + "ProviderLabel";

    internal const string DeterministicProvider = "Deterministic";
    internal const string EncryptedIntakeContentReader =
        "DevelopmentEncryptedIntakeContentStore";

    internal const string InvalidConfigurationMessage =
        "Document extraction configuration is missing or invalid.";

    internal const string NonDevelopmentRefusalMessage =
        "Document extraction is unavailable outside Development until an approved production provider and content reader are supplied.";

    internal static ExtractionRuntimeConfiguration Require(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (!string.Equals(
                environment.EnvironmentName,
                Environments.Development,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(NonDevelopmentRefusalMessage);
        }

        if (!string.Equals(
                configuration[ProviderSetting],
                DeterministicProvider,
                StringComparison.Ordinal) ||
            !string.Equals(
                configuration[ContentReaderSetting],
                EncryptedIntakeContentReader,
                StringComparison.Ordinal) ||
            !TryPositiveDuration(
                configuration[ProviderCallLimitSetting],
                out var providerCallLimit) ||
            !TryPositiveLong(
                configuration[MaximumSourceBytesSetting],
                out var maximumSourceBytes))
        {
            throw new InvalidOperationException(InvalidConfigurationMessage);
        }

        DocumentIntelligenceOptions intelligence;
        try
        {
            intelligence = DocumentIntelligenceOptions.RequireValid(
                new DocumentIntelligenceOptions
                {
                    MaximumSourceBytes = maximumSourceBytes,
                    ProviderLabel = configuration[ProviderLabelSetting],
                });
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(InvalidConfigurationMessage);
        }

        return new ExtractionRuntimeConfiguration(
            intelligence,
            new ExtractionWorkflowOptions
            {
                ProviderCallLimit = providerCallLimit,
            });
    }

    private static bool TryPositiveDuration(
        string? candidate,
        out TimeSpan duration) =>
        TimeSpan.TryParse(
            candidate,
            CultureInfo.InvariantCulture,
            out duration) &&
        duration > TimeSpan.Zero;

    private static bool TryPositiveLong(
        string? candidate,
        out long value) =>
        long.TryParse(
            candidate,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value) &&
        value > 0;
}
