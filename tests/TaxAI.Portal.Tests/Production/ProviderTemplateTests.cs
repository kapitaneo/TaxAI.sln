using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TaxAI.Portal.Tests.Production;

public sealed class ProviderTemplateTests
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln"))) directory = directory.Parent;
            return directory!.FullName;
        }
    }
    private static string Template => File.ReadAllText(Path.Combine(Root, "infra/azure/modules/document-intelligence.bicep"));

    [Fact]
    public void PrimaryOnlyPrivateEntraResourceHasNoDiagnosticsOrAlternateModel()
    {
        Assert.True(Approved(Template));
        var main = File.ReadAllText(Path.Combine(Root, "infra/azure/main.bicep"));
        Assert.Contains("module documentIntelligence 'modules/document-intelligence.bicep' = if (isPrimary)", main);
        Assert.Contains("documentIntelligenceResourceId: documentIntelligence!.outputs.resourceId", main);
        Assert.Contains("applicationPrincipalId: primaryInputs!.runtime.managedIdentityPrincipalId", main);
    }

    [Theory]
    [InlineData("'canadacentral'", "'canadaeast'")]
    [InlineData("kind: 'FormRecognizer'", "kind: 'OpenAI'")]
    [InlineData("name: 'S0'", "name: 'F0'")]
    [InlineData("publicNetworkAccess: 'Disabled'", "publicNetworkAccess: 'Enabled'")]
    [InlineData("disableLocalAuth: true", "disableLocalAuth: false")]
    [InlineData("defaultAction: 'Deny'", "defaultAction: 'Allow'")]
    [InlineData("restrictOutboundNetworkAccess: true", "restrictOutboundNetworkAccess: false")]
    [InlineData("actions: []", "actions: ['*']")]
    public void SecurityPostureMutationsAreRejected(string original, string replacement)
    {
        Assert.Contains(original, Template);
        Assert.False(Approved(Template.Replace(original, replacement, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task LiveProbeWithoutExplicitAuthorityIsUnexecuted()
    {
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(Root, "scripts/azure/Test-DocumentIntelligence.ps1"));
        start.ArgumentList.Add("-Mode"); start.ArgumentList.Add("Live");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(deadline.Token);
        Assert.Equal(3, process.ExitCode);
        Assert.Contains("LIVE UNEXECUTED", await output);
        Assert.Empty(await error);
    }

    private static bool Approved(string source)
    {
        var code = Regex.Replace(source, @"(?m)^\s*//.*$", "");
        return new[] { "@allowed(['canadacentral'])", "kind: 'FormRecognizer'", "name: 'S0'",
            "publicNetworkAccess: 'Disabled'", "disableLocalAuth: true", "defaultAction: 'Deny'",
            "restrictOutboundNetworkAccess: true", "allowedFqdnList: []", "ipRules: []", "virtualNetworkRules: []",
            "actions: []", "scope: account", "principalType: 'ServicePrincipal'" }.All(code.Contains) &&
            !code.Contains("diagnosticSettings", StringComparison.Ordinal) && !code.Contains("listkeys", StringComparison.OrdinalIgnoreCase) &&
            !code.Contains("preview", StringComparison.OrdinalIgnoreCase) && !code.Contains("'Microsoft.CognitiveServices/*'", StringComparison.Ordinal);
    }
}
