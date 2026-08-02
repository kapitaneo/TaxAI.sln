using CaseProcessing.Services;
using ClientIntake.Contracts;
using ClientIntake.Models;
using ClientIntake.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Persistence;
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
/// No <c>Add*</c> registration may appear outside this file, and this file calls
/// <b>exactly one registration extension per contributing module</b>. A module
/// that needs several things registered declares one extension and registers
/// them itself; that is what keeps an ORM package out of this project's file
/// while the container still gets a fully wired storage layer.
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
    /// <para>
    /// The storage layer arrives through its module's own single extension. The
    /// connection string is passed in rather than read here, because the host
    /// has already refused to start without one — see
    /// <see cref="PortalStartupGuards"/>.
    /// </para>
    /// <para>
    /// Identity is this host's own contribution and arrives the same way, behind
    /// one extension in <see cref="PortalIdentity"/>. Two extensions, two
    /// contributors: the storage module and the host itself.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddTaxAiModules(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        string databaseConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(databaseConnectionString);

        services.AddSingleton<IT4LineMapProvider, EmbeddedT4LineMapProvider>();
        services.AddSingleton<IT5LineMapProvider, EmbeddedT5LineMapProvider>();
        services.AddSingleton<T4CaseProcessor>();

        var intake = IntakeConfiguration.Require(configuration, environment);
        services.AddSingleton(intake);
        services.AddSingleton(intake.Workflow);
        services.AddSingleton<IIntakeCredentialSource>(
            _ => new CryptographicIntakeCredentialSource(intake.DigestKey));

        if (string.Equals(
                environment.EnvironmentName,
                Environments.Development,
                StringComparison.Ordinal))
        {
            services.AddSingleton<IIntakeContentStore>(
                _ => new DevelopmentEncryptedIntakeContentStore(
                    intake.DevelopmentContentRoot!,
                    intake.ContentEncryptionKey,
                    intake.FingerprintKey));
        }

        // The Blazor Web App foundation: static server rendering, and nothing
        // more.
        //
        // Deliberately NOT AddInteractiveServerComponents(). No component in this
        // project declares an interactive render mode, so Interactive Server would
        // provide no current functionality while opening a SignalR circuit
        // endpoint surface on the host that serves taxpayer data. Registering a
        // capability nothing uses is still surface, and this host's surface is
        // enumerated exactly.
        //
        // Interactive Server registration belongs to spec 014 T03, which
        // introduces the first genuinely interactive island — the adaptive
        // questionnaire and upload panel. It arrives with the component that
        // needs it.
        services.AddRazorComponents();

        services.AddTaxAiPersistence(databaseConnectionString);
        services.AddScoped<IAccountantIntakeWorkflow, AccountantIntakeWorkflow>();
        services.AddScoped<IClientIntakeWorkflow>(provider =>
            new ClientIntakeWorkflow(
                provider.GetRequiredService<IIntakeStore>(),
                provider.GetRequiredService<IIntakeContentStore>(),
                provider.GetRequiredService<IIntakeCredentialSource>(),
                provider.GetRequiredService<IntakeWorkflowOptions>()));
        services.AddPortalIdentity(configuration, environment);

        services.AddAntiforgery(options =>
        {
            options.FormFieldName = FirmEndpoints.AntiforgeryFieldName;
            options.Cookie.Name = FirmEndpoints.AntiforgeryCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddScoped<PortalAuthorization>();
        services.AddScoped<IAuthorizationHandler>(
            provider => provider.GetRequiredService<PortalAuthorization>());
        services.AddScoped<IAuthorizationMiddlewareResultHandler>(
            provider => provider.GetRequiredService<PortalAuthorization>());

        services.Configure<AuthorizationOptions>(options =>
        {
            options.AddPolicy(
                PortalAuthorization.BootstrapPolicy,
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(PortalAuthorization.BootstrapRequirement);
                });

            options.AddPolicy(
                PortalAuthorization.FirmScopedPolicy,
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(PortalAuthorization.FirmScopedRequirement);
                });
        });

        services.Configure<CookieAuthenticationOptions>(
            PortalIdentity.CookieScheme,
            PortalAuthorization.ConfigureSelectionIssuance);

        return services;
    }
}
