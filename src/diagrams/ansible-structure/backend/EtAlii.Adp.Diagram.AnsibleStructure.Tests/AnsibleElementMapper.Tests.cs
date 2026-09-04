using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// Model and layout onto the core element and delta vocabulary (Requirement 7.1) - and, just as
/// much, the two delta kinds this type must never emit.
/// </summary>
public class AnsibleElementMapperTests
{
    private static readonly AnsibleProjectReader Reader = new();
    private static readonly AnsibleElementMapper Mapper = new();

    private static (AnsibleProject Project, AnsibleGraph Graph) Read(string fixture)
    {
        var project = Reader.Read(IoPath.Combine("Fixtures", fixture));
        return (project, AnsibleGraph.Derive(project));
    }

    private static IReadOnlyList<DiagramElement> Everything(string fixture)
    {
        var (project, graph) = Read(fixture);
        return Mapper.Visible(project, graph, DiagramViewport.Unbounded);
    }

    private static Wire.AnsibleElementPayload PayloadOf(DiagramElement element) =>
        Wire.AnsibleElementPayload.Parser.ParseFrom(element.Payload.ToArray());

    // ---- what crosses the wire ---------------------------------------------------------------

    [Fact]
    public void EveryNodeAndEdge_BecomesAnElement()
    {
        // Act.
        var (project, graph) = Read("infrastructure");
        var elements = Mapper.Visible(project, graph, DiagramViewport.Unbounded);

        // Assert.
        Assert.Equal(graph.Nodes.Count + graph.Edges.Count, elements.Count);
    }

    [Fact]
    public void EachNodeKind_CarriesItsOwnMimeTypedKind()
    {
        // Act.
        var types = Everything("infrastructure").Select(element => element.Type).Distinct().ToArray();

        // Assert.
        Assert.Contains(AnsibleElementMapper.PlaybookType, types);
        Assert.Contains(AnsibleElementMapper.RoleType, types);
        Assert.Contains(AnsibleElementMapper.TaskFileType, types);
        Assert.Contains(AnsibleElementMapper.InventoryType, types);
        Assert.Contains(AnsibleElementMapper.VariableFolderType, types);
        Assert.Contains(AnsibleElementMapper.EdgeType, types);
        Assert.All(types, type => Assert.StartsWith("ansible/structure+", type, StringComparison.Ordinal));
    }

    [Fact]
    public void ANode_CarriesItsProjectRelativePath_ForTheReveal()
    {
        // Act.
        var nginx = Assert.Single(Everything("infrastructure"), element => element.Id == "role:nginx");

        // Assert.
        // What the canvas hands to revealPath when the node is activated (Requirement 8.1).
        Assert.Equal(["roles", "nginx"], PayloadOf(nginx).ProjectRelativePath);
    }

    [Fact]
    public void ANode_CarriesTheBoxTheLayoutMeasured()
    {
        // Act.
        var nginx = Assert.Single(Everything("infrastructure"), element => element.Id == "role:nginx");

        // Assert.
        // The core Element carries a position but no size, and a client-side guess at the size
        // is what made wide nodes overlap in the mindmap module.
        var payload = PayloadOf(nginx);
        Assert.True(payload.Width > 0);
        Assert.True(payload.Height > 0);
    }

    [Fact]
    public void ARole_CarriesItsContents_AndWhetherItIsHollow()
    {
        // Act.
        var elements = Everything("broken");

        // Assert.
        var hollow = PayloadOf(Assert.Single(elements, element => element.Id == "role:hollow"));
        Assert.True(hollow.Hollow);
        Assert.Equal(0, hollow.Contents.TaskFiles);

        var web = PayloadOf(Assert.Single(elements, element => element.Id == "role:web"));
        Assert.False(web.Hollow);
        Assert.Equal(1, web.Contents.TaskFiles);
        Assert.True(web.Contents.HasMeta);
    }

    [Fact]
    public void AnInventory_CarriesItsGroups()
    {
        // Act.
        var production = Assert.Single(Everything("infrastructure"), e => e.Id == "inventory:inventories/production");

        // Assert.
        var groups = PayloadOf(production).Groups;
        Assert.Equal(["web", "db"], groups.Select(group => group.Name));
        Assert.Equal(2, groups.Single(group => group.Name == "web").HostCount);
    }

    [Fact]
    public void ASinglePlayPlaybook_CarriesItsPlaysHostPattern()
    {
        // Act.
        // No play box is drawn for a single-play file, so the pattern has to ride the playbook
        // or the reader would never see it.
        var web = Assert.Single(Everything("infrastructure"), element => element.Id == "playbook:webservers.yml");

        // Assert.
        Assert.Equal("web", PayloadOf(web).Hosts);
    }

    [Fact]
    public void AnEdge_CarriesTheFileAndDirectiveThatDeclaredIt()
    {
        // Act.
        var elements = Everything("infrastructure");
        var include = Assert.Single(elements, element =>
            element.Type == AnsibleElementMapper.EdgeType &&
            PayloadOf(element).Edge.Kind == Wire.AnsibleEdgeKind.IncludesTasks);

        // Assert.
        // The answer to "why is this here" (Requirement 10.6).
        var edge = PayloadOf(include).Edge;
        Assert.Equal("include_tasks", edge.Directive);
        Assert.Equal("tls.yml", edge.TargetAsWritten);
        Assert.Equal(["roles", "nginx", "tasks", "main.yml"], edge.DeclaredIn);
        Assert.True(edge.DeclaredAtLine > 0);
        Assert.True(edge.Dynamic);
        Assert.Equal("nginx_tls_enabled | default(false)", edge.Condition);
    }

    [Fact]
    public void AMissingAndAnUnresolvableEdge_AreDistinguishableOnTheWire()
    {
        // Act.
        var elements = Everything("broken").Where(e => e.Type == AnsibleElementMapper.EdgeType).ToArray();

        // Assert.
        // Missing is a mistake; unknown is not. A client that could not tell them apart would
        // have to draw them alike, and the distinction is the point of the three-state model.
        var missing = Assert.Single(elements, e => PayloadOf(e).Edge.TargetAsWritten == "absent-role");
        Assert.True(PayloadOf(missing).Edge.Missing);
        Assert.False(PayloadOf(missing).Unresolvable);

        var expression = Assert.Single(elements, e => PayloadOf(e).Edge.TargetAsWritten == "{{ role_name }}");
        Assert.True(PayloadOf(expression).Unresolvable);
        Assert.False(PayloadOf(expression).Edge.Missing);
    }

    [Fact]
    public void ThePlayColour_CrossesAsAnIndex_NeverAsAColour()
    {
        // Act.
        var plays = Everything("broken").Where(e => e.Type == AnsibleElementMapper.PlayType).ToArray();

        // Assert.
        Assert.NotEmpty(plays);
        Assert.All(plays, play => Assert.True(PayloadOf(play).PlayIndex >= 0));
        // Nothing in the payload names a colour: the palette is the stylesheet's (tech.md).
        var fields = typeof(Wire.AnsibleElementPayload).GetProperties().Select(property => property.Name);
        Assert.DoesNotContain(fields, name => name.Contains("Colour", StringComparison.OrdinalIgnoreCase) ||
                                              name.Contains("Color", StringComparison.OrdinalIgnoreCase));
    }

    // ---- ids ------------------------------------------------------------------------------------

    [Fact]
    public void AnId_SurvivesAnEditElsewhereInTheTree()
    {
        // Arrange.
        var scratch = CopyFixture("infrastructure");
        try
        {
            var before = Ids(scratch);

            // Act.
            // A change that has nothing to do with the nginx role.
            File.AppendAllText(IoPath.Combine(scratch, "dbservers.yml"), "\n# a comment\n");
            var after = Ids(scratch);

            // Assert.
            Assert.Contains("role:nginx", before);
            Assert.Contains("role:nginx", after);
            Assert.Contains("taskfile:roles/nginx/tasks/tls.yml", after);
        }
        finally
        {
            TestFolder.TryDelete(scratch);
        }
    }

    // ---- the viewport ----------------------------------------------------------------------------

    [Fact]
    public void AViewport_CullsWhatItDoesNotTouch()
    {
        // Act.
        var (project, graph) = Read("infrastructure");
        var everything = Mapper.Visible(project, graph, DiagramViewport.Unbounded);
        var narrow = Mapper.Visible(project, graph, new DiagramViewport(-10, -10, 10, 10));

        // Assert.
        Assert.True(narrow.Count < everything.Count, "A narrow viewport should deliver less than everything.");
        Assert.NotEmpty(narrow);
    }

    [Fact]
    public void ACulledEdgeKeepsBothItsEnds()
    {
        // Act.
        var (project, graph) = Read("infrastructure");
        var delivered = Mapper.Visible(project, graph, new DiagramViewport(-10, -10, 10, 10));

        // Assert.
        // The canvas draws a connector from the boxes at its two ends, so an element whose
        // partner was culled would lose the line running towards it.
        var ids = delivered.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var edge in delivered.Where(element => element.Type == AnsibleElementMapper.EdgeType))
        {
            var payload = PayloadOf(edge).Edge;
            Assert.Contains(payload.SourceId, ids);
            if (payload.TargetId.Length > 0)
            {
                Assert.Contains(payload.TargetId, ids);
            }
        }
    }

    // ---- what is never emitted ---------------------------------------------------------------------

    [Fact]
    public void NoGroupOrUngroupDeltaIsEverProduced()
    {
        // Act.
        var (project, graph) = Read("infrastructure");
        var elements = Mapper.Visible(project, graph, DiagramViewport.Unbounded);
        var deltas = Mapper.Diff([], elements);

        // Assert.
        // Nothing on this diagram folds, so the two delta kinds that express folding have no
        // meaning here. Their absence is the design, not an omission.
        Assert.All(deltas, delta => Assert.IsNotType<DiagramGroupDelta>(delta));
        Assert.All(deltas, delta => Assert.IsNotType<DiagramUngroupDelta>(delta));
    }

    [Fact]
    public void AFirstPush_IsOneAddAndNoRemove()
    {
        // Act.
        var deltas = Mapper.Diff([], Everything("infrastructure"));

        // Assert.
        Assert.IsType<DiagramAddDelta>(Assert.Single(deltas));
    }

    [Fact]
    public void SomethingLeavingTheFolder_IsRemovedBeforeTheRestIsResent()
    {
        // Arrange.
        var before = Everything("infrastructure");
        var scratch = CopyFixture("infrastructure");
        try
        {
            File.Delete(IoPath.Combine(scratch, "dbservers.yml"));
            var project = Reader.Read(scratch);
            var after = Mapper.Visible(project, AnsibleGraph.Derive(project), DiagramViewport.Unbounded);

            // Act.
            var deltas = Mapper.Diff(before, after);

            // Assert.
            var removed = Assert.IsType<DiagramRemoveDelta>(deltas[0]);
            Assert.Contains("playbook:dbservers.yml", removed.ElementIds);
            Assert.IsType<DiagramAddDelta>(deltas[1]);
        }
        finally
        {
            TestFolder.TryDelete(scratch);
        }
    }

    [Fact]
    public void AnUnchangedProject_ProducesNoRemoval()
    {
        // Act.
        var elements = Everything("infrastructure");
        var deltas = Mapper.Diff(elements, elements);

        // Assert.
        Assert.All(deltas, delta => Assert.IsNotType<DiagramRemoveDelta>(delta));
    }

    // ---- plumbing -----------------------------------------------------------------------------------

    private static string[] Ids(string folder)
    {
        var project = Reader.Read(folder);
        return [.. Mapper.Visible(project, AnsibleGraph.Derive(project), DiagramViewport.Unbounded)
            .Select(element => element.Id)];
    }

    private static string CopyFixture(string fixture)
    {
        var source = IoPath.Combine("Fixtures", fixture);
        var destination = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        foreach (var folder in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(IoPath.Combine(destination, IoPath.GetRelativePath(source, folder)));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, IoPath.Combine(destination, IoPath.GetRelativePath(source, file)));
        }
        return destination;
    }

    // ---- authored positions (Requirements 1.4, 3.1, 3.4) --------------------------------------

    [Fact]
    public void AnAuthoredPosition_OverridesTheComputedOne()
    {
        // Arrange: one node moved somewhere the layout engine would never place it.
        var (project, graph) = Read("infrastructure");
        var moved = graph.Nodes[0].Id;
        var stored = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal)
        {
            [moved] = new RegistrationPosition(4321, 1234),
        };

        // Act.
        var elements = Mapper.Visible(project, graph, DiagramViewport.Unbounded, stored);

        // Assert.
        var element = elements.Single(candidate => candidate.Id == moved);
        Assert.Equal(4321, element.X);
        Assert.Equal(1234, element.Y);
    }

    [Fact]
    public void UnauthoredElements_KeepTheirComputedPositions()
    {
        // Arrange.
        var (project, graph) = Read("infrastructure");
        var moved = graph.Nodes[0].Id;
        var computed = Mapper.Visible(project, graph, DiagramViewport.Unbounded);
        var stored = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal)
        {
            [moved] = new RegistrationPosition(4321, 1234),
        };

        // Act.
        var arranged = Mapper.Visible(project, graph, DiagramViewport.Unbounded, stored);

        // Assert: everything sits where it sat before, except the moved node itself and the
        // edges anchored on it - those follow their endpoint, which is the point of moving it.
        // Compared pairwise rather than by id lookup: two identical directives yield two edges
        // sharing one id, so an id is not a key in this sequence.
        var follows = graph.Edges
            .Where(candidate => candidate.SourceId == moved)
            .Select(candidate => candidate.Id)
            .Append(moved)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(computed.Count, arranged.Count);
        foreach (var (before, after) in computed.Zip(arranged).Where(pair => !follows.Contains(pair.First.Id)))
        {
            Assert.Equal(before.Id, after.Id);
            Assert.Equal(before.X, after.X);
            Assert.Equal(before.Y, after.Y);
        }
    }

    [Fact]
    public void AStoredIdTheGraphNoLongerProduces_ChangesNothing()
    {
        // Arrange: the stale-key case - a play removed, a file renamed (Requirement 3.1).
        var (project, graph) = Read("infrastructure");
        var computed = Mapper.Visible(project, graph, DiagramViewport.Unbounded);
        var stored = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal)
        {
            ["playbook:deleted-yesterday.yml"] = new RegistrationPosition(4321, 1234),
        };

        // Act.
        var arranged = Mapper.Visible(project, graph, DiagramViewport.Unbounded, stored);

        // Assert: no phantom element, and nothing displaced. Pairwise, as above.
        Assert.Equal(computed.Count, arranged.Count);
        Assert.DoesNotContain(arranged, element => element.Id == "playbook:deleted-yesterday.yml");
        foreach (var (before, after) in computed.Zip(arranged))
        {
            Assert.Equal(before.Id, after.Id);
            Assert.Equal(before.X, after.X);
            Assert.Equal(before.Y, after.Y);
        }
    }

    [Fact]
    public void AnEdge_FollowsAMovedEndpoint()
    {
        // Arrange: an edge anchors on its source, so moving that source moves the edge with it.
        var (project, graph) = Read("infrastructure");
        var edge = graph.Edges.First(candidate => candidate.TargetId.Length > 0);
        var stored = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal)
        {
            [edge.SourceId] = new RegistrationPosition(4321, 1234),
        };

        // Act.
        var elements = Mapper.Visible(project, graph, DiagramViewport.Unbounded, stored);

        // Assert.
        var drawn = elements.Single(candidate => candidate.Id == edge.Id);
        Assert.Equal(4321, drawn.X);
        Assert.Equal(1234, drawn.Y);
    }
}
