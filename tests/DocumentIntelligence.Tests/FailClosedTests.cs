using System.Text;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentIntelligence.Tests;

/// <summary>
/// Spec 015 T02 — everything that can go wrong, and the one thing that happens
/// when it does.
/// <para>
/// A provider that throws makes bad input into a code path every caller has to
/// distinguish; a provider that returns half an extraction makes bad input into
/// a filing error. This adapter does neither: an absent stream, a stream that
/// fails mid-read, a cancelled operation, and content beyond the configured
/// limit all produce a null result, nothing partial, and no exception handed
/// back.
/// </para>
/// <para>
/// The configuration guard is here for the same reason. A host that cannot
/// supply a usable size limit and provider label refuses to start rather than
/// exposing a half-configured extraction surface.
/// </para>
/// </summary>
public sealed class FailClosedTests
{
    private const string Header = DeterministicDocumentIntelligenceProvider.DocumentHeader;

    private static readonly string[] ValidT4 = { Header, "KIND: T4", "BOX 14: 50000.00" };

    // ----------------------------------------------------- untrusted content

    [Fact]
    public async Task AnAbsentStreamFailsClosedOnEveryMember()
    {
        var provider = DeterministicProviderTests.Provider();

        Assert.Null(await provider.ClassifyAsync(null!, CancellationToken.None));
        Assert.Null(await provider.ExtractT4Async(null!, CancellationToken.None));
        Assert.Null(await provider.ExtractMedicalAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task AStreamThatThrowsFailsClosedOnEveryMember()
    {
        var provider = DeterministicProviderTests.Provider();

        Assert.Null(await provider.ClassifyAsync(new ThrowingStream(), CancellationToken.None));
        Assert.Null(await provider.ExtractT4Async(new ThrowingStream(), CancellationToken.None));
        Assert.Null(await provider.ExtractMedicalAsync(new ThrowingStream(), CancellationToken.None));
    }

    [Fact]
    public async Task AStreamThatFailsPartWayThroughProducesNothingPartial()
    {
        var provider = DeterministicProviderTests.Provider();
        var content = Encoding.UTF8.GetBytes(string.Join("\n", ValidT4));

        // The first read succeeds and the second fails, so a best-effort
        // implementation would return the fields it had already seen.
        var result = await provider.ExtractT4Async(
            new FailAfterFirstReadStream(content), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ACancelledOperationFailsClosedWithoutThrowing()
    {
        var provider = DeterministicProviderTests.Provider();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Assert.Null(await provider.ClassifyAsync(
            DeterministicProviderTests.Document(ValidT4), cancellation.Token));
        Assert.Null(await provider.ExtractT4Async(
            DeterministicProviderTests.Document(ValidT4), cancellation.Token));
        Assert.Null(await provider.ExtractMedicalAsync(
            DeterministicProviderTests.Document(ValidT4), cancellation.Token));
    }

    [Fact]
    public async Task ContentBeyondTheConfiguredLimitIsRefusedRatherThanTruncated()
    {
        var provider = DeterministicProviderTests.Provider(maximumSourceBytes: 16);

        // Truncating would turn an oversized document into a document that
        // merely happens to be missing fields.
        Assert.Null(await provider.ClassifyAsync(
            DeterministicProviderTests.Document(ValidT4), CancellationToken.None));
        Assert.Null(await provider.ExtractT4Async(
            DeterministicProviderTests.Document(ValidT4), CancellationToken.None));
    }

    [Fact]
    public async Task ContentExactlyAtTheConfiguredLimitIsStillRead()
    {
        var content = Encoding.UTF8.GetBytes(string.Join("\n", ValidT4));
        var provider = DeterministicProviderTests.Provider(maximumSourceBytes: content.Length);

        var result = await provider.ClassifyAsync(new MemoryStream(content), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(DocumentClassification.T4Slip, result!.Classification);
    }

    [Fact]
    public async Task AFailedReadNeverBecomesAnUnsupportedClassification()
    {
        // The two outcomes mean different things: "I read it and it is not a
        // supported document" is a classification, "I could not read it" is not.
        var provider = DeterministicProviderTests.Provider();

        var unreadable = await provider.ClassifyAsync(new ThrowingStream(), CancellationToken.None);
        var unrecognized = await provider.ClassifyAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("not a document")), CancellationToken.None);

        Assert.Null(unreadable);
        Assert.Equal(DocumentClassification.Unsupported, unrecognized!.Classification);
    }

    // ------------------------------------------------------- configuration

    [Fact]
    public void AbsentOptionsRefuse()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DocumentIntelligenceOptions.RequireValid(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void ANonPositiveSizeLimitRefuses(long maximumSourceBytes)
    {
        Assert.Throws<ArgumentException>(() => DocumentIntelligenceOptions.RequireValid(
            new DocumentIntelligenceOptions
            {
                MaximumSourceBytes = maximumSourceBytes,
                ProviderLabel = "adapter",
            }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingOrEmptyProviderLabelRefuses(string? providerLabel)
    {
        Assert.Throws<ArgumentException>(() => DocumentIntelligenceOptions.RequireValid(
            new DocumentIntelligenceOptions
            {
                MaximumSourceBytes = 1024,
                ProviderLabel = providerLabel,
            }));
    }

    [Fact]
    public void AnOverlongOrControlCharacterProviderLabelRefuses()
    {
        Assert.Throws<ArgumentException>(() => DocumentIntelligenceOptions.RequireValid(
            new DocumentIntelligenceOptions
            {
                MaximumSourceBytes = 1024,
                ProviderLabel = new string('a', DocumentIntelligenceOptions.MaximumProviderLabelLength + 1),
            }));

        Assert.Throws<ArgumentException>(() => DocumentIntelligenceOptions.RequireValid(
            new DocumentIntelligenceOptions
            {
                MaximumSourceBytes = 1024,
                ProviderLabel = "adapter\nlabel",
            }));
    }

    [Fact]
    public void NoConfiguredValueHasADefault()
    {
        // An unset options object is invalid, so a host that forgot to configure
        // the adapter cannot start with a plausible-looking fallback.
        Assert.Throws<ArgumentException>(() =>
            DocumentIntelligenceOptions.RequireValid(new DocumentIntelligenceOptions()));
    }

    [Fact]
    public void TheProviderRefusesToBeConstructedWithUnusableConfiguration()
    {
        Assert.Throws<ArgumentException>(() =>
            new DeterministicDocumentIntelligenceProvider(new DocumentIntelligenceOptions()));

        Assert.Throws<ArgumentNullException>(() =>
            new DeterministicDocumentIntelligenceProvider(null!));
    }

    [Fact]
    public void OptionsPrintNoConfiguredValue()
    {
        var options = new DocumentIntelligenceOptions
        {
            MaximumSourceBytes = 1024,
            ProviderLabel = "adapter",
        };

        Assert.DoesNotContain("adapter", options.ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------- registration

    [Fact]
    public void RegistrationSuppliesTheDeterministicAdapterAsTheSingleProvider()
    {
        var services = new ServiceCollection();

        services.AddDeterministicDocumentIntelligence(ValidOptions(), isDevelopmentOrTestHost: true);

        var descriptor = Assert.Single(
            services,
            service => service.ServiceType == typeof(IDocumentIntelligenceProvider));

        // One instance, shared: the adapter holds no per-request state, and a
        // second registration would mean two answers about the same bytes.
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.IsType<DeterministicDocumentIntelligenceProvider>(descriptor.ImplementationInstance);
    }

    [Fact]
    public void RegistrationRefusesAHostThatIsNotDevelopmentOrTest()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddDeterministicDocumentIntelligence(
                ValidOptions(), isDevelopmentOrTestHost: false));

        // Nothing was registered on the way to refusing.
        Assert.Empty(services);
    }

    [Fact]
    public void RegistrationRefusesInvalidConfigurationBeforeRegisteringAnything()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddDeterministicDocumentIntelligence(
                new DocumentIntelligenceOptions(), isDevelopmentOrTestHost: true));

        Assert.Empty(services);
    }

    [Fact]
    public void RegistrationRefusesAnAbsentServiceCollectionOrOptions()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DocumentIntelligenceRegistration.AddDeterministicDocumentIntelligence(
                null!, ValidOptions(), isDevelopmentOrTestHost: true));

        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddDeterministicDocumentIntelligence(
                null!, isDevelopmentOrTestHost: true));
    }

    // ------------------------------------------------------------- helpers

    private static DocumentIntelligenceOptions ValidOptions() => new()
    {
        MaximumSourceBytes = 64 * 1024,
        ProviderLabel = "deterministic-test-adapter",
    };

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("The source could not be read.");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            throw new IOException("The source could not be read.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class FailAfterFirstReadStream : Stream
    {
        private readonly byte[] _content;
        private bool _firstReadDone;

        internal FailAfterFirstReadStream(byte[] content) => _content = content;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_firstReadDone)
            {
                throw new IOException("The source failed part way through.");
            }

            _firstReadDone = true;
            var count = Math.Min(buffer.Length, _content.Length);
            _content.AsSpan(0, count).CopyTo(buffer.Span);
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
