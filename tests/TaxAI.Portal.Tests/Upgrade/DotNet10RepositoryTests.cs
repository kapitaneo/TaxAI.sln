using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TaxAI.Portal.Tests.Upgrade;

public sealed class DotNet10RepositoryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string[] ApprovedProjects =
    [
        "src/CaseProcessing/CaseProcessing.csproj",
        "src/CaseReview/CaseReview.csproj",
        "src/DocumentExtraction/DocumentExtraction.csproj",
        "src/Persistence/Persistence.csproj",
        "src/TaxAI.Portal/TaxAI.Portal.csproj",
        "src/TaxCases/TaxCases.csproj",
        "src/TaxLogicEngine/TaxLogicEngine.csproj",
        "src/Tenancy/Tenancy.csproj",
        "tests/CaseProcessing.Tests/CaseProcessing.Tests.csproj",
        "tests/CaseReview.Tests/CaseReview.Tests.csproj",
        "tests/DocumentExtraction.Tests/DocumentExtraction.Tests.csproj",
        "tests/Persistence.Tests/Persistence.Tests.csproj",
        "tests/TaxAI.Portal.Tests/TaxAI.Portal.Tests.csproj",
        "tests/TaxCases.Tests/TaxCases.Tests.csproj",
        "tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj",
        "tests/Tenancy.Tests/Tenancy.Tests.csproj",
    ];

    private static readonly Dictionary<string, string> TestPackageVersions =
        new(StringComparer.Ordinal)
        {
            ["Microsoft.NET.Test.Sdk"] = "18.8.1",
            ["xunit"] = "2.9.3",
            ["xunit.runner.visualstudio"] = "3.1.5",
            ["coverlet.collector"] = "10.0.1",
        };

    [Fact]
    public void RepositoryUsesTheExactApprovedDotNet10Contract()
    {
        var solutionProjects = SolutionProjects();

        foreach (var approvedProject in ApprovedProjects)
        {
            Assert.Single(
                solutionProjects,
                actual => string.Equals(
                    actual,
                    approvedProject,
                    StringComparison.Ordinal));
        }

        Assert.DoesNotContain(
            solutionProjects,
            actual => !ApprovedProjects.Contains(actual, StringComparer.Ordinal));

        foreach (var projectPath in ApprovedProjects)
        {
            var project = XDocument.Load(
                Path.Combine(RepositoryRoot, projectPath));
            Assert.Equal(
                "net10.0",
                Assert.Single(Elements(project, "TargetFramework")).Value);
            Assert.Empty(Elements(project, "TargetFrameworks"));

            var packages = Elements(project, "PackageReference")
                .ToDictionary(
                    package => package.Attribute("Include")!.Value,
                    package => package.Attribute("Version")!.Value,
                    StringComparer.Ordinal);
            Assert.DoesNotContain("xunit.v3", packages.Keys);

            if (projectPath.StartsWith("tests/", StringComparison.Ordinal))
            {
                foreach (var (name, version) in TestPackageVersions)
                {
                    Assert.True(
                        packages.TryGetValue(name, out var actualVersion),
                        $"{projectPath} is missing {name}.");
                    Assert.Equal(version, actualVersion);
                }
            }
        }

        AssertPackage(
            "src/TaxAI.Portal/TaxAI.Portal.csproj",
            "Microsoft.AspNetCore.Authentication.OpenIdConnect",
            "10.0.10");
        AssertPackage(
            "src/Persistence/Persistence.csproj",
            "Microsoft.EntityFrameworkCore.SqlServer",
            "10.0.10");
        AssertPackage(
            "src/Persistence/Persistence.csproj",
            "Microsoft.EntityFrameworkCore.Design",
            "10.0.10");
        AssertPackage(
            "tests/TaxAI.Portal.Tests/TaxAI.Portal.Tests.csproj",
            "Microsoft.AspNetCore.Mvc.Testing",
            "10.0.10");
        AssertPackage(
            "tests/Persistence.Tests/Persistence.Tests.csproj",
            "Microsoft.EntityFrameworkCore.Sqlite",
            "10.0.10");

        using var globalJson = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot, "global.json")));
        var sdk = globalJson.RootElement.GetProperty("sdk");
        Assert.Equal("10.0.302", sdk.GetProperty("version").GetString());
        Assert.Equal("disable", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());

        using var toolManifest = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    RepositoryRoot,
                    ".config",
                    "dotnet-tools.json")));
        var tools = toolManifest.RootElement.GetProperty("tools")
            .EnumerateObject()
            .ToArray();
        var tool = Assert.Single(tools);
        Assert.Equal("dotnet-ef", tool.Name);
        Assert.Equal("10.0.10", tool.Value.GetProperty("version").GetString());
    }

    private static void AssertPackage(
        string projectPath,
        string packageName,
        string expectedVersion)
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot, projectPath));
        var package = Assert.Single(
            Elements(project, "PackageReference"),
            candidate => string.Equals(
                candidate.Attribute("Include")?.Value,
                packageName,
                StringComparison.Ordinal));
        Assert.Equal(expectedVersion, package.Attribute("Version")?.Value);
    }

    private static XElement[] Elements(XDocument project, string name) =>
        project.Descendants()
            .Where(element => element.Name.LocalName == name)
            .ToArray();

    private static string[] SolutionProjects()
    {
        var solution = File.ReadAllText(
            Path.Combine(RepositoryRoot, "TaxAI.sln"));
        return Regex.Matches(
                solution,
                "^Project\\([^\\r\\n]+\\) = \"[^\"]+\", \"(?<path>[^\"]+\\.csproj)\"",
                RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Select(match => match.Groups["path"].Value.Replace('\\', '/'))
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the TaxAI repository root.");
    }
}
