namespace TaxAI.Portal;

/// <summary>
/// The portal's complete response vocabulary: two fixed byte sequences and their
/// content types.
/// <para>
/// These constants are spec 010's Wave 0 contract. Later waves map them; they do
/// not invent bodies. Keeping the responses as compile-time constants — rather
/// than files on disk or rendered views — is what makes "the portal serves
/// exactly two known byte sequences" mechanically true: a constant is
/// inspectable at compile time and cannot be replaced at deploy time.
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
    /// states what this application is and that it serves no taxpayer data. It
    /// contains no data binding and no dynamic value of any kind.
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
        "<p>This application serves no taxpayer data.</p>\n" +
        "</body>\n" +
        "</html>\n";
}
