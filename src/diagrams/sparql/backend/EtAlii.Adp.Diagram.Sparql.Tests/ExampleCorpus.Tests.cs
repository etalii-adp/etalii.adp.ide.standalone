using EtAlii.Adp.Diagram.Sparql;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// Every vendored example opens: it parses, it draws something, and it reports nothing above
/// info level - the promise Requirement 8.5 makes about the corpus. Each folder's license and
/// provenance files are asserted present, because a vendored file without them is a licensing
/// problem rather than a missing nicety (Requirement 8.2).
/// </summary>
public class ExampleCorpusTests
{
    private static string ExamplesRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(IoPath.Combine(directory.FullName, "examples")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return IoPath.Combine(directory!.FullName, "examples");
    }

    public static TheoryData<string> EveryQuery()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(ExamplesRoot(), "*.rq", SearchOption.AllDirectories))
        {
            data.Add(path);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryQuery))]
    public void EveryExample_ParsesDrawsAndReportsNothingSerious(string path)
    {
        // Arrange.
        var text = File.ReadAllText(path);

        // Act.
        var model = SparqlParser.Parse(text);
        var projection = SparqlProjection.Project(model);
        var problems = SparqlValidator.Judge(model, text);

        // Assert.
        Assert.NotEmpty(projection.Nodes);
        Assert.False(projection.Truncated);
        Assert.DoesNotContain(problems, problem => problem.Severity > DiagramProblemSeverity.Info);
    }

    [Theory]
    [MemberData(nameof(EveryQuery))]
    public void EveryExample_HasItsRegistrationBesideIt(string path)
    {
        // Arrange & act.
        var registration = IoPath.ChangeExtension(path, ".adp");

        // Assert.
        Assert.True(File.Exists(registration), $"{IoPath.GetFileName(path)} has no .adp beside it");
        var lines = File.ReadAllLines(registration);
        Assert.Equal("w3c/sparql", lines[0]);
        Assert.Equal($"body: {IoPath.GetFileName(path)}", lines[1]);
    }

    [Fact]
    public void EveryVendoredFolder_CarriesItsLicenseAndProvenance()
    {
        // Arrange: a folder holding found-online queries owes both files; the one folder whose
        // content ADP wrote itself owes only the readme that says so.
        foreach (var folder in Directory.EnumerateDirectories(ExamplesRoot()))
        {
            var name = new DirectoryInfo(folder).Name;

            // Act & assert.
            Assert.True(File.Exists(IoPath.Combine(folder, "readme.md")), $"{name} has no provenance readme");
            if (name != "adp")
            {
                Assert.True(File.Exists(IoPath.Combine(folder, "LICENSE.md")), $"{name} has no LICENSE.md");
            }
        }
    }

    [Fact]
    public void TheCorpus_CoversEveryQueryFormAndEveryDrawingRule()
    {
        // Arrange.
        var models = Directory
            .EnumerateFiles(ExamplesRoot(), "*.rq", SearchOption.AllDirectories)
            .Select(path => SparqlParser.Parse(File.ReadAllText(path)))
            .ToList();

        // Act & assert: Requirement 8.3's coverage set, asserted rather than assumed.
        Assert.Contains(models, model => model.Form == SparqlQueryForm.Select);
        Assert.Contains(models, model => model.Form == SparqlQueryForm.Construct);
        Assert.Contains(models, model => model.Form == SparqlQueryForm.Ask);
        Assert.Contains(models, model => model.Form == SparqlQueryForm.Describe);

        Assert.Contains(models, model => Scopes(model.Where).Any(scope => scope.Kind == GroupScopeKind.Optional));
        Assert.Contains(models, model => Scopes(model.Where).Any(scope => scope.Kind == GroupScopeKind.Union));
        Assert.Contains(models, model => Scopes(model.Where).Any(scope =>
            scope.Constraints.Any(constraint => constraint.Kind == SparqlConstraintKind.Filter)));
        Assert.Contains(models, model => Scopes(model.Where).Any(scope =>
            scope.Patterns.Any(pattern => pattern.Predicate is PathTerm)));
        Assert.Contains(models, model => model.ModifierRows.Any(row => row.StartsWith("GROUP BY", StringComparison.Ordinal)));
    }

    private static IEnumerable<GroupScope> Scopes(GroupScope scope)
    {
        yield return scope;
        foreach (var nested in scope.Children.SelectMany(Scopes))
        {
            yield return nested;
        }
    }
}
