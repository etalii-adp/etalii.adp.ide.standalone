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
    /// <summary>
    /// This module's own examples folder, found by walking up from the test binary on a
    /// distinctive multi-segment path rather than on a folder name (test-suite-conformance
    /// Requirement 3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to walk up looking for any directory called <c>examples</c>, and a second one
    /// sits further up the very same walk at <c>src/examples</c> - the showcase tree. It was
    /// correct only because the nearer directory wins, which is not a property anyone had
    /// checked; move or rename this module's folder and the guard would have gone on asserting
    /// happily about somebody else's corpus.
    /// </para>
    /// <para>
    /// The two-part form - a folder that holds both <c>examples</c> and <c>backend</c> - was
    /// considered and is <b>not</b> enough here, which is worth recording because a sibling
    /// guard uses it: <c>src</c> itself holds both <c>examples</c> and <c>backend</c>, so that
    /// form is also only correct because the module's own directory is nearer. It moves the
    /// false match one step further away rather than removing it. The full path names exactly
    /// one directory in the tree, and fails loudly instead of quietly guarding the wrong one.
    /// </para>
    /// </remarks>
    internal static string ExamplesRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "src", "diagrams", "sparql", "examples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            "src/diagrams/sparql/examples was not found above " + AppContext.BaseDirectory);
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
        var folders = Directory.EnumerateDirectories(ExamplesRoot()).ToArray();

        // Assert, first, that there was anything to check. Every assertion below sits inside the
        // loop, so an empty sweep asserted the licence rule about no corpus at all and passed -
        // and this is the guard for a house rule that is not negotiable, which makes a silent
        // pass here worse than a silent pass almost anywhere else in the suite.
        //
        // Three corpora today: adp, uniprot, w3c-sparql. If one is deliberately retired, re-read
        // this guard rather than lowering the number - the point of the floor is that losing a
        // corpus is a thing somebody decides, not a thing that happens.
        Assert.True(
            folders.Length >= 3,
            $"Only {folders.Length} example folders were found under {ExamplesRoot()}; this guard has stopped finding the corpus whose licensing it checks.");

        foreach (var folder in folders)
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
