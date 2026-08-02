using System.Security.Cryptography;
using System.Text;
using ClientIntake.Contracts;
using ClientIntake.Models;

namespace TaxAI.Portal;

internal sealed class CryptographicIntakeCredentialSource :
    IIntakeCredentialSource
{
    private readonly byte[] _digestKey;

    internal CryptographicIntakeCredentialSource(byte[] digestKey)
    {
        ArgumentNullException.ThrowIfNull(digestKey);
        if (digestKey.Length != 32)
        {
            throw new ArgumentOutOfRangeException(nameof(digestKey));
        }

        _digestKey = digestKey.ToArray();
    }

    public Guid CreateIdentifier()
    {
        Guid identifier;
        do
        {
            identifier = new Guid(RandomNumberGenerator.GetBytes(16));
        }
        while (identifier == Guid.Empty);

        return identifier;
    }

    public IntakeSecret CreateCredential()
    {
        var encoded = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        if (!IntakeSecret.TryParse(encoded, out var credential))
        {
            throw new CryptographicException(
                "A canonical intake credential could not be generated.");
        }

        return credential!;
    }

    public IntakeTokenDigest Digest(
        IntakeSecret credential,
        IntakeTokenPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        var purposeBytes = Encoding.UTF8.GetBytes(
            purpose == IntakeTokenPurpose.BootstrapLink
                ? "TaxAI.Intake.Token.v1.Bootstrap"
                : "TaxAI.Intake.Token.v1.Session");
        var credentialBytes = Encoding.ASCII.GetBytes(credential.Reveal());
        var input = new byte[purposeBytes.Length + 1 + credentialBytes.Length];
        purposeBytes.CopyTo(input, 0);
        credentialBytes.CopyTo(input, purposeBytes.Length + 1);

        using var hmac = new HMACSHA256(_digestKey);
        return new IntakeTokenDigest(hmac.ComputeHash(input));
    }

    public override string ToString() =>
        nameof(CryptographicIntakeCredentialSource);
}
