using System.Diagnostics.Tracing;
using System.Net;
using System.Reflection;
using DocumentIntelligence.Production;

namespace DocumentIntelligence.Tests.Production;

public sealed class ProductionPackageBoundaryTests
{
    [Fact]
    public void ProductionTransportDisablesRedirectsCookiesProxyAndPropagation()
    {
        using var client = (HttpClient)typeof(AzureDocumentIntelligenceProvider)
            .GetMethod("CreateClient", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        var handler = (SocketsHttpHandler)typeof(HttpMessageInvoker)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseProxy);
        Assert.Null(handler.ActivityHeadersPropagator);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public async Task DeletionFailureEventHasNoPayloadAndNeverIncludesResultId()
    {
        using var listener = new LifecycleListener();
        using var wire = new ProviderWire { DeleteStatus = HttpStatusCode.ServiceUnavailable };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
        var captured = Assert.Single(listener.Events);
        Assert.Equal(1, captured.EventId);
        Assert.True(captured.Payload is null || captured.Payload.Count == 0);
        Assert.Equal("Document processing cleanup unavailable.", captured.Message);
    }

    private sealed class LifecycleListener : EventListener
    {
        internal readonly List<EventWrittenEventArgs> Events = [];
        protected override void OnEventSourceCreated(EventSource eventSource)
        { if (eventSource.Name == "TaxAI-DocumentIntelligence-Lifecycle") EnableEvents(eventSource, EventLevel.LogAlways); }
        protected override void OnEventWritten(EventWrittenEventArgs eventData) => Events.Add(eventData);
    }
}
