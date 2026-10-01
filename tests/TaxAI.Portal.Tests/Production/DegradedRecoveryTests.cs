using System.Net;
using System.Reflection;
using DocumentIntelligence;
using ExtractionWorkflow.Contracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxAI.AzureInfrastructure.Configuration;
using TaxAI.Portal.Tests.EndToEnd;

namespace TaxAI.Portal.Tests.Production;

/// <summary>
/// Spec 016 T06 — the approved degraded recovery scope (FR-11 Q-A2, FR-14;
/// AC-107, AC-155, AC-156, AC-157, AC-165).
/// <para>
/// Recovered application service with Canada Central Document Intelligence
/// unavailable: authentication, already-authorized reads and operations that
/// need no extraction keep working through the real portal; every new
/// extraction fails closed with the fixed safe response after scope checks;
/// and no configuration can substitute another region, provider or
/// Development fallback.
/// </para>
/// </summary>
[Collection("Extraction network observation")]
public sealed class DegradedRecoveryTests
{
    [Fact] // AC-155, AC-156, AC-157
    public async Task RecoveredServiceAuthenticatesReadsAndOperatesWhileExtractionIsUnavailable()
    {
        var gate = new ProviderAvailabilityTests.MutableAvailability();
        await using var world = ExtractionPortalWorld.Create(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IExtractionAvailabilityGate>();
            services.AddSingleton<IExtractionAvailabilityGate>(gate);
        }));
        var seeded = await world.SeedFirmOneAsync();

        // AC-155: the existing authentication contract.
        using var accountant = world.Intake.FirmOne.CreateAnonymousClient();
        await world.Intake.FirmOne.SignInAsync(accountant);

        // AC-156: the existing authorized projection.
        var read = await ExtractionPortalHttp.GetAsync(accountant, $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.FirstT4DocumentId:D}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        // AC-157: an operation that needs no Document Intelligence.
        var created = await IntakeFlow.CreateCaseAsync(accountant, expectedT4Count: 1, expectedMedicalCount: 0);
        Assert.NotNull(created);

        // New extraction fails closed, with the fixed safe indication.
        var refused = await ExtractionPortalHttp.ExtractAsync(accountant, seeded.CaseId, seeded.FirstT4DocumentId);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal("Document extraction is temporarily unavailable.", refused.Body);
        Assert.DoesNotContain(ExtractionPortalWorld.PlantedSin, refused.All, StringComparison.Ordinal);
        Assert.DoesNotContain("cognitiveservices", refused.All, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("canadaeast", refused.All, StringComparison.OrdinalIgnoreCase);

        // Extraction resumes only when the approved provider boundary is valid again.
        gate.Available = true;
        Assert.Equal(HttpStatusCode.OK, (await ExtractionPortalHttp.ExtractAsync(accountant, seeded.CaseId, seeded.FirstT4DocumentId)).StatusCode);
    }

    [Fact] // AC-107, AC-165: the recovery profile keeps the one Canada Central provider.
    public void TheRecoveryProfileRegistersOnlyTheCanadaCentralProvider()
    {
        var settings = ProductionStartupTests.ProductionProfile();
        settings["TaxAi:Production:Recovery:Profile"] = "RegionalRecovery";
        settings["TaxAi:Production:Sql:Region"] = "canadaeast";
        settings["TaxAi:Production:SourceStorage:Region"] = "canadaeast";
        settings["TaxAi:Extraction:Provider"] = "AzureDocumentIntelligence";
        settings["TaxAi:Extraction:ContentReader"] = "AzureBlobSourceReader";
        settings["TaxAi:Extraction:ProviderCallLimit"] = "00:01:00";
        settings["TaxAi:Extraction:MaximumSourceBytes"] = "1048576";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var options = ProductionConfigurationRead(configuration);
        Assert.Empty(new ProductionConfigurationValidator().Validate(options, ProductionStartupTests.ProductionConnectionString).Refusals);
        Assert.True(ProductionConfigurationValidator.IsRegionalRecovery(options));
        Assert.Equal("canadacentral", options.DocumentIntelligence.Resource.Region);
        Assert.Equal("canadaeast", options.Sql.Region);

        // Moving the provider anywhere, as part of recovery, is refused.
        foreach (var region in new[] { "canadaeast", "eastus", "westeurope" })
        {
            settings["TaxAi:Production:DocumentIntelligence:Region"] = region;
            var moved = ProductionConfigurationRead(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            Assert.Contains(
                Tenancy.Models.ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid,
                new ProductionConfigurationValidator().Validate(moved, ProductionStartupTests.ProductionConnectionString).Refusals);
        }
    }

    [Fact] // AC-165: there is no recovery switch, fallback provider or bypass setting.
    public void NoRecoverySettingNamesAFallbackBypassOrSecondProvider()
    {
        var names = (IReadOnlyList<string>)typeof(Program).Assembly
            .GetType("TaxAI.Portal.Production.ProductionConfiguration", throwOnError: true)!
            .GetField("SettingNames", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        var recovery = names.Where(name => name.Contains(":Recovery:", StringComparison.Ordinal)).ToArray();

        Assert.Equal(14, recovery.Length);
        Assert.DoesNotContain(recovery, name =>
            name.Contains("Bypass", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Fallback", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Skip", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Enable", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DocumentIntelligence", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Provider", StringComparison.OrdinalIgnoreCase));
    }

    private static AzureInfrastructureOptions ProductionConfigurationRead(IConfiguration configuration)
    {
        var snapshot = typeof(Program).Assembly
            .GetType("TaxAI.Portal.Production.ProductionConfiguration", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { configuration })!;
        return (AzureInfrastructureOptions)snapshot.GetType()
            .GetProperty("Options", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(snapshot)!;
    }
}
