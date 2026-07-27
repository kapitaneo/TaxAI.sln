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
    /// Declared in Wave 0 as the contract every later task codes against;
    /// implemented in Wave 1 (T02). Until then nothing calls it, so no start-up
    /// path can reach this throw.
    /// </remarks>
    internal static IServiceCollection AddTaxAiModules(this IServiceCollection services) =>
        throw new NotImplementedException();
}
