using ExtractionWorkflow.Contracts;

namespace ExtractionWorkflow.Tests.Lifecycle;

public sealed class ExtractionAvailabilityTests
{
    [Fact]
    public void AvailabilityPortCarriesNoTaxpayerSelectorOrInfrastructureType()
    {
        var method = Assert.Single(typeof(IExtractionAvailabilityGate).GetMethods());
        Assert.Equal(typeof(Task<bool>), method.ReturnType);
        Assert.Equal(typeof(CancellationToken), Assert.Single(method.GetParameters()).ParameterType);
        Assert.DoesNotContain(typeof(IExtractionAvailabilityGate).Assembly.GetTypes(), type =>
            type.IsClass && typeof(IExtractionAvailabilityGate).IsAssignableFrom(type));
    }

    [Fact]
    public void ExistingProviderSignaturesRemainExactlyTheThreeSourceOperations()
    {
        var methods = typeof(IDocumentIntelligenceProvider).GetMethods().OrderBy(method => method.Name).ToArray();
        Assert.Equal(new[] { "ClassifyAsync", "ExtractMedicalAsync", "ExtractT4Async" }, methods.Select(method => method.Name));
        Assert.All(methods, method => Assert.Equal(new[] { typeof(Stream), typeof(CancellationToken) },
            method.GetParameters().Select(parameter => parameter.ParameterType)));
    }
}
