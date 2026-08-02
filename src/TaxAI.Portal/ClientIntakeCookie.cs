using System.Security.Cryptography;
using System.Text;
using ClientIntake.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace TaxAI.Portal;

/// <summary>
/// Protects the browser's opaque client-session key. The durable intake session
/// remains authoritative; the cookie carries no firm, case, link, or taxpayer
/// value.
/// </summary>
internal sealed class ClientIntakeCookie
{
    internal const string Name = "__Secure-taxai.client-intake";
    internal const string Path = "/client-intake";

    private readonly IDataProtector _protector;

    public ClientIntakeCookie(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _protector = provider.CreateProtector(
            "TaxAI.Portal.ClientIntake.SessionCookie.v1");
    }

    internal void Append(
        HttpResponse response,
        RedeemedIntakeSession session)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(session);

        var protectedBytes = _protector.Protect(
            Encoding.UTF8.GetBytes(session.SessionCredential.Reveal()));
        response.Cookies.Append(
            Name,
            WebEncoders.Base64UrlEncode(protectedBytes),
            new CookieOptions
            {
                Expires = session.AbsoluteExpiresAtUtc,
                HttpOnly = true,
                IsEssential = true,
                Path = Path,
                SameSite = SameSiteMode.Strict,
                Secure = true,
            });
    }

    internal bool TryRead(HttpRequest request, out IntakeSecret? credential)
    {
        ArgumentNullException.ThrowIfNull(request);

        credential = null;
        if (!request.Cookies.TryGetValue(Name, out var protectedValue))
        {
            return false;
        }

        try
        {
            var protectedBytes = WebEncoders.Base64UrlDecode(protectedValue);
            var raw = Encoding.UTF8.GetString(
                _protector.Unprotect(protectedBytes));
            return IntakeSecret.TryParse(raw, out credential);
        }
        catch (Exception exception) when (
            exception is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
