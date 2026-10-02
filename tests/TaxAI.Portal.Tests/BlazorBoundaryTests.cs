using System.Reflection;

namespace TaxAI.Portal.Tests;

public sealed class BlazorBoundaryTests
{
    public static TheoryData<string, string[]> ComponentParameters() => new()
    {
        {
            "TaxAI.Portal.Components.Pages.IntakeCaseCreatePage",
            []
        },
        {
            "TaxAI.Portal.Components.Pages.IntakeCaseIssuedPage",
            [
                "CaseId:System.Guid",
                "ClientUrl:System.String",
                "LinkExpiresAtUtc:System.DateTimeOffset",
            ]
        },
        {
            "TaxAI.Portal.Components.Pages.IntakeCaseProgressPage",
            [
                "CaseId:System.Guid",
                "Model:ClientIntake.Models.AccountantIntakeView",
            ]
        },
        {
            "TaxAI.Portal.Components.Pages.ClientLinkConfirmationPage",
            [
                "RedemptionHandle:System.String",
                "TaxYear:System.Int32",
            ]
        },
        {
            "TaxAI.Portal.Components.Pages.ClientIntakePage",
            ["Model:ClientIntake.Models.ClientIntakeView"]
        },
        {
            "TaxAI.Portal.Components.Shared.ClientQuestionnairePanel",
            ["Model:ClientIntake.Models.ClientIntakeView"]
        },
        {
            "TaxAI.Portal.Components.Shared.ExpectedDocumentChecklist",
            [
                "Items:System.Collections.Generic.IReadOnlyList`1[[ClientIntake.Models.ExpectedDocumentView",
            ]
        },
        {
            "TaxAI.Portal.Components.Shared.IntakeStatusBadge",
            ["Status:ClientIntake.Models.ExpectedDocumentStatus"]
        },

        // Spec 017 T03: the case list takes its closed model; the accountant
        // navigation takes nothing; the notice takes only the closed kind.
        {
            "TaxAI.Portal.Components.Pages.CaseListPage",
            ["Model:TaxAI.Portal.Components.Pages.CaseListPageModel"]
        },
        {
            "TaxAI.Portal.Components.Shared.AccountantNavigation",
            []
        },
        {
            "TaxAI.Portal.Components.Shared.WorkflowNotice",
            ["Kind:TaxAI.Portal.Components.Pages.WorkflowNoticeKind"]
        },
    };

    [Theory]
    [MemberData(nameof(ComponentParameters))]
    public void IntakeComponentParameterAllowlistsAreExact(
        string typeName,
        string[] expected)
    {
        var type = typeof(Program).Assembly.GetType(
            typeName,
            throwOnError: true)!;
        var parameters = type.GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly)
            .Where(property => property.GetCustomAttributes().Any(attribute =>
                attribute.GetType().Name == "ParameterAttribute"))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        if (typeName.EndsWith(
                "ExpectedDocumentChecklist",
                StringComparison.Ordinal))
        {
            var parameter = Assert.Single(parameters);
            Assert.Equal("Items", parameter.Name);
            Assert.StartsWith(
                expected[0].Split(':')[1],
                parameter.PropertyType.AssemblyQualifiedName,
                StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(
                expected,
                parameters.Select(property =>
                    $"{property.Name}:{property.PropertyType.FullName}"));
        }

        Assert.All(
            parameters,
            property => Assert.Contains(
                property.GetCustomAttributes(),
                attribute =>
                    attribute.GetType().Name ==
                    "EditorRequiredAttribute"));
        Assert.DoesNotContain(
            parameters,
            property => property.GetCustomAttributes().Any(attribute =>
                attribute.GetType().Name == "ParameterAttribute" &&
                (bool?)attribute.GetType()
                    .GetProperty("CaptureUnmatchedValues")
                    ?.GetValue(attribute) == true));
    }

    [Fact]
    public void OnlyTheQuestionnairePanelIsInteractive()
    {
        var componentRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "TaxAI.Portal",
            "Components");
        var renderModes = Directory
            .EnumerateFiles(componentRoot, "*.razor", SearchOption.AllDirectories)
            .Where(path => ExecutableMarkup(path).Contains(
                "@rendermode",
                StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileName(path))
            .ToArray();

        Assert.Equal(
            new[] { "ClientQuestionnairePanel.razor" },
            renderModes);
        var panel = File.ReadAllText(
            Directory.EnumerateFiles(
                    componentRoot,
                    "ClientQuestionnairePanel.razor",
                    SearchOption.AllDirectories)
                .Single());
        Assert.Contains(
            "@rendermode InteractiveServer",
            panel,
            StringComparison.Ordinal);
        Assert.DoesNotContain("@inject", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IClientIntakeWorkflow", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpContext", panel, StringComparison.Ordinal);
    }

    private static string ExecutableMarkup(string path) =>
        System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(path),
            @"@\*.*?\*@",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    [Fact]
    public void EventHandlersAreConfinedToThePresentationOnlyIsland()
    {
        var componentRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "TaxAI.Portal",
            "Components");
        var interactive = Directory
            .EnumerateFiles(componentRoot, "*.razor", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "@onchange",
                StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Equal(
            new[] { "ClientQuestionnairePanel.razor" },
            interactive);
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
