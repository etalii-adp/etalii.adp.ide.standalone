using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.C4.Tests;

/// <summary>
/// ADP's parser against a realistic model - the C4 worked example, with all four static levels,
/// a dynamic view, a deployment environment and a styles block in one document.
/// </summary>
/// <remarks>
/// The point of a large fixture is not coverage of constructs one at a time, which the small
/// ones already give. It is that a realistic document combines them in ways a parser written
/// against a tidy mental model gets wrong - and this one is certified valid by the real
/// Structurizr CLI, so a disagreement here is ADP's to fix.
/// </remarks>
public class BigBankPlcTests
{
    private static readonly C4Workspace Workspace =
        C4Parser.Parse(C4Document.Parse(File.ReadAllText(IoPath.Combine("Fixtures", "big-bank-plc.dsl"))));

    [Fact]
    public void TheFourStaticLevels_AllParse()
    {
        // Arrange, act and assert.
        Assert.Equal(C4ElementKind.Person, Workspace.Find("customer")!.Kind);
        Assert.Equal(C4ElementKind.SoftwareSystem, Workspace.Find("internetBankingSystem")!.Kind);
        Assert.Equal(C4ElementKind.Container, Workspace.Find("apiApplication")!.Kind);
        Assert.Equal(C4ElementKind.Component, Workspace.Find("securityComponent")!.Kind);
    }

    [Fact]
    public void TheHierarchyIsNested_ThreeLevelsDeep()
    {
        // Arrange, act and assert.
        Assert.Equal("internetBankingSystem", Workspace.Find("apiApplication")!.ParentId);
        Assert.Equal("apiApplication", Workspace.Find("securityComponent")!.ParentId);
    }

    [Fact]
    public void ExistingSystemsAreExternal_AndTheSubjectIsNot()
    {
        // Arrange, act and assert.
        Assert.True(Workspace.Find("mainframeBankingSystem")!.IsExternal);
        Assert.True(Workspace.Find("emailSystem")!.IsExternal);
        Assert.False(Workspace.Find("internetBankingSystem")!.IsExternal);
    }

    [Fact]
    public void ContainersKeepTheirTechnology_AndTheirTags()
    {
        // Act.
        var database = Workspace.Find("database")!;

        // Assert.
        Assert.Equal("Oracle Database Schema", database.Technology);
        Assert.Contains("Database", database.Tags);
        Assert.Equal("Cylinder", C4Theme.ShapeOf(database));
    }

    [Fact]
    public void ADeploymentNodeWithAnInstanceCount_DoesNotMistakeTheCountForATag()
    {
        // Arrange and act.
        // `deploymentNode <name> [description] [technology] [tags] [instances]` - the count sits
        // after the tags, and reading the two the wrong way round turns "4" into a tag and the
        // tags into nothing. The kind of mistake only a realistic model exposes.
        var webServer = Workspace.Elements.Single(element =>
            element.Kind == C4ElementKind.DeploymentNode && element.Name == "bigbank-web***");

        // Assert.
        Assert.Equal("Ubuntu 16.04 LTS", webServer.Technology);
        Assert.DoesNotContain("4", webServer.Tags);
        Assert.Empty(webServer.Tags);
    }

    [Fact]
    public void DeploymentNodesNest_AndInstancesResolveToTheirContainers()
    {
        // Act and assert, step by step.
        var tomcat = Workspace.Elements.First(element =>
            element.Kind == C4ElementKind.DeploymentNode && element.Name == "Apache Tomcat");
        Assert.NotNull(tomcat.ParentId);

        var instances = Workspace.Elements.Where(element => element.Kind == C4ElementKind.ContainerInstance).ToArray();
        Assert.Contains(instances, instance => instance.ReferencedId == "database");
        // The database is deployed twice - primary and secondary.
        Assert.Equal(2, instances.Count(instance => instance.ReferencedId == "database"));
    }

    [Fact]
    public void EveryDeclaredView_IsRead()
    {
        // Arrange, act and assert.
        Assert.Equal(
            ["SystemLandscape", "SystemContext", "Containers", "Components", "SignIn", "LiveDeployment"],
            Workspace.Views.Select(view => view.Key));
    }

    [Fact]
    public void TheDynamicView_KeepsItsInteractionsInOrder()
    {
        // Act.
        var signIn = Workspace.FindView("SignIn")!;

        // Assert.
        Assert.Equal(C4ViewKind.Dynamic, signIn.Kind);
        Assert.Equal(6, signIn.Interactions.Count);
        Assert.Equal("singlePageApplication", signIn.Interactions[0].SourceId);
        Assert.Equal("Submits credentials to", signIn.Interactions[0].Description);
        Assert.Equal(["1", "2", "3", "4", "5", "6"], signIn.Interactions.Select(i => i.Order));
    }

    [Fact]
    public void AutoLayoutWithNoDirection_IsStillRead()
    {
        // Arrange, act and assert.
        // The example writes a bare `autoLayout`, which is legal and means the default direction.
        Assert.NotNull(Workspace.FindView("Containers")!.AutoLayout);
    }

    [Fact]
    public void TheStylesBlock_IsRead_SoTheDocumentsPaletteWins()
    {
        // Arrange, act and assert.
        Assert.Contains(Workspace.Styles, style => style.Tag == "Person");
        Assert.Contains(Workspace.Styles, style => style.Tag == "Existing System");
        Assert.Contains(Workspace.Styles, style => style.Tag == "Database");
    }

    [Fact]
    public void RelationshipsAcrossAllLevels_AreRead()
    {
        // Arrange, act and assert.
        Assert.Contains(Workspace.Relationships, r => r.SourceId == "customer" && r.DestinationId == "internetBankingSystem");
        Assert.Contains(Workspace.Relationships, r => r.SourceId == "securityComponent" && r.DestinationId == "database" && r.Technology == "SQL/TCP");
        // Declared inside the deployment environment, between two deployment nodes.
        Assert.Contains(Workspace.Relationships, r => r.SourceId == "primaryDatabaseServer" && r.DestinationId == "secondaryDatabaseServer");
    }

    [Fact]
    public void EveryViewLaysOutWithoutOverlaps()
    {
        // Act and assert, step by step.
        foreach (var view in Workspace.Views)
        {
            var layout = C4LayoutEngine.Compute(Workspace, view, C4Metrics.Default);
            var boxes = layout.Boxes.ToArray();
            for (var i = 0; i < boxes.Length; i++)
            {
                for (var j = i + 1; j < boxes.Length; j++)
                {
                    Assert.False(
                        boxes[i].Value.Overlaps(boxes[j].Value),
                        $"'{view.Key}': '{boxes[i].Key}' {boxes[i].Value} overlaps '{boxes[j].Key}' {boxes[j].Value}");
                }
            }
        }
    }

    [Fact]
    public void TheModelIsClean_ByC4sOwnRules_SaveForOneKnownWarning()
    {
        // Arrange.
        // The worked example is what C4 holds up as done properly, so anything reported here is
        // either a real gap in the fixture or a rule of ADP's that C4 does not actually have.
        // Three of ADP's rules were found over-reaching this way and were narrowed or removed.
        //
        // What survives is one warning, and it is the right call rather than a fourth
        // over-reach: every container-to-container relationship in the example names its
        // protocol - "JSON/HTTPS", "SQL/TCP" - except the web application delivering the
        // single-page application to the browser, which the example leaves bare. It travels
        // over HTTPS like everything else, so a warning suggesting the document say so is
        // useful. Pinning it exactly means a *new* complaint fails this test.
        var problems = C4RuleSet.Validate(Workspace)
            .Where(problem => problem.RuleId != C4Rules.EmptyView)
            .ToArray();

        // Act and assert, step by step.
        var problem = Assert.Single(problems);
        Assert.Equal(C4Rules.MissingProtocol, problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Contains("Web Application", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Single-Page Application", problem.Message, StringComparison.Ordinal);
    }
}
