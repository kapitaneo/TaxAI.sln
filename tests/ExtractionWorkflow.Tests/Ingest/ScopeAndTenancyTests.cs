using System.Reflection;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using Tenancy.Models;

namespace ExtractionWorkflow.Tests.Ingest;

/// <summary>
/// Spec 015 T04 — the firm is server-derived, the scope is verified against
/// server-stored state before a byte is read, and every refusal looks the same.
/// </summary>
public sealed class ScopeAndTenancyTests
{
    [Fact]
    public async Task AForeignFirmDocumentAndAMissingDocumentAreTheSameRefusal()
    {
        var foreignFirm = await IngestHarness.Create()
            .ExtractAsync(firm: IngestHarness.OtherFirm);

        var missingDocument = await IngestHarness.Create()
            .ExtractAsync(documentId: new Guid("99999999-9999-9999-9999-999999999999"));

        var missingCase = await IngestHarness.Create()
            .ExtractAsync(caseId: new Guid("88888888-8888-8888-8888-888888888888"));

        // Same code, same absent value, same rendering. Nothing distinguishes
        // the three, so none can be used to learn that a resource exists.
        foreach (var refusal in new[] { foreignFirm, missingDocument, missingCase })
        {
            Assert.Equal(ExtractionResultCode.NotFound, refusal.Code);
            Assert.Null(refusal.Value);
            Assert.Equal(foreignFirm.ToString(), refusal.ToString());
        }
    }

    [Fact]
    public async Task AForeignFirmRequestReadsNoContentAndCallsNoProvider()
    {
        var harness = IngestHarness.Create();

        var result = await harness.ExtractAsync(firm: IngestHarness.OtherFirm);

        Assert.Equal(ExtractionResultCode.NotFound, result.Code);
        Assert.Equal(0, harness.Content.OpenCallCount);
        Assert.False(harness.Provider.ClassificationRequested);
        Assert.Equal(0, harness.Store.WriteCount);
    }

    [Fact]
    public async Task ADocumentBelongingToAnotherCaseIsNeverReached()
    {
        var harness = IngestHarness.Create();

        var result = await harness.ExtractAsync(
            documentId: new Guid("77777777-7777-7777-7777-777777777777"));

        Assert.Equal(ExtractionResultCode.NotFound, result.Code);
        Assert.Equal(0, harness.Content.OpenCallCount);
        Assert.Equal(0, harness.Store.WriteCount);
    }

    [Fact]
    public async Task TheContentKeyComesFromTheStoredCaseAndAMismatchReadsNothing()
    {
        // The stored case names one key; the reader honours only that key with
        // this firm, case, and document. A workflow that accepted a key from a
        // caller would be able to open a document it was never given.
        var harness = IngestHarness.Create(contentKey: "the-key-the-server-stored");

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);

        // And with the stored case naming a key the reader does not recognize,
        // the read fails closed with the same empty refusal.
        var mismatched = IngestHarness.CreateWithMismatchedContentKey();
        var refused = await mismatched.ExtractAsync();

        Assert.Equal(ExtractionResultCode.NotFound, refused.Code);
        Assert.Equal(0, mismatched.Store.WriteCount);
    }

    [Fact]
    public void NoCommandOrViewCanCarryAScopeACallerSupplied()
    {
        // The firm never appears on a command: it arrives as a TrustedFirmId
        // argument, which cannot be produced from a route, query, form, header,
        // or cookie value without a visible conversion.
        foreach (var type in new[]
                 {
                     typeof(ExtractDocumentCommand),
                     typeof(ExtractionWorkflowOptions),
                 })
        {
            Assert.DoesNotContain(
                type.GetProperties(),
                property => property.Name.Contains("Firm", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void EveryTaxpayerDataMemberTakesATrustedFirmWithNoOmittingOverload()
    {
        foreach (var contract in new[]
                 {
                     typeof(IExtractionIngestWorkflow),
                     typeof(IExtractionStore),
                     typeof(IExtractionContentReader),
                 })
        {
            foreach (var member in contract.GetMethods())
            {
                var parameters = member.GetParameters();

                Assert.True(
                    parameters.Length > 0 &&
                    parameters[0].ParameterType == typeof(TrustedFirmId),
                    $"{contract.Name}.{member.Name} does not take a trusted firm first.");

                Assert.All(parameters, parameter => Assert.False(parameter.IsOptional));
            }
        }
    }

    [Fact]
    public void TheIngestContractDeclaresNoMemberABackgroundInitiatorCouldUse()
    {
        // Every member takes the actor reference or the trusted firm explicitly,
        // and none schedules, enqueues, or polls. There is nothing here a hosted
        // service, timer, or queue consumer could call without an accountant's
        // firm and an accountant's actor reference.
        var names = typeof(IExtractionIngestWorkflow)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        Assert.All(
            names,
            name => Assert.DoesNotContain(
                name,
                new[] { "StartAsync", "StopAsync", "ExecuteAsync", "EnqueueAsync", "PollAsync" }));

        var mutation = typeof(IExtractionIngestWorkflow)
            .GetMethod(nameof(IExtractionIngestWorkflow.ExtractAsync))!;

        Assert.Contains(
            mutation.GetParameters(),
            parameter => parameter.ParameterType == typeof(Guid) &&
                         parameter.Name == "accountantActorReference");
    }

    [Fact]
    public void TheT04IngestContractContainsOnlyTheOperationT04Consumes()
    {
        Assert.Equal(
            new[] { nameof(IExtractionIngestWorkflow.ExtractAsync) },
            typeof(IExtractionIngestWorkflow)
                .GetMethods()
                .Select(method => method.Name)
                .ToArray());
    }

    [Fact]
    public void TheWorkflowNamesNoClockEnvironmentCurrentUserOrHttpContext()
    {
        var assembly = typeof(ExtractionResultCode).Assembly;

        var referenced = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.DoesNotContain(
            referenced,
            name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                    name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                    name.Equals("Persistence", StringComparison.Ordinal) ||
                    name.Equals("DocumentIntelligence", StringComparison.Ordinal));

        // The instant and the actor reference are parameters on every member
        // that needs them, which is what makes the workflow a function of its
        // arguments.
        var extract = typeof(IExtractionIngestWorkflow)
            .GetMethod(nameof(IExtractionIngestWorkflow.ExtractAsync))!;

        Assert.Contains(
            extract.GetParameters(),
            parameter => parameter.ParameterType == typeof(DateTimeOffset));
    }

    [Theory]
    [InlineData(DocumentClassification.T4Slip)]
    [InlineData(DocumentClassification.MedicalExpenseEvidence)]
    public async Task BothSupportedPathsApplyTheFullStoredScopeToBothContentReads(
        DocumentClassification classification)
    {
        var harness = IngestHarness.Create();
        harness.Provider.Classification = classification;

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);
        Assert.Collection(
            harness.Content.OpenCalls,
            AssertFullScope,
            AssertFullScope);

        static void AssertFullScope(
            (TrustedFirmId Firm, Guid CaseId, Guid DocumentId, string ContentKey) call)
        {
            Assert.Equal(IngestHarness.Firm, call.Firm);
            Assert.Equal(IngestHarness.CaseId, call.CaseId);
            Assert.Equal(IngestHarness.DocumentId, call.DocumentId);
            Assert.Equal(IngestHarness.ContentKey, call.ContentKey);
        }
    }

    [Fact]
    public void NoModuleTypeCanBeConstructedWithAnUntrustedFirmString()
    {
        // TrustedFirmId has no public constructor, so the only ways to obtain
        // one are the server's own explicit conversion and a strict parse. That
        // is what makes "the firm was trusted" checkable by the compiler at
        // every one of this module's boundaries.
        Assert.Empty(
            typeof(TrustedFirmId).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }
}
