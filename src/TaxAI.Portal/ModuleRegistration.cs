using CaseProcessing.Services;
using TaxLogicEngine.Services;

namespace TaxAI.Portal;

/// <summary>
/// The composition root: the one and only place where modules are wired into the
/// container.
/// <para>
/// Wiring lives in one place or it lives in several. Before this existed, every
/// consumer of the T4 coordinator had to construct its own mapping provider at
/// its own call site — which is how a process ends up with two provider
/// instances and, eventually, two different answers about which mapping config
/// is authoritative.
/// </para>
/// <para>
/// No <c>Add*</c> registration may appear outside this file.
/// </para>
/// </summary>
internal static class ModuleRegistration
{
    /// <summary>
    /// Registers the tax modules' contracts with the container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every registration is a singleton. Both providers are stateless and
    /// parameterless, and the coordinator holds nothing but a provider, so there
    /// is no per-request state to scope and no reason to rebuild them.
    /// </para>
    /// <para>
    /// Deliberately absent: <c>IDocumentExtractionIngress</c>, whose signature
    /// spec 007 shipped without an implementation — registering one here would
    /// invent the untrusted-input intake path inside a hosting spec. Also absent
    /// are the T4/T5 line mappers, which <c>CaseProcessing</c> constructs
    /// internally rather than resolving from a container.
    /// </para>
    /// <para>
    /// The coordinator is registered so the container can prove it is
    /// constructible. Nothing resolves or invokes it: there is no endpoint that
    /// runs tax logic.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddTaxAiModules(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IT4LineMapProvider, EmbeddedT4LineMapProvider>();
        services.AddSingleton<IT5LineMapProvider, EmbeddedT5LineMapProvider>();
        services.AddSingleton<T4CaseProcessor>();

        return services;
    }
}
