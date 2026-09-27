using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// One test per edge kind of Requirement 5, plus the three-state resolution that keeps a
/// parameterised role from being reported as a missing one.
/// </summary>
public class AnsibleGraphTests
{
    private static readonly AnsibleProjectReader Reader = new();

    private static AnsibleGraph Graph(string fixture) => AnsibleGraph.Derive(Reader.Read(IoPath.Combine("Fixtures", fixture)));

    private static AnsibleEdge[] EdgesOf(AnsibleGraph graph, AnsibleEdgeKind kind) =>
        [.. graph.Edges.Where(edge => edge.Kind == kind)];

    // ---- the five edge kinds ---------------------------------------------------------------

    [Fact]
    public void APlaysRolesList_RunsPlaybookToRole()
    {
        // Act.
        var uses = EdgesOf(Graph("infrastructure"), AnsibleEdgeKind.UsesRole);

        // Assert.
        // webservers.yml and dbservers.yml each have one play, so the edges run from the
        // playbook: a single-play file drawn as two boxes would say the same thing twice.
        Assert.Contains(uses, e => e.SourceId == "playbook:webservers.yml" && e.TargetId == "role:common");
        Assert.Contains(uses, e => e.SourceId == "playbook:webservers.yml" && e.TargetId == "role:nginx");
        Assert.Contains(uses, e => e.SourceId == "playbook:dbservers.yml" && e.TargetId == "role:postgres");
        Assert.All(uses, e => Assert.Equal(AnsibleTargetResolution.Resolved, e.Resolution));
    }

    [Fact]
    public void AnImportPlaybook_RunsPlaybookToPlaybook()
    {
        // Act.
        var imports = EdgesOf(Graph("infrastructure"), AnsibleEdgeKind.ImportsPlaybook);

        // Assert.
        Assert.Equal(2, imports.Length);
        Assert.All(imports, e => Assert.Equal("playbook:site.yml", e.SourceId));
        Assert.Equal(["playbook:webservers.yml", "playbook:dbservers.yml"], imports.Select(e => e.TargetId));
    }

    [Fact]
    public void AnIncludeTasks_RunsWithinTheRole_AndDrawsTheTaskFile()
    {
        // Act.
        var graph = Graph("infrastructure");

        // Assert.
        var include = Assert.Single(EdgesOf(graph, AnsibleEdgeKind.IncludesTasks));
        Assert.Equal("role:nginx", include.SourceId);
        Assert.Equal("taskfile:roles/nginx/tasks/tls.yml", include.TargetId);
        // Task files are drawn only when something includes them (Requirement 4.1): main.yml
        // is in the same folder and is not a node.
        Assert.Single(graph.Nodes, node => node.Kind == AnsibleNodeKind.TaskFile);
    }

    [Fact]
    public void AMetaDependency_RunsRoleToRole()
    {
        // Act.
        var dependencies = EdgesOf(Graph("infrastructure"), AnsibleEdgeKind.DependsOn);

        // Assert.
        Assert.Equal(2, dependencies.Length);
        Assert.All(dependencies, e => Assert.Equal("role:common", e.TargetId));
        Assert.Equal(["role:nginx", "role:postgres"], dependencies.Select(e => e.SourceId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AHostsPattern_RunsToEveryInventoryThatDefinesIt()
    {
        // Act.
        var targets = EdgesOf(Graph("infrastructure"), AnsibleEdgeKind.Targets);

        // Assert.
        // Both environments define web and db, so both plays reach both inventories.
        Assert.Equal(4, targets.Length);
        Assert.Contains(targets, e => e.SourceId == "playbook:webservers.yml" && e.TargetId == "inventory:inventories/production");
        Assert.Contains(targets, e => e.SourceId == "playbook:webservers.yml" && e.TargetId == "inventory:inventories/staging");
        // The edge is labelled with the pattern as written (Requirement 5.6).
        Assert.All(
            targets.Where(e => e.SourceId == "playbook:webservers.yml"),
            e => Assert.Equal("web", e.Directive.Target));
        Assert.All(
            targets.Where(e => e.SourceId == "playbook:dbservers.yml"),
            e => Assert.Equal("db", e.Directive.Target));
    }

    /// <summary>
    /// One pattern reaching two inventories is two edges, so it must be two ids: the canvas
    /// keys an element on its id, and two edges sharing one draw as one - the second overwrites
    /// the first - while the shared diff refuses a rendering that repeats an id at all.
    /// </summary>
    [Fact]
    public void AHostsPatternReachingTwoInventories_IsTwoEdgesWithTwoIds()
    {
        // Act.
        var targets = EdgesOf(Graph("infrastructure"), AnsibleEdgeKind.Targets);

        // Assert.
        Assert.Equal(4, targets.Length);
        Assert.Equal(targets.Length, targets.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("infrastructure")]
    [InlineData("broken")]
    [InlineData("ini-inventory")]
    [InlineData("unconventional")]
    public void EveryNodeAndEdge_HasAnIdNothingElseInTheGraphHas(string fixture)
    {
        // Act.
        var graph = Graph(fixture);
        var ids = graph.Nodes.Select(node => node.Id).Concat(graph.Edges.Select(edge => edge.Id)).ToArray();

        // Assert.
        Assert.Empty(ids.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key));
    }

    // ---- static versus dynamic, and what the file wrote --------------------------------------

    [Fact]
    public void StaticAndDynamicMechanisms_AreDistinguishable()
    {
        // Act.
        var graph = Graph("infrastructure");

        // Assert.
        // include_tasks is resolved during the run; roles: and import_playbook before it.
        Assert.True(Assert.Single(EdgesOf(graph, AnsibleEdgeKind.IncludesTasks)).IsDynamic);
        Assert.All(EdgesOf(graph, AnsibleEdgeKind.ImportsPlaybook), e => Assert.False(e.IsDynamic));
        Assert.All(EdgesOf(graph, AnsibleEdgeKind.UsesRole), e => Assert.False(e.IsDynamic));
    }

    [Fact]
    public void AnEdge_CarriesTheFileAndLineThatDeclaredIt()
    {
        // Act.
        var include = Assert.Single(EdgesOf(Graph("infrastructure"), AnsibleEdgeKind.IncludesTasks));

        // Assert.
        // The answer to "why is this here", which the property grid shows (Requirement 10.6).
        Assert.Equal("roles/nginx/tasks/main.yml", include.Directive.DeclaredIn);
        Assert.True(include.Directive.Line > 0);
        Assert.Equal("tls.yml", include.Directive.Target);
    }

    [Fact]
    public void AWhenCondition_SurvivesAsWritten_AndIsNeverEvaluated()
    {
        // Act.
        var graph = Graph("infrastructure");

        // Assert.
        var postgres = Assert.Single(EdgesOf(graph, AnsibleEdgeKind.UsesRole), e => e.TargetId == "role:postgres");
        Assert.Equal("postgres_enabled | default(true)", postgres.Directive.Condition);
    }

    // ---- the three resolution states ---------------------------------------------------------

    [Fact]
    public void ARoleWithNoFolder_IsMissing()
    {
        // Act.
        var uses = EdgesOf(Graph("broken"), AnsibleEdgeKind.UsesRole);

        // Assert.
        var absent = Assert.Single(uses, e => e.Directive.Target == "absent-role");
        Assert.Equal(AnsibleTargetResolution.Missing, absent.Resolution);
        Assert.Equal("", absent.TargetId);
    }

    [Fact]
    public void AnExpressionTarget_IsUnresolvable_NotMissing()
    {
        // Act.
        var uses = EdgesOf(Graph("broken"), AnsibleEdgeKind.UsesRole);

        // Assert.
        // The distinction the whole three-state resolution exists for: a rule that reported
        // this as missing would fire on every parameterised role in every real repository.
        var expression = Assert.Single(uses, e => e.Directive.IsExpression);
        Assert.Equal(AnsibleTargetResolution.Unresolvable, expression.Resolution);
        Assert.Equal("{{ role_name }}", expression.Directive.Target);
    }

    [Fact]
    public void ADanglingImportAndInclude_AreBothMissing()
    {
        // Act.
        var graph = Graph("broken");

        // Assert.
        var import = Assert.Single(EdgesOf(graph, AnsibleEdgeKind.ImportsPlaybook), e => e.Directive.Target == "does-not-exist.yml");
        Assert.Equal(AnsibleTargetResolution.Missing, import.Resolution);

        var include = Assert.Single(EdgesOf(graph, AnsibleEdgeKind.IncludesTasks));
        Assert.Equal(AnsibleTargetResolution.Missing, include.Resolution);
        Assert.Equal("tls.yml", include.Directive.Target);
    }

    [Fact]
    public void ADependencyOnAMissingRole_IsMissing()
    {
        // Act.
        var dependency = Assert.Single(EdgesOf(Graph("broken"), AnsibleEdgeKind.DependsOn));

        // Assert.
        Assert.Equal("base", dependency.Directive.Target);
        Assert.Equal(AnsibleTargetResolution.Missing, dependency.Resolution);
    }

    [Fact]
    public void AHostsPatternNoInventoryDefines_YieldsNoEdgeAtAll()
    {
        // Act.
        var targets = EdgesOf(Graph("broken"), AnsibleEdgeKind.Targets);

        // Assert.
        // The `cache` play reaches nothing; a pattern nothing defines is a problem the rule set
        // reports, never an edge drawn to a guess (Requirement 5.6).
        Assert.All(targets, e => Assert.NotEqual("cache", e.Directive.Target));
    }

    // ---- multi-play playbooks, and what is deliberately not drawn ----------------------------

    [Fact]
    public void APlaybookWithSeveralPlays_DrawsEachPlay_AndEdgesRunFromThePlay()
    {
        // Act.
        var graph = Graph("broken");

        // Assert.
        // broken/playbooks/deploy.yml has three plays, so each is its own box.
        var plays = graph.Nodes.Where(node => node.Kind == AnsibleNodeKind.Play).ToArray();
        Assert.Equal(3, plays.Length);
        Assert.All(plays, play => Assert.StartsWith("play:playbooks/deploy.yml#", play.Id, StringComparison.Ordinal));
        Assert.Contains(EdgesOf(graph, AnsibleEdgeKind.UsesRole), e => e.SourceId.StartsWith("play:", StringComparison.Ordinal));
    }

    [Fact]
    public void EachPlay_GetsItsOwnColourIndex()
    {
        // Act.
        var plays = Graph("broken").Nodes.Where(node => node.Kind == AnsibleNodeKind.Play).ToArray();

        // Assert.
        // An index, never a colour: the client's stylesheet owns the palette (tech.md).
        Assert.Equal(plays.Length, plays.Select(play => play.PlayIndex).Distinct().Count());
        Assert.All(plays, play => Assert.True(play.PlayIndex >= 0));
    }

    [Fact]
    public void VariableFolders_AreNodes_ButNothingDrawsAnEdgeToAVariable()
    {
        // Act.
        var graph = Graph("infrastructure");

        // Assert.
        // The honest half of ansible-viz's idea: the folders exist, so they are drawn; which
        // variable a play reads is an inference, and Requirement 5.8 refuses to draw inferences.
        var folders = graph.Nodes.Where(node => node.Kind == AnsibleNodeKind.VariableFolder).ToArray();
        Assert.Equal(3, folders.Length);
        Assert.All(graph.Edges, edge => Assert.DoesNotContain("vars:", edge.TargetId, StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnrecognisedLayout_HasNoNodesAndNoEdges()
    {
        // Act.
        var graph = Graph("unconventional");

        // Assert.
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
    }

    // ---- host pattern matching ----------------------------------------------------------------

    [Theory]
    [InlineData("web", true)]
    [InlineData("db", true)]
    [InlineData("cache", false)]
    [InlineData("all", true)]
    [InlineData("*", true)]
    [InlineData("web:db", true)]
    [InlineData("cache:web", true)]
    [InlineData("we*", true)]
    [InlineData("xy*", false)]
    [InlineData("!web", false)]
    [InlineData("&web", false)]
    [InlineData("", false)]
    public void HostPatterns_MatchTheSubsetTheModuleClaims(string pattern, bool expected)
    {
        // Arrange.
        var inventory = new AnsibleInventory("production", "inventories/production", [new("web", 2), new("db", 1)], []);

        // Act and assert.
        // Exclusions and intersections deliberately draw nothing: an exclusion names what a
        // play will NOT run on, and an edge for it would say the opposite of what the file says.
        Assert.Equal(expected, AnsibleGraph.Matches(pattern, inventory));
    }

    [Fact]
    public void TheSameProject_DerivesTheSameGraphTwice()
    {
        // Act.
        var first = Graph("infrastructure");
        var second = Graph("infrastructure");

        // Assert.
        Assert.Equal(first.Nodes.Select(n => n.Id), second.Nodes.Select(n => n.Id));
        Assert.Equal(first.Edges.Select(e => e.Id), second.Edges.Select(e => e.Id));
    }
}
