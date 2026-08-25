using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// Reading the DSL subset ADP models: the model, the deployment model, relationships, views and
/// styles (c4-diagrams tasks 7-11). What a document *means* is not judged here - that is
/// C4RuleSet's job - so an incomplete model still parses.
/// </summary>
public class C4ParserTests
{
    private static C4Workspace ParseFixture(string name) =>
        C4Parser.Parse(C4Document.Parse(File.ReadAllText(IoPath.Combine("Fixtures", name))));

    private static C4Workspace ParseText(string text) => C4Parser.Parse(C4Document.Parse(text));

    // ---- the static model ---------------------------------------------------------------

    [Fact]
    public void Parse_ReadsPeopleSystemsAndContainers_WithTheirDescriptionsAndTechnologies()
    {
        // Arrange.
        var workspace = ParseFixture("comments-everywhere.dsl");

        // Act and assert, step by step.
        var user = workspace.Find("u")!;
        Assert.Equal(C4ElementKind.Person, user.Kind);
        Assert.Equal("User", user.Name);
        Assert.Equal("A user.", user.Description);

        var web = workspace.Find("web")!;
        Assert.Equal(C4ElementKind.Container, web.Kind);
        Assert.Equal("Serves pages.", web.Description);
        Assert.Equal("React", web.Technology);
        Assert.Equal("s", web.ParentId);
    }

    [Fact]
    public void Parse_IsNotFooledByCommentsInAnyPosition()
    {
        // Act.
        // Every comment form appears in this fixture, including one trailing an element and one
        // inside a name that only looks like a comment.
        var workspace = ParseFixture("comments-everywhere.dsl");

        // Assert.
        Assert.Equal(4, workspace.Elements.Count);
        Assert.Equal("Comments", workspace.Name);
    }

    [Fact]
    public void Parse_IgnoresModelKeywordsInsideABlockComment()
    {
        // Arrange and act.
        var workspace = ParseText(
            "workspace \"W\" {\n  model {\n    /* person \"Ghost\"\n       softwareSystem \"Phantom\" */\n    u = person \"Real\"\n  }\n}\n");

        // Assert.
        Assert.Single(workspace.Elements);
        Assert.Equal("Real", workspace.Elements[0].Name);
    }

    [Fact]
    public void Parse_ReadsAnElementWithNoIdentifier_ByGeneratingOne()
    {
        // Arrange.
        var workspace = ParseText("workspace {\n  model {\n    person \"Anonymous\"\n  }\n}\n");

        // Act and assert, step by step.
        var element = Assert.Single(workspace.Elements);
        Assert.Equal("Anonymous", element.Name);
        Assert.NotEmpty(element.Id);
    }

    [Fact]
    public void Parse_TreatsAGroupAsAClusterRatherThanAnAbstractionLevel()
    {
        // Arrange and act.
        // A group is visual: its children belong to whatever contains the group, so nothing
        // inside one should acquire the group as a parent.
        var workspace = ParseText(
            "workspace {\n  model {\n    group \"Team\" {\n      a = softwareSystem \"A\"\n    }\n  }\n}\n");

        // Assert.
        Assert.Null(workspace.Find("a")!.ParentId);
    }

    [Fact]
    public void Parse_ReadsExternalFromTags()
    {
        // Arrange and act.
        var workspace = ParseText(
            "workspace {\n  model {\n    a = softwareSystem \"A\" \"desc\" \"External\"\n    b = softwareSystem \"B\"\n  }\n}\n");

        // Assert.
        Assert.True(workspace.Find("a")!.IsExternal);
        Assert.False(workspace.Find("b")!.IsExternal);
    }

    // ---- the deployment model -----------------------------------------------------------

    [Fact]
    public void Parse_ReadsNestedDeploymentNodes_ToAnyDepth()
    {
        // Arrange.
        var workspace = ParseFixture("deployment-nested.dsl");

        // Act and assert, step by step.
        var node = workspace.Find("node")!;
        Assert.Equal(C4ElementKind.DeploymentNode, node.Kind);
        Assert.Equal("cluster", node.ParentId);
        Assert.Equal("region", workspace.Find("cluster")!.ParentId);
    }

    [Fact]
    public void Parse_ReadsInfrastructureNodes_AsTheirOwnKind()
    {
        // Act.
        var workspace = ParseFixture("deployment-nested.dsl");

        // Assert.
        Assert.Equal(C4ElementKind.InfrastructureNode, workspace.Find("dns")!.Kind);
        Assert.Equal(C4ElementKind.InfrastructureNode, workspace.Find("lb")!.Kind);
    }

    [Fact]
    public void Parse_ReadsAContainerDeployedTwice_AsTwoInstancesOfOneContainer()
    {
        // Arrange.
        // Requirement 9.3: an instance references its container rather than duplicating it.
        var workspace = ParseFixture("deployment-nested.dsl");

        // Act and assert, step by step.
        var instances = workspace.Elements.Where(e => e.Kind == C4ElementKind.ContainerInstance && e.ReferencedId == "db").ToArray();
        Assert.Equal(2, instances.Length);
        Assert.Equal(["primary", "replica"], instances.Select(i => i.ParentId).Order());
    }

    // ---- relationships ------------------------------------------------------------------

    [Fact]
    public void Parse_ReadsRelationships_WithDescriptionAndTechnology()
    {
        // Arrange.
        var workspace = ParseFixture("comments-everywhere.dsl");

        // Act and assert, step by step.
        var relationship = workspace.Relationships.Single(r => r.SourceId == "web" && r.DestinationId == "db");
        Assert.Equal("Reads from and writes to", relationship.Description);
        Assert.Equal("SQL/TCP", relationship.Technology);
    }

    [Fact]
    public void Parse_ReadsARelationshipDeclaredInsideAnElementBlock()
    {
        // Arrange.
        var workspace = ParseText(
            "workspace {\n  model {\n    a = softwareSystem \"A\" {\n      -> b \"Calls\" \"HTTPS\"\n    }\n    b = softwareSystem \"B\"\n  }\n}\n");

        // Act and assert, step by step.
        var relationship = Assert.Single(workspace.Relationships);
        Assert.Equal("a", relationship.SourceId);
        Assert.Equal("b", relationship.DestinationId);
        Assert.Equal("Calls", relationship.Description);
    }

    [Fact]
    public void ARelationshipsId_IsStableAndDistinguishesParallelEdges()
    {
        // Arrange and act.
        var workspace = ParseText(
            "workspace {\n  model {\n    a = softwareSystem \"A\"\n    b = softwareSystem \"B\"\n    a -> b \"One\"\n    a -> b \"Two\"\n  }\n}\n");

        // Assert.
        Assert.Equal(2, workspace.Relationships.Select(r => r.Id).Distinct().Count());
    }

    // ---- views --------------------------------------------------------------------------

    [Fact]
    public void Parse_ReadsEachViewKind_WithItsScope()
    {
        // Arrange.
        var workspace = ParseFixture("comments-everywhere.dsl");

        // Act.
        var context = workspace.FindView("context")!;
        Assert.Equal(C4ViewKind.SystemContext, context.Kind);
        Assert.Equal("s", context.ScopeId);
        Assert.True(context.IncludesEverything);

        // Assert.
        Assert.Equal(C4ViewKind.Container, workspace.FindView("containers")!.Kind);
    }

    [Fact]
    public void Parse_ReadsADeploymentViewsEnvironment()
    {
        // Act.
        var view = ParseFixture("deployment-nested.dsl").FindView("production")!;

        // Assert.
        Assert.Equal(C4ViewKind.Deployment, view.Kind);
        Assert.Equal("s", view.ScopeId);
        Assert.Equal("Production", view.Environment);
    }

    [Fact]
    public void Parse_ReadsALandscapeViewWithNoScope()
    {
        // Arrange.
        var workspace = ParseText("workspace {\n  model {\n  }\n  views {\n    systemLandscape \"all\" {\n      include *\n    }\n  }\n}\n");

        // Act and assert, step by step.
        var view = Assert.Single(workspace.Views);
        Assert.Equal(C4ViewKind.SystemLandscape, view.Kind);
        Assert.Null(view.ScopeId);
    }

    [Fact]
    public void Parse_ReadsADynamicViewsInteractions_InOrder()
    {
        // Act.
        var view = ParseFixture("dynamic-interactions.dsl").FindView("signup")!;

        // Assert.
        Assert.Equal(C4ViewKind.Dynamic, view.Kind);
        Assert.Equal(4, view.Interactions.Count);
        Assert.Equal(["1", "2", "3", "4"], view.Interactions.Select(i => i.Order));
        Assert.Equal("Submits the form", view.Interactions[0].Description);
        Assert.Equal("api", view.Interactions[2].SourceId);
    }

    [Fact]
    public void Parse_ReadsAutoLayout_WithItsDirection()
    {
        // Act.
        var view = ParseFixture("deployment-nested.dsl").FindView("production")!;

        // Assert.
        Assert.NotNull(view.AutoLayout);
        Assert.Equal("lr", view.AutoLayout!.Direction);
    }

    [Fact]
    public void Parse_LeavesAutoLayoutNull_WhenTheDocumentDeclaresNone()
    {
        // Act.
        var view = ParseFixture("comments-everywhere.dsl").FindView("context")!;

        // Assert.
        Assert.Null(view.AutoLayout);
    }

    [Fact]
    public void TheFirstView_IsWhatABareDocumentOpens()
    {
        // Act.
        // Requirement 2.6: a .dsl without an .adp defaults to the first declared view.
        var workspace = ParseFixture("comments-everywhere.dsl");

        // Assert.
        Assert.Equal("context", workspace.Views[0].Key);
    }

    // ---- styles -------------------------------------------------------------------------

    [Fact]
    public void Parse_ReadsTagBasedElementStyles()
    {
        // Act.
        var workspace = ParseFixture("unmodelled-constructs.dsl");

        // Assert.
        Assert.Equal(["Person", "External"], workspace.Styles.Select(style => style.Tag));
    }

    [Fact]
    public void Parse_ReadsNoStyles_WhenTheDocumentDeclaresNone()
    {
        // Arrange, act and assert.
        Assert.Empty(ParseFixture("minimal.dsl").Styles);
    }

    // ---- what it does not model ---------------------------------------------------------

    [Fact]
    public void Parse_IgnoresConstructsItDoesNotModel_WithoutLosingTheOnesItDoes()
    {
        // Act.
        // !identifiers, !docs, !adrs, theme and configuration all appear in this fixture; the
        // document carries them, and the parser must neither choke nor let them shift the
        // parent chain (Requirement 3.3).
        var workspace = ParseFixture("unmodelled-constructs.dsl");

        // Assert.
        Assert.Equal("u", workspace.Find("u")!.Id);
        Assert.Equal("s", workspace.Find("web")!.ParentId);
        Assert.Single(workspace.Views);
    }

    [Fact]
    public void Parse_SurvivesUnusualIndentation()
    {
        // Act.
        var workspace = ParseFixture("unusual-indentation.dsl");

        // Assert.
        Assert.Equal("s", workspace.Find("api")!.ParentId);
        Assert.Single(workspace.Views);
    }

    [Fact]
    public void Parse_OfAnEmptyDocument_IsAnEmptyWorkspace()
    {
        // Act.
        var workspace = ParseText("");

        // Assert.
        Assert.Empty(workspace.Elements);
        Assert.Empty(workspace.Views);
    }

    [Theory]
    [InlineData("minimal.dsl")]
    [InlineData("crlf-line-endings.dsl")]
    [InlineData("lf-line-endings.dsl")]
    [InlineData("no-trailing-newline.dsl")]
    public void Parse_ReadsTheSameModel_WhateverTheLineEndings(string name)
    {
        // Act.
        var workspace = ParseFixture(name);

        // Assert.
        Assert.Equal(2, workspace.Elements.Count);
        Assert.Single(workspace.Relationships);
    }
}
