using System.Globalization;
using DocumentIntelligence;
using ExtractionWorkflow.Models;
using TaxAI.AzureInfrastructure.Configuration;
using Tenancy.Models;

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

    /// <summary>
    /// Spec 016 T01: the only production provider and reader names the approved
    /// structure admits. Naming them is not supplying them — no adapter behind
    /// either name ships in T01, so production admission stays refused until the
    /// implementing slices register real, verified adapters.
    /// </summary>
    internal const string AzureDocumentIntelligenceProvider = "AzureDocumentIntelligence";
    internal const string AzureBlobSourceReader = "AzureBlobSourceReader";

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

    /// <summary>
    /// Spec 016 T01: the production structure of extraction configuration. The
    /// deterministic Development provider or the Development encrypted
    /// filesystem reader in a production profile is refused outright; any other
    /// value than the approved production names is a missing adapter, never a
    /// fallback. Provider bounds must still be finite and positive.
    /// </summary>
    internal static IReadOnlyList<ProductionAdmissionRefusal> ProductionShapeRefusals(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var refusals = new List<ProductionAdmissionRefusal>();
        var provider = configuration[ProviderSetting];
        var reader = configuration[ContentReaderSetting];

        if (string.Equals(provider, DeterministicProvider, StringComparison.Ordinal))
        {
            refusals.Add(ProductionAdmissionRefusal.DevelopmentProviderConfigured);
        }
        else if (!string.Equals(provider, AzureDocumentIntelligenceProvider, StringComparison.Ordinal))
        {
            refusals.Add(ProductionAdmissionRefusal.ProductionDocumentIntelligenceAdapterMissing);
        }

        if (string.Equals(reader, EncryptedIntakeContentReader, StringComparison.Ordinal))
        {
            refusals.Add(ProductionAdmissionRefusal.LocalSourceStoreConfigured);
        }
        else if (!string.Equals(reader, AzureBlobSourceReader, StringComparison.Ordinal))
        {
            refusals.Add(ProductionAdmissionRefusal.ProductionSourceReaderMissing);
        }

        if (!TryPositiveDuration(configuration[ProviderCallLimitSetting], out var callLimit) ||
            callLimit > ProductionConfigurationValidator.OperationDeadlineBound ||
            !TryPositiveLong(configuration[MaximumSourceBytesSetting], out _))
        {
            refusals.Add(ProductionAdmissionRefusal.ResilienceUnbounded);
        }

        return refusals;
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
