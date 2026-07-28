using System.Text.Json.Serialization;

namespace Tenancy.Models;

/// <summary>
/// A local user identity, keyed by OIDC issuer and subject.
/// <para>
/// Authentication is delegated entirely to an external provider, so there is no
/// password, no credential, and no registration surface anywhere in this
/// repository. This record stores issuer, subject, active state, and timestamps
/// and <strong>nothing else</strong>: no email, no phone, no display name, and
/// no provider profile claim. Email and phone are forbidden at rest, and a
/// display name is available from the live principal when one is actually
/// needed.
/// </para>
/// <para>
/// One of the three identity tables that bootstrap tenancy, and therefore one of
/// the three that carry no firm column — they cannot be tenant-scoped without
/// circularity. None of them may carry taxpayer content.
/// </para>
/// </summary>
public sealed class PortalUser
{
    /// <summary>Materialization seam for the storage layer.</summary>
    private PortalUser()
    {
    }

    internal PortalUser(
        Guid userId,
        string issuer,
        string subject,
        bool isActive,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        UserId = userId;
        Issuer = issuer;
        Subject = subject;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid UserId { get; private set; }

    [JsonIgnore]
    public string Issuer { get; private set; } = string.Empty;

    [JsonIgnore]
    public string Subject { get; private set; } = string.Empty;

    /// <summary>Re-checked on every protected request, not only at sign-in.</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Fixed structural facts only; never the issuer or the subject.</summary>
    public override string ToString() => nameof(PortalUser);
}
