using System.Reflection;
using System.Text;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;
using TaxLogicEngine.Models;
using Tenancy.Models;

namespace ExtractionWorkflow.Tests.Services;

/// <summary>
/// Spec 015 T02 — the single in-memory payload assembly path, and the
/// extraction-only content-read port it will be fed from.
/// <para>
/// The builder is where the raw <see cref="T4RawInput"/> comes into existence
/// and where it stops existing. These tests hold it to three things: it copies
/// rather than decides, it fails closed on everything untrusted provider output
/// can be, and the payload never leaves the call.
/// </para>
/// </summary>
public sealed class ExtractionPayloadBuilderTests
{
    private static readonly TrustedFirmId Firm = TrustedFirmId.FromGuid(Guid.NewGuid());

    // ---------------------------------------------------------- assembly

    [Fact]
    public void ThePayloadCarriesEveryValueTheProviderReported()
    {
        var result = ExtractionPayloadBuilder.Build(SampleProviderResult(), payload =>
        {
            Assert.Equal(2025, payload.Year);
            Assert.Equal("Acme Manufacturing", payload.EmployerName);
            Assert.Equal("12 Main Street", payload.EmployerAddress);
            Assert.Equal("Casey Taxpayer", payload.EmployeeName);
            Assert.Equal("9 Bay Road", payload.EmployeeAddress);
            Assert.True(payload.ExemptCppQpp);
            Assert.False(payload.ExemptEi);
            Assert.Null(payload.ExemptPpip);

            return ParseResult<int>.Success(payload.Boxes.Count);
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value);
    }

    [Fact]
    public void TheBoxesArriveInOrderWithDuplicatesIntact()
    {
        var provider = new ProviderT4Result
        {
            Year = 2025,
            Boxes = new ProviderBoxValue?[]
            {
                new() { Code = "14", Value = "50000.00" },
                new() { Code = "14", Value = "60000.00" },
                new() { Code = "16A", Value = "188.00" },
            },
        };

        ExtractionPayloadBuilder.Build(provider, payload =>
        {
            // Collapsing, deduplicating, or reordering here would hide exactly
            // what the engine's own duplicate-box rejection exists to catch.
            Assert.Equal(
                new[] { ("14", "50000.00"), ("14", "60000.00"), ("16A", "188.00") },
                payload.Boxes.Select(box => (box.Code, box.Value)).ToArray());

            return ParseResult<bool>.Success(true);
        });
    }

    [Fact]
    public void AnEmptyBoxKeepsItsAbsentValue()
    {
        var provider = new ProviderT4Result
        {
            Boxes = new ProviderBoxValue?[] { new() { Code = "14", Value = null } },
        };

        ExtractionPayloadBuilder.Build(provider, payload =>
        {
            Assert.Null(payload.Boxes.Single().Value);
            return ParseResult<bool>.Success(true);
        });
    }

    [Fact]
    public void NothingIsInventedForAFieldTheDocumentDidNotShow()
    {
        var employeeCopy = new ProviderT4Result
        {
            Year = 2025,
            EmployerName = "Acme Manufacturing",
            EmployeeName = "Casey Taxpayer",
            Boxes = new ProviderBoxValue?[]
            {
                new() { Code = "12", Value = "123456789" },
                new() { Code = "14", Value = "50000.00" },
                new() { Code = "45", Value = "1" },
            },
        };

        ExtractionPayloadBuilder.Build(employeeCopy, payload =>
        {
            // An employee copy carries no Box 54 and a scan may carry no
            // address. A missing thing stays missing: nothing here defaults,
            // infers, substitutes, or copies a value from another field.
            Assert.DoesNotContain(payload.Boxes, box => box.Code == "54");
            Assert.Null(payload.EmployerAddress);
            Assert.Null(payload.EmployeeAddress);
            Assert.Null(payload.ExemptCppQpp);

            return ParseResult<bool>.Success(true);
        });
    }

    [Fact]
    public void TheYearIsWhateverTheDocumentShowed()
    {
        // Never coerced towards a supported year, and never read from a clock:
        // the coherence check between this and the case's year belongs to the
        // existing envelope validator.
        var provider = new ProviderT4Result { Year = 2024, Boxes = Array.Empty<ProviderBoxValue?>() };

        ExtractionPayloadBuilder.Build(provider, payload =>
        {
            Assert.Equal(2024, payload.Year);
            return ParseResult<bool>.Success(true);
        });
    }

    [Fact]
    public void TheSameProviderResultAlwaysAssemblesTheSamePayload()
    {
        var first = Assemble(SampleProviderResult());
        var second = Assemble(SampleProviderResult());

        Assert.Equal(first.Year, second.Year);
        Assert.Equal(first.EmployerName, second.EmployerName);
        Assert.Equal(
            first.Boxes.Select(box => (box.Code, box.Value)).ToArray(),
            second.Boxes.Select(box => (box.Code, box.Value)).ToArray());
    }

    // ---------------------------------------------------- the payload's life

    [Fact]
    public void TheBuilderReturnsWhateverTheContinuationReturnedAndNeverThePayload()
    {
        var method = typeof(ExtractionPayloadBuilder).GetMethod(nameof(ExtractionPayloadBuilder.Build));

        Assert.NotNull(method);
        Assert.True(method!.IsGenericMethodDefinition);

        // The only way out is the continuation's own result type. No overload,
        // property, or out parameter hands the assembled payload to a caller.
        Assert.DoesNotContain(
            typeof(ExtractionPayloadBuilder).GetMethods(BindingFlags.Public | BindingFlags.Static),
            candidate => candidate.ReturnType == typeof(T4RawInput) ||
                candidate.GetParameters().Any(parameter =>
                    parameter.IsOut && parameter.ParameterType.GetElementType() == typeof(T4RawInput)));

        Assert.DoesNotContain(
            typeof(ExtractionPayloadBuilder).GetProperties(BindingFlags.Public | BindingFlags.Static),
            property => property.PropertyType == typeof(T4RawInput));
    }

    [Fact]
    public void AnAbsentContinuationIsAProgrammingErrorNotAFailedResult()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ExtractionPayloadBuilder.Build<bool>(SampleProviderResult(), null!));
    }

    // -------------------------------------------------------- failing closed

    [Fact]
    public void AnAbsentProviderResultFailsClosed()
    {
        AssertFailsClosed(null, ExtractionWorkflowFields.Provider, ExtractionWorkflowErrors.ProviderResultAbsent);
    }

    [Fact]
    public void AnAbsentBoxCollectionFailsClosed()
    {
        AssertFailsClosed(
            new ProviderT4Result { Year = 2025, Boxes = null },
            ExtractionWorkflowFields.PayloadBoxes,
            ExtractionWorkflowErrors.PayloadBoxesAbsent);
    }

    [Fact]
    public void AnAbsentBoxEntryFailsClosed()
    {
        AssertFailsClosed(
            new ProviderT4Result { Boxes = new ProviderBoxValue?[] { null } },
            ExtractionWorkflowFields.PayloadBox,
            ExtractionWorkflowErrors.PayloadBoxAbsent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABoxWithNoCodeFailsClosed(string? code)
    {
        AssertFailsClosed(
            new ProviderT4Result
            {
                Boxes = new ProviderBoxValue?[]
                {
                    new() { Code = "14", Value = "50000.00" },
                    new() { Code = code, Value = "1.00" },
                },
            },
            ExtractionWorkflowFields.PayloadBoxCode,
            ExtractionWorkflowErrors.PayloadBoxCodeAbsent);
    }

    [Fact]
    public void AnEmptyProviderResultStillAssemblesAnEmptyPayloadForTheEngineToRefuse()
    {
        // "Nothing extracted" is not this module's rejection to make. An empty
        // box list is well-formed, so it goes to the engine, which is the one
        // authority on whether a slip without boxes is a slip.
        var payload = Assemble(new ProviderT4Result { Boxes = Array.Empty<ProviderBoxValue?>() });

        Assert.Empty(payload.Boxes);
        Assert.Null(payload.Year);
    }

    // ------------------------------------------- the content-read port shape

    [Fact]
    public void TheReadPortRequiresAllFourScopeArgumentsWithNoWayToOmitOne()
    {
        var members = typeof(IExtractionContentReader).GetMethods();

        var open = Assert.Single(members);

        Assert.Equal(
            new[]
            {
                typeof(TrustedFirmId),
                typeof(Guid),
                typeof(Guid),
                typeof(string),
                typeof(CancellationToken),
            },
            open.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        // No overload, optional argument, or default omits any of the four: a
        // content key on its own can never authorize a read.
        Assert.All(
            open.GetParameters(),
            parameter => Assert.False(parameter.IsOptional));
    }

    [Fact]
    public async Task AReaderReturnsTheContentForTheExactFourPartScope()
    {
        var reader = InMemoryReader(out var scope);

        await using var lease = await reader.OpenAsync(
            scope.Firm, scope.CaseId, scope.DocumentId, scope.ContentKey, CancellationToken.None);

        Assert.NotNull(lease);
        Assert.Equal("source bytes", await ReadAllAsync(lease!.Content));
    }

    [Fact]
    public async Task EveryScopeMismatchReadsNothingAndIsIndistinguishable()
    {
        var reader = InMemoryReader(out var scope);
        var otherFirm = TrustedFirmId.FromGuid(Guid.NewGuid());

        var attempts = new[]
        {
            await reader.OpenAsync(otherFirm, scope.CaseId, scope.DocumentId, scope.ContentKey, CancellationToken.None),
            await reader.OpenAsync(scope.Firm, Guid.NewGuid(), scope.DocumentId, scope.ContentKey, CancellationToken.None),
            await reader.OpenAsync(scope.Firm, scope.CaseId, Guid.NewGuid(), scope.ContentKey, CancellationToken.None),
            await reader.OpenAsync(scope.Firm, scope.CaseId, scope.DocumentId, "another-key", CancellationToken.None),
            await reader.OpenAsync(otherFirm, Guid.NewGuid(), Guid.NewGuid(), "another-key", CancellationToken.None),
        };

        // A foreign firm, a different case, a different document, an unknown
        // key, and a document that does not exist all look identical.
        Assert.All(attempts, attempt => Assert.Null(attempt));
    }

    [Fact]
    public async Task TheLeaseBoundsTheByteLifetime()
    {
        var reader = InMemoryReader(out var scope);

        var lease = await reader.OpenAsync(
            scope.Firm, scope.CaseId, scope.DocumentId, scope.ContentKey, CancellationToken.None);

        Assert.NotNull(lease);
        Assert.False(lease!.IsDisposed);

        var content = lease.Content;
        await lease.DisposeAsync();

        Assert.True(lease.IsDisposed);
        Assert.False(content.CanRead);
        Assert.Throws<ObjectDisposedException>(() => lease.Content);

        // Disposing twice is not an error; a lease released early stays released.
        await lease.DisposeAsync();
        Assert.True(lease.IsDisposed);
    }

    [Fact]
    public void ALeaseCannotWrapAnAbsentStream()
    {
        Assert.Throws<ArgumentNullException>(() => ExtractionSourceLease.Create(null!));
    }

    [Fact]
    public void ALeasePrintsStructuralFactsOnly()
    {
        using var stream = new MemoryStream();
        var lease = ExtractionSourceLease.Create(stream);

        Assert.Equal("ExtractionSourceLease(IsDisposed: False)", lease.ToString());
    }

    // ------------------------------------------------------------- helpers

    private static T4RawInput Assemble(ProviderT4Result? provider)
    {
        T4RawInput? captured = null;

        var result = ExtractionPayloadBuilder.Build(provider, payload =>
        {
            captured = payload;
            return ParseResult<bool>.Success(true);
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        return captured!;
    }

    private static void AssertFailsClosed(
        ProviderT4Result? provider,
        string expectedField,
        string expectedMessage)
    {
        var continuationRan = false;

        // A reference outcome type, so "no value came back" is checkable: a
        // value-typed one would report its default rather than nothing.
        var result = ExtractionPayloadBuilder.Build<string>(provider, _ =>
        {
            continuationRan = true;
            return ParseResult<string>.Success("processed");
        });

        Assert.False(continuationRan);
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);

        var error = Assert.Single(result.Errors);
        Assert.Equal(expectedField, error.Field);
        Assert.Equal(expectedMessage, error.Message);
    }

    private static ProviderT4Result SampleProviderResult() => new()
    {
        Year = 2025,
        EmployerName = "Acme Manufacturing",
        EmployerAddress = "12 Main Street",
        EmployeeName = "Casey Taxpayer",
        EmployeeAddress = "9 Bay Road",
        ExemptCppQpp = true,
        ExemptEi = false,
        ExemptPpip = null,
        Boxes = new ProviderBoxValue?[]
        {
            new() { Code = "12", Value = "123456789" },
            new() { Code = "14", Value = "50000.00" },
            new() { Code = "45", Value = "1" },
            new() { Code = "54", Value = null },
        },
    };

    private static async Task<string> ReadAllAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static IExtractionContentReader InMemoryReader(out ContentScope scope)
    {
        scope = new ContentScope(
            Firm, Guid.NewGuid(), Guid.NewGuid(), "content-key-1");

        return new InMemoryExtractionContentReader(scope, Encoding.UTF8.GetBytes("source bytes"));
    }

    internal sealed record ContentScope(
        TrustedFirmId Firm,
        Guid CaseId,
        Guid DocumentId,
        string ContentKey);

    /// <summary>
    /// A real implementation of the declared port, so the contract is exercised
    /// by the task that declares it rather than sitting idle until the portal
    /// supplies the encrypted one. It answers for exactly one stored document
    /// and returns nothing for every other combination — the same fail-closed
    /// shape the production adapter must have.
    /// </summary>
    private sealed class InMemoryExtractionContentReader : IExtractionContentReader
    {
        private readonly ContentScope _scope;
        private readonly byte[] _content;

        internal InMemoryExtractionContentReader(ContentScope scope, byte[] content)
        {
            _scope = scope;
            _content = content;
        }

        public Task<ExtractionSourceLease?> OpenAsync(
            TrustedFirmId firm,
            Guid caseId,
            Guid documentId,
            string contentKey,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(firm);
            ArgumentNullException.ThrowIfNull(contentKey);

            if (!firm.Equals(_scope.Firm) ||
                caseId != _scope.CaseId ||
                documentId != _scope.DocumentId ||
                !string.Equals(contentKey, _scope.ContentKey, StringComparison.Ordinal))
            {
                return Task.FromResult<ExtractionSourceLease?>(null);
            }

            return Task.FromResult<ExtractionSourceLease?>(
                ExtractionSourceLease.Create(new MemoryStream(_content, writable: false)));
        }
    }
}
