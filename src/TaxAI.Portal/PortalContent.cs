namespace TaxAI.Portal;

/// <summary>
/// The portal's fixed anonymous response vocabulary and shared content types.
/// <para>
/// The health body remains spec 010's exact byte sequence. The root shell is
/// still a compile-time constant and contains no taxpayer data; spec 012 changes
/// its one capability sentence because saying the application renders nothing
/// would no longer be true after the authenticated review route ships.
/// </para>
/// <para>
/// Line endings are written as explicit <c>\n</c> escapes, and the shell
/// document ends with exactly one of them. That is deliberate: the source file's
/// own line endings are normalized by git on checkout, so embedding them in the
/// literal would make the response platform-dependent and break byte-for-byte
/// assertions on a different machine.
/// </para>
/// <para>
/// Neither body carries a version, environment name, machine or user name, path,
/// timestamp, or configuration value. A probe endpoint that describes the system
/// is a disclosure surface. Neither references an external stylesheet, script,
/// font, or image — there is nothing to fetch and no client-side stack.
/// </para>
/// </summary>
internal static class PortalContent
{
    /// <summary>The entire body of <c>GET /health</c>. No trailing newline.</summary>
    internal const string HealthBody = "Healthy";

    internal const string PlainTextContentType = "text/plain; charset=utf-8";

    internal const string HtmlContentType = "text/html; charset=utf-8";

    /// <summary>
    /// The entire body of <c>GET /</c>: a static, server-rendered shell that
    /// describes the one authenticated review capability without rendering any
    /// taxpayer value. It contains no data binding and no dynamic value.
    /// </summary>
    internal const string ShellHtml =
        "<!DOCTYPE html>\n" +
        "<html lang=\"en\">\n" +
        "<head>\n" +
        "<meta charset=\"utf-8\" />\n" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n" +
        "<title>TaxAI</title>\n" +
        "</head>\n" +
        "<body>\n" +
        "<h1>TaxAI</h1>\n" +
        "<p>Authenticated accountants can review one stored package in read-only form.</p>\n" +
        "<p><a href=\"/sign-in\">Sign in</a></p>\n" +
        "</body>\n" +
        "</html>\n";
}
