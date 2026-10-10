using Xunit;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>
/// The examples the module ships (agent-activity-diagram Requirements 10.5 and 10.6): each one
/// opens without an error and is written back as it was, the copies the showcase seeds are the
/// module's, and between them they show everything the requirement lists.
/// </summary>
public sealed class AadExamplesTests
{
    private static string ModuleFolder
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (directory.Name == "agent-activity-diagram")
                {
                    return directory.FullName;
                }
            }

            throw new InvalidOperationException($"No agent-activity-diagram folder above {AppContext.BaseDirectory}. That is a failure, not a skip.");
        }
    }

    private static string[] Examples => Directory.GetFiles(Path.Combine(ModuleFolder, "examples"), "*.aad", SearchOption.AllDirectories);

    public static TheoryData<string> ExampleNames() => [.. Examples.Select(path => Path.GetFileNameWithoutExtension(path))];

    private static string Text(string name) => File.ReadAllText(Path.Combine(ModuleFolder, "examples", name, name + ".aad"));

    [Fact]
    public void TheModuleShipsItsThreeExamples_EachWithAReadmeAndARegistration()
    {
        Assert.Equal(["adp-one-day", "every-state", "two-projects"], Examples.Select(path => Path.GetFileNameWithoutExtension(path)).Order(StringComparer.Ordinal));
        Assert.All(Examples, path =>
        {
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "readme.md")), path);
            Assert.True(File.Exists(Path.ChangeExtension(path, ".adp")), path);
        });
        Assert.True(File.Exists(Path.Combine(ModuleFolder, "examples", "updating-this-file.md")));
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void AnExample_OpensWithoutAnError_AndIsWrittenBackAsItWas(string name)
    {
        // Arrange.
        var text = Text(name);

        // Act.
        var entry = AadDocumentEntry.Read(text);
        var problems = AadValidator.Validate(entry.Document);

        // Assert: what an example has to say for itself is information at most.
        Assert.True(entry.IsUsable, entry.Unreadable);
        Assert.DoesNotContain(problems, problem => problem.Severity is DiagramProblemSeverity.Error or DiagramProblemSeverity.Warning);
        Assert.Equal(text, entry.Document.Text);
        Assert.NotEmpty(entry.Model.Elements);
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void TheCopyTheShowcaseSeeds_IsTheModulesOwn(string name)
    {
        var seeded = Path.Combine(ModuleFolder, "..", "..", "examples", "diagrams", "agent-activity-diagram", name);

        Assert.Equal(Text(name), File.ReadAllText(Path.Combine(seeded, name + ".aad")));
        Assert.Equal(File.ReadAllText(Path.Combine(ModuleFolder, "examples", name, name + ".adp")), File.ReadAllText(Path.Combine(seeded, name + ".adp")));
    }

    [Fact]
    public void BetweenThem_TheExamplesShowEverythingTheNotationHas()
    {
        // Arrange: every example's model, and the file's own words where the model has folded them away.
        var models = Examples.Select(path => AadDocumentEntry.Read(File.ReadAllText(path)).Model).ToList();
        var elements = models.SelectMany(model => model.Elements).ToList();
        var rows = elements.SelectMany(element => element.Rows).ToList();
        var links = elements.SelectMany(element => (string[])[element.Link, element.BranchLink, element.FolderLink]).Where(link => link.Length > 0).ToList();
        var rowLinks = rows.Select(row => row.Link).Where(link => link.Length > 0).ToList();

        // Assert, item by item of Requirement 10.5. Each names what is missing when it fails.
        var missing = new List<string>();

        foreach (var status in AadDefinition.SpecificationStatus.Members)
        {
            Shows(elements.Any(element => element.Kind == AadKind.Specification && element.Status == status.Name), $"a specification that is {status.Label}");
        }

        foreach (var status in AadDefinition.TaskStatus.Members)
        {
            Shows(elements.Where(element => element.Kind == AadKind.Specification).SelectMany(element => element.Rows).Any(row => row.Status == status.Name), $"a task that is {status.Label}");
        }

        foreach (var kind in AadDefinition.EnvironmentKind.Members)
        {
            Shows(elements.Any(element => element.Kind == AadKind.Environment && element.KindLabel == kind.Label), $"an environment of kind {kind.Label}");
        }

        Shows(models.Any(model => model.Relations.Where(relation => relation.Type == "SpecificationAgent").GroupBy(relation => relation.From).Any(group => group.Count() > 1)), "a specification with several agents");
        Shows(models.Any(model => model.Relations.Where(relation => relation.Type == "AgentLocation").GroupBy(relation => relation.From).Any(group => group.Count() > 1)), "an agent with several locations");
        Shows(models.Any(model => model.Relations.Where(relation => relation.Type == "LocationEnvironment").GroupBy(relation => relation.To).Any(group => group.Count() > 1)), "an environment shared by several locations");
        Shows(elements.Any(element => element is { Kind: AadKind.Location, Folder: "Default" }), "a location in the folder Default");
        Shows(elements.Any(element => element.Kind == AadKind.Location && element.Folder != "Default"), "a location in a worktree");
        Shows(elements.Any(element => element is { Kind: AadKind.Location, Rows.Count: > 0 }), "a pull request");
        Shows(links.Any(Web), "a web link on an element");
        Shows(links.Any(ProjectPath), "a project path on an element");
        Shows(links.Any(link => !Web(link) && !ProjectPath(link)), "a file location on an element");
        Shows(rowLinks.Any(Web), "a web link on a list item");
        Shows(rowLinks.Any(ProjectPath), "a project path on a list item");
        Shows(models.Any(model => model.Placements.Count > 0 && model.Placements.Count < model.Elements.Count), "locked beside unlocked elements");
        Shows(models.Any(model => model.Collapsed.Any(groups => groups.Value.Any(group => !AadDefinition.CollapsedByDefault.Contains(group)))), "a group folded that comes unfolded");
        Shows(models.Any(model => model.Collapsed.Any(groups => GroupsOf(model, groups.Key).Any(group => AadDefinition.CollapsedByDefault.Contains(group) && !groups.Value.Contains(group)))), "a group unfolded that comes folded");
        Shows(models.Count(model => model.Elements.Count(element => element.Kind == AadKind.Project) > 1) > 0, "more than one project");

        Assert.True(missing.Count == 0, "No example shows: " + string.Join("; ", missing));
        return;

        void Shows(bool shown, string what) { if (!shown) missing.Add(what); }

        static IEnumerable<string> GroupsOf(AadModel model, string id) => model.Elements.Single(element => element.Id == id).Kind == AadKind.Specification ? AadDefinition.TaskStatus.Members.Select(member => member.Name) : [AadDefinition.PullRequestsGroup];
        static bool ProjectPath(string link) => !link.Contains(':', StringComparison.Ordinal) && !link.StartsWith('/');
        static bool Web(string link) => link.StartsWith("https://", StringComparison.Ordinal);
    }
}
