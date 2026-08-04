using System.Globalization;
using Tenancy.Models;

namespace ExtractionWorkflow.Contracts;

/// <summary>
/// A bounded loan of one stored source document, for extraction and nothing
/// else.
/// <para>
/// The lease is what stops a stream outliving the operation that opened it.
/// Handing back a bare <see cref="Stream"/> would let a caller keep it, cache
/// it, or hand it to something that writes it somewhere; a disposable lease
/// makes the byte lifetime explicit and makes "the bytes are gone now"
/// checkable. After disposal the content is unreachable through this object.
/// </para>
/// </summary>
public sealed class ExtractionSourceLease : IAsyncDisposable
{
    private readonly Stream _content;
    private bool _disposed;

    private ExtractionSourceLease(Stream content) => _content = content;

    /// <summary>
    /// The source stream, readable until this lease is disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The lease has been disposed.</exception>
    public Stream Content =>
        _disposed
            ? throw new ObjectDisposedException(nameof(ExtractionSourceLease))
            : _content;

    /// <summary>True once the lease has been disposed and the bytes released.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Wraps a stream the adapter has just opened.</summary>
    public static ExtractionSourceLease Create(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new ExtractionSourceLease(content);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _content.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Fixed structural facts only — never a key, a name, or a byte.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ExtractionSourceLease(IsDisposed: {0})",
            _disposed);
}

/// <summary>
/// The extraction-only read path over stored source content.
/// <para>
/// This is deliberately <b>not</b> a read member on the intake content store.
/// That contract is write-only by design, and the workflow holding it is
/// reachable from anonymous client routes — a read member there would be one
/// injection away from every anonymous handler in the system. A separate port
/// keeps the consumer set to exactly one: the extraction workflow, reached only
/// from an authenticated, live, firm-scoped accountant request whose session and
/// membership have already been revalidated.
/// </para>
/// <para>
/// <b>All four scope arguments are required.</b> There is no overload, optional
/// argument, or default that omits any of them, so a content key on its own can
/// never authorize a read. The implementation verifies that the firm, case,
/// document, and key all belong to the same server-stored firm and case before
/// returning a byte.
/// </para>
/// <para>
/// Every mismatch returns null — a foreign firm, a different case, a different
/// document, an unknown key, and a document that does not exist are
/// indistinguishable to the caller, which is what keeps the shared empty
/// response from becoming an oracle.
/// </para>
/// </summary>
public interface IExtractionContentReader
{
    /// <summary>
    /// Opens the stored source for one extraction, or returns null for every
    /// mismatch and every absent document.
    /// </summary>
    Task<ExtractionSourceLease?> OpenAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        string contentKey,
        CancellationToken cancellationToken);
}
