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
    internal static DocumentIntelligence.Production.AzureDocumentIntelligenceOptions? ProductionOptions(
        IConfiguration configuration)
    {
        var profile = Production.ProductionConfiguration.Read(configuration).Options;
        if (!TryPositiveDuration(configuration[ProviderCallLimitSetting], out var callLimit) ||
            !TimeSpan.TryParse(profile.Resilience.OperationDeadline, CultureInfo.InvariantCulture, out var deadline) ||
            !TimeSpan.TryParse(profile.Resilience.ProbeDeadline, CultureInfo.InvariantCulture, out var cleanup) ||
            !int.TryParse(profile.Resilience.MaximumAttempts, NumberStyles.None, CultureInfo.InvariantCulture, out var attempts)) return null;
        try
        {
            return DocumentIntelligence.Production.AzureDocumentIntelligenceOptions.RequireValid(new()
            {
                Endpoint = profile.DocumentIntelligence.Resource.Endpoint,
                ResourceId = profile.DocumentIntelligence.Resource.ResourceId,
                Region = profile.DocumentIntelligence.Resource.Region,
                ManagedIdentityClientId = profile.ManagedIdentity.ClientId,
                ApiVersion = profile.DocumentIntelligence.ApiVersion,
                Model = profile.DocumentIntelligence.Model,
                Features = profile.DocumentIntelligence.Features,
                MaximumSourceBytes = ProductionMaximumSourceBytes(configuration),
                MaximumAttempts = attempts,
                OperationDeadline = deadline < callLimit ? deadline : callLimit,
                CleanupDeadline = cleanup,
            });
        }
        catch (ArgumentException) { return null; }
    }

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
    /// structure admits. Naming them is not supplying them: admission inspects
    /// the actual registrations. T04 registers the real Azure Blob source reader
    /// behind <see cref="AzureBlobSourceReader"/>; no production provider exists
    /// until T05, so production admission stays refused.
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

    /// <summary>
    /// Spec 016 T04: the production source bound the Azure source reader
    /// enforces, from the same setting the production shape validates. An
    /// absent or invalid value is <c>0</c>, which registers no reader; it is
    /// never replaced by a default.
    /// </summary>
    internal static long ProductionMaximumSourceBytes(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return TryPositiveLong(configuration[MaximumSourceBytesSetting], out var maximum)
            ? maximum
            : 0;
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
