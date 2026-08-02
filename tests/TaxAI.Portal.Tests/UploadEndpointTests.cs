using System.Net;
using System.Net.Http.Headers;

namespace TaxAI.Portal.Tests;

public sealed class UploadEndpointTests
{
    [Fact]
    public async Task UploadStreamsOneFileAndDiscardsTheReceivedFilename()
    {
        var workflow = ClientIntakePageTests.SuccessfulClientWorkflow();
        using var fixture = IntakePortalHarness.Create(client: workflow);
        using var client = fixture.CreateClient(followRedirects: false);
        await ClientIntakePageTests.RedeemAsync(client);

        using var page = await client.GetAsync("/client-intake");
        var pageHtml = await page.Content.ReadAsStringAsync();
        var token = IntakePortalHarness.AntiforgeryToken(pageHtml);
        var contentBytes = "%PDF-synthetic"u8.ToArray();

        using var multipart = new MultipartFormDataContent("taxai-test-boundary");
        multipart.Add(new StringContent("T4Slip"), "category");
        multipart.Add(new StringContent("1"), "ordinal");
        multipart.Add(new StringContent("Pdf"), "declaredMediaKind");
        multipart.Add(
            new StringContent(contentBytes.Length.ToString()),
            "declaredLength");
        var file = new ByteArrayContent(contentBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        multipart.Add(file, "document", "planted-original-name.pdf");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/client-intake/documents")
        {
            Content = multipart,
        };
        request.Headers.Add("X-TaxAI-Antiforgery", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var upload = Assert.Single(workflow.UploadCalls);
        Assert.Equal("T4Slip", upload.Command.Category?.ToString());
        Assert.Equal(1, upload.Command.Ordinal);
        Assert.Equal("Pdf", upload.Command.DeclaredMediaKind?.ToString());
        Assert.Equal(contentBytes.Length, upload.Command.DeclaredLength);
        Assert.Equal(contentBytes, upload.Content);
        Assert.DoesNotContain(
            "planted-original-name.pdf",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedMultipartAndMissingAntiforgeryDoNotReachTheWorkflow()
    {
        var workflow = ClientIntakePageTests.SuccessfulClientWorkflow();
        using var fixture = IntakePortalHarness.Create(client: workflow);
        using var client = fixture.CreateClient(followRedirects: false);
        await ClientIntakePageTests.RedeemAsync(client);

        using var page = await client.GetAsync("/client-intake");
        var token = IntakePortalHarness.AntiforgeryToken(
            await page.Content.ReadAsStringAsync());

        using var malformedRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/client-intake/documents")
        {
            Content = new StringContent("not multipart"),
        };
        malformedRequest.Headers.Add("X-TaxAI-Antiforgery", token);
        using var malformed = await client.SendAsync(malformedRequest);

        using var noTokenBody = ValidMultipart();
        using var withoutToken = await client.PostAsync(
            "/client-intake/documents",
            noTokenBody);

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        Assert.Empty(workflow.UploadCalls);
    }

    [Fact]
    public async Task ZeroByteFileReachesTheWorkflowForRejectedAttemptProjection()
    {
        var workflow = ClientIntakePageTests.SuccessfulClientWorkflow();
        using var fixture = IntakePortalHarness.Create(client: workflow);
        using var client = fixture.CreateClient(followRedirects: false);
        await ClientIntakePageTests.RedeemAsync(client);

        using var page = await client.GetAsync("/client-intake");
        var token = IntakePortalHarness.AntiforgeryToken(
            await page.Content.ReadAsStringAsync());
        using var multipart = new MultipartFormDataContent("taxai-empty-file");
        multipart.Add(new StringContent("T4Slip"), "category");
        multipart.Add(new StringContent("1"), "ordinal");
        multipart.Add(new StringContent("Pdf"), "declaredMediaKind");
        multipart.Add(new StringContent("0"), "declaredLength");
        multipart.Add(
            new ByteArrayContent(Array.Empty<byte>()),
            "document",
            "upload.bin");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/client-intake/documents")
        {
            Content = multipart,
        };
        request.Headers.Add("X-TaxAI-Antiforgery", token);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var upload = Assert.Single(workflow.UploadCalls);
        Assert.Equal(0, upload.Command.DeclaredLength);
        Assert.Empty(upload.Content);
    }

    [Fact]
    public async Task SecondFileIsRejectedBeforeTheWorkflowCanComplete()
    {
        var workflow = ClientIntakePageTests.SuccessfulClientWorkflow();
        using var fixture = IntakePortalHarness.Create(client: workflow);
        using var client = fixture.CreateClient(followRedirects: false);
        await ClientIntakePageTests.RedeemAsync(client);

        using var page = await client.GetAsync("/client-intake");
        var token = IntakePortalHarness.AntiforgeryToken(
            await page.Content.ReadAsStringAsync());
        var bytes = "%PDF-two-files"u8.ToArray();
        using var multipart = new MultipartFormDataContent("taxai-two-files");
        multipart.Add(new StringContent("T4Slip"), "category");
        multipart.Add(new StringContent("1"), "ordinal");
        multipart.Add(new StringContent("Pdf"), "declaredMediaKind");
        multipart.Add(new StringContent(bytes.Length.ToString()), "declaredLength");
        multipart.Add(
            new ByteArrayContent(bytes),
            "document",
            "upload.bin");
        multipart.Add(
            new ByteArrayContent(bytes),
            "document",
            "upload.bin");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/client-intake/documents")
        {
            Content = multipart,
        };
        request.Headers.Add("X-TaxAI-Antiforgery", token);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(workflow.UploadCalls);
    }

    [Fact]
    public void BrowserModuleUsesOnlyTheNeutralTransportFilename()
    {
        var modulePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "TaxAI.Portal",
            "Components",
            "Shared",
            "ClientQuestionnairePanel.razor.js");
        var module = File.ReadAllText(modulePath);

        Assert.Contains(
            "body.append(\"document\", file, \"upload.bin\")",
            module,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".name", module, StringComparison.Ordinal);
        Assert.DoesNotContain("FileReader", module, StringComparison.Ordinal);
        Assert.Contains(
            "document.addEventListener(\"click\"",
            module,
            StringComparison.Ordinal);
        Assert.Contains(
            "closest(\"form[data-intake-upload]\")",
            module,
            StringComparison.Ordinal);
    }

    private static MultipartFormDataContent ValidMultipart()
    {
        var bytes = "%PDF-test"u8.ToArray();
        var multipart = new MultipartFormDataContent("taxai-no-token");
        multipart.Add(new StringContent("T4Slip"), "category");
        multipart.Add(new StringContent("1"), "ordinal");
        multipart.Add(new StringContent("Pdf"), "declaredMediaKind");
        multipart.Add(new StringContent(bytes.Length.ToString()), "declaredLength");
        multipart.Add(new ByteArrayContent(bytes), "document", "upload.bin");
        return multipart;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find the repository root containing TaxAI.sln.");
    }
}
