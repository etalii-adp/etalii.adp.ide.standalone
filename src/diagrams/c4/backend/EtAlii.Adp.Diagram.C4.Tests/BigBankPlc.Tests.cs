using EtAlii.Adp.Common;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

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
            element is { Kind: C4ElementKind.DeploymentNode, Name: "bigbank-web***" });

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
            element is { Kind: C4ElementKind.DeploymentNode, Name: "Apache Tomcat" });
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
        Assert.Contains(Workspace.Styles, style =>
        {
            ArgumentNullException.ThrowIfNull(style);
            return style.Tag == "Person";
        });
        Assert.Contains(Workspace.Styles, style => style.Tag == "Existing System");
        Assert.Contains(Workspace.Styles, style => style.Tag == "Database");
    }

    [Fact]
    public void RelationshipsAcrossAllLevels_AreRead()
    {
        // Arrange, act and assert.
        Assert.Contains(Workspace.Relationships, r =>
        {
            ArgumentNullException.ThrowIfNull(r);
            return r is { SourceId: "customer", DestinationId: "internetBankingSystem" };
        });
        Assert.Contains(Workspace.Relationships, r => r is { SourceId: "securityComponent", DestinationId: "database", Technology: "SQL/TCP" });
        // Declared inside the deployment environment, between two deployment nodes.
        Assert.Contains(Workspace.Relationships, r => r is { SourceId: "primaryDatabaseServer", DestinationId: "secondaryDatabaseServer" });
    }

    [Fact]
    public void EveryViewLaysOutWithoutOverlaps()
    {
        // Arrange. The floor: a workspace that parsed to no views has nothing to overlap, so
        // this passes exactly as loudly as a model whose every view lays out cleanly.
        Assert.True(
            Workspace.Views.Count >= 4,
            $"Only {Workspace.Views.Count} views were parsed from the Big Bank workspace; this guard has stopped finding the views it lays out.");

        // Act and assert, step by step.
        var comparableViews = 0;
        foreach (var view in Workspace.Views)
        {
            var layout = C4LayoutEngine.Compute(Workspace, view, C4Metrics.Default);
            var boxes = layout.Boxes.ToArray();
            if (boxes.Length >= 2)
            {
                comparableViews++;
            }

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

        // The floor belongs on the aggregate, not on each view. `SignIn` is a dynamic view and
        // lays out to no boxes at all, which is legitimate - so requiring every view to yield a
        // comparable pair asserts something untrue of this model. What must not happen is that
        // *no* view yields one, which is the state in which "no view overlaps" means nothing.
        Assert.True(
            comparableViews > 0,
            "No view laid out two or more boxes, so no pair was compared and nothing could have overlapped.");
    }

    [Fact]
    public void TheWorkedExample_ReportsWhatStructurizrAlsoReports()
    {
        // Arrange.
        // This test used to assert the worked example was clean but for one warning, and that
        // assertion is what made three of ADP's rules get narrowed: they fired here, and
        // "C4's own example trips it" was read as "the rule is wrong".
        //
        // Structurizr's own inspector reports 26 findings on this same document. So the
        // example is authoritative about *syntax* - `validate` accepts it, which is what makes
        // it a good round-trip fixture - and not about quality. What is asserted now is the
        // set of rules reported, so a *new* kind of complaint fails this test while the known
        // ones do not.
        var reported = C4RuleSet.Validate(Workspace)
            .Where(problem => problem.RuleId != C4Rules.EmptyView)
            .Select(problem => problem.RuleId)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Act and assert.
        // Deployment nodes named by what they are and left undescribed - which Structurizr
        // reports as model.deploymentnode.description - and the one container relationship the
        // example leaves without a technology.
        Assert.Equal(
            [C4Rules.MissingDeploymentDescription, C4Rules.MissingProtocol],
            reported);
    }

    [Fact]
    public void EveryProblemOnTheWorkedExample_IsAWarningRatherThanAnError()
    {
        // Act and assert.
        // Structurizr prints all of these as ERROR. ADP reports them as warnings because they
        // are recommendations rather than syntax faults, and that divergence is a decision on
        // the record rather than an accident (quality-gates Requirement 1.9).
        Assert.All(
            C4RuleSet.Validate(Workspace),
            problem => Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity));
    }
}
