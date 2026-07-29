using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace TaxAI.Portal.Tests.Support;

/// <summary>
/// The approved <b>test-only</b> durable Data Protection configuration, and the
/// only way a test in this project may satisfy decision B-6.
/// <para>
/// B-6 requires that, outside Development, the host refuse to start unless the
/// built service provider holds a real key repository and a real key encryptor,
/// neither of which is one of the ASP.NET Core Data Protection framework's own
/// local implementations. In production that is Azure Blob Storage plus Azure
/// Key Vault. This repository ships neither — and a test must not conjure one —
/// so the suites that start a Production host register these fakes instead.
/// </para>
/// <para>
/// The fakes are <b>durable in the only sense the guard inspects</b>: they are
/// explicitly configured, and they are declared in this test assembly rather
/// than in the framework assembly, so the guard classifies them as approved.
/// They are emphatically not durable in the sense a deployment needs, which is
/// why they live here and are reachable by no production code.
/// </para>
/// <para>
/// Everything they hold is synthetic and in memory. Nothing is written to disk,
/// the registry, an environment variable, a cloud service, or any other external
/// system, and nothing here is a secret or a taxpayer value.
/// </para>
/// </summary>
internal static class DurableDataProtectionTestServices
{
    /// <summary>
    /// Registers the approved test provider. Call from
    /// <c>ConfigureTestServices</c> so it lands after the host's own
    /// registrations and wins.
    /// </summary>
    /// <remarks>
    /// <c>AddDataProtection()</c> is what puts <c>IDataProtectionProvider</c> in
    /// the container — a bare ASP.NET Core host has none at all — and the
    /// <c>Configure</c> call is what replaces the framework's implicit
    /// machine-local key directory with an explicit repository. Both halves are
    /// required: the framework leaves <c>XmlRepository</c> null and resolves the
    /// local directory lazily, so a null repository is the fallback rather than
    /// an undecided state.
    /// </remarks>
    internal static IServiceCollection AddApprovedDurableDataProtection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDataProtection();

        services.Configure<KeyManagementOptions>(options =>
        {
            options.XmlRepository = new SyntheticKeyRepository();
            options.XmlEncryptor = new SyntheticKeyEncryptor();
        });

        return services;
    }

    /// <summary>
    /// Holds key elements in memory for the lifetime of one host. Deterministic
    /// enough for a startup test — it starts empty, returns exactly what was
    /// stored, and never reaches outside the process.
    /// </summary>
    private sealed class SyntheticKeyRepository : IXmlRepository
    {
        private readonly ConcurrentQueue<XElement> _elements = new();

        public IReadOnlyCollection<XElement> GetAllElements() => _elements.ToArray();

        public void StoreElement(XElement element, string friendlyName)
        {
            ArgumentNullException.ThrowIfNull(element);

            _elements.Enqueue(new XElement(element));
        }

        /// <summary>Fixed type identity only; never key material.</summary>
        public override string ToString() => nameof(SyntheticKeyRepository);
    }

    /// <summary>
    /// Wraps the key element without transforming it. A test double for "some
    /// non-framework encryptor is configured" — not an encryption scheme, and
    /// deliberately not one: an implementation that looked like real key
    /// protection would invite someone to reach for it outside a test.
    /// </summary>
    private sealed class SyntheticKeyEncryptor : IXmlEncryptor
    {
        internal const string ElementName = "syntheticTestKeyRing";

        public EncryptedXmlInfo Encrypt(XElement plaintextElement)
        {
            ArgumentNullException.ThrowIfNull(plaintextElement);

            return new EncryptedXmlInfo(
                new XElement(ElementName, new XElement(plaintextElement)),
                typeof(SyntheticKeyDecryptor));
        }

        /// <summary>Fixed type identity only; never key material.</summary>
        public override string ToString() => nameof(SyntheticKeyEncryptor);
    }

    /// <summary>The matching unwrap, so a host that exercises the key ring round-trips.</summary>
    private sealed class SyntheticKeyDecryptor : IXmlDecryptor
    {
        public XElement Decrypt(XElement encryptedElement)
        {
            ArgumentNullException.ThrowIfNull(encryptedElement);

            return new XElement(encryptedElement.Elements().Single());
        }

        /// <summary>Fixed type identity only; never key material.</summary>
        public override string ToString() => nameof(SyntheticKeyDecryptor);
    }
}
