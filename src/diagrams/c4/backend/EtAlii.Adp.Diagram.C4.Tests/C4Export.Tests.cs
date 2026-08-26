using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// What Structurizr drew from each fixture, checked against what ADP says the views contain.
/// </summary>
/// <remarks>
/// <para>
/// `validate` proves another tool can parse what ADP wrote, and `inspect` proves the two agree
/// about quality. Neither draws anything, and a document that parses perfectly can still produce
/// a picture with nothing in it. ADP shipped exactly that: a `c4/deployment` template whose
/// `deploymentEnvironment` had no nodes, so the view bound to nothing and rendered empty while
/// every command exited zero.
/// </para>
/// <para>
/// So the assertion is not "the export succeeded". It is that every element ADP believes is on a
/// view appears in the diagram Structurizr drew for it - which is the same question a person
/// answers by opening Structurizr Lite and looking, asked without a JDK, a browser or a person.
/// </para>
/// </remarks>
public class C4ExportTests
{
    public static TheoryData<string> Corpus() => C4DocumentTests.Corpus();

    private static C4Workspace Parse(string fixture) =>
        C4Parser.Parse(C4Document.Parse(File.ReadAllText(IoPath.Combine("Fixtures", fixture))));

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EveryFixture_DrawsSomething(string fixture)
    {
        // Act.
        var diagrams = C4Export.Read(fixture);

        // Assert.
        // Not one of them may be an empty frame. `graph LR` plus a title is a diagram file that
        // exports, commits and diffs perfectly while showing a reader nothing at all.
        Assert.All(diagrams, diagram => Assert.True(
            diagram.Value.Contains("font-weight: bold", StringComparison.Ordinal),
            $"'{fixture}' exported '{diagram.Key}' with no elements drawn in it at all. {C4Export.RegenerationHint}"));
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EveryElementAdpPutsOnAView_WasDrawn(string fixture)
    {
        // Arrange.
        var workspace = Parse(fixture);
        var diagrams = C4Export.Read(fixture);
        var everything = string.Concat(diagrams.Values);

        // Act.
        // Instances are excluded: Structurizr draws a container instance labelled with the
        // container it instantiates, so looking for the instance's own name would fail on a
        // diagram that is drawing exactly the right thing.
        var undrawn = workspace.Views
            .SelectMany(view => C4RuleSet.MembersOf(workspace, view))
            .Where(element => element.Kind is not (C4ElementKind.ContainerInstance or C4ElementKind.SoftwareSystemInstance))
            .Select(element => element.Name)
            .Distinct(StringComparer.Ordinal)
            .Where(name => name.Length > 0 && !C4Export.Draws(everything, name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert.
        Assert.True(
            undrawn.Length == 0,
            $"ADP puts [{string.Join(", ", undrawn)}] on a view of '{fixture}', and Structurizr drew none of them. " +
            $"Either ADP is reading the view wrongly or the document does not say what it appears to. {C4Export.RegenerationHint}");
    }

    [Fact]
    public void AnEmptyDiagram_IsRecognisedAsEmpty()
    {
        // Arrange.
        // The guard on the guard. `EveryFixture_DrawsSomething` is only worth anything if it can
        // tell a drawing from an empty frame, so here is the empty frame the CLI produces for a
        // view that binds to nothing.
        const string empty = """
            graph LR
              linkStyle default fill:#ffffff

              subgraph diagram ["Deployment View: Live"]
                style diagram fill:#ffffff,stroke:#ffffff

              end
            """;

        // Act and assert, step by step.
        Assert.DoesNotContain("font-weight: bold", empty, StringComparison.Ordinal);
        Assert.False(C4Export.Draws(empty, "Web Application"));
    }

    [Fact]
    public void AnElementNamedOnlyInARelationship_DoesNotCountAsDrawn()
    {
        // Arrange.
        // `Draws` matches the element's own label rather than the text anywhere in the file,
        // because an arrow describing "Sends data to the Database" must not be enough to call the
        // Database drawn.
        const string diagram = """
            graph LR
              1["<div style='font-weight: bold'>Web Application</div><div style='font-size: 70%; margin-top: 0px'>[Container]</div>"]
              1-. "<div>Reads from the Database</div>" .->1
            """;

        // Act and assert, step by step.
        Assert.True(C4Export.Draws(diagram, "Web Application"));
        Assert.False(C4Export.Draws(diagram, "Database"));
    }

    [Fact]
    public void AFixtureWithNoExports_FailsRatherThanPassingQuietly()
    {
        // Act and assert.
        var problem = Assert.Throws<DirectoryNotFoundException>(() => C4Export.Read("no-such-fixture.dsl"));
        Assert.Contains("no-such-fixture", problem.Message, StringComparison.Ordinal);
        Assert.Contains("structurizr export", problem.Message, StringComparison.Ordinal);
    }
}
