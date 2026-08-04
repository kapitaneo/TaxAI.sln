using ExtractionWorkflow.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentIntelligence;

/// <summary>
/// This module's single registration extension, and the only supported way in.
/// <para>
/// Registration is <b>Development and test only</b>, and the host must say so
/// explicitly. The flag is an argument rather than an environment read on
/// purpose: a module that decides for itself which environment it is in is a
/// module whose refusal can be changed by a variable, and the deployment
/// refusal this repository carries is not something a variable should be able to
/// lift. No production document-intelligence provider ships in this
/// specification, so there is nothing else here to register.
/// </para>
/// <para>
/// Invalid configuration refuses at composition time. A host that cannot supply
/// a usable size limit and provider label does not start with a half-configured
/// extraction surface exposed.
/// </para>
/// </summary>
public static class DocumentIntelligenceRegistration
{
    /// <summary>
    /// Registers the deterministic in-process adapter as the single
    /// <see cref="IDocumentIntelligenceProvider"/>. The adapter holds no
    /// per-request state, so one instance serves every request.
    /// </summary>
    /// <exception cref="ArgumentNullException">The services or options are absent.</exception>
    /// <exception cref="ArgumentException">A configured value is missing, empty, or invalid.</exception>
    /// <exception cref="InvalidOperationException">The host is not a Development or test host.</exception>
    public static IServiceCollection AddDeterministicDocumentIntelligence(
        this IServiceCollection services,
        DocumentIntelligenceOptions options,
        bool isDevelopmentOrTestHost)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!isDevelopmentOrTestHost)
        {
            throw new InvalidOperationException(
                "The deterministic document intelligence adapter may be registered only in a Development or test host.");
        }

        var validated = DocumentIntelligenceOptions.RequireValid(options);

        services.AddSingleton<IDocumentIntelligenceProvider>(
            new DeterministicDocumentIntelligenceProvider(validated));

        return services;
    }
}
