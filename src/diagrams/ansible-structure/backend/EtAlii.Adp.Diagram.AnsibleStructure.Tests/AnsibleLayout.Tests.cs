using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// Where everything goes (Requirement 6): ranked so the execution story reads left to right,
/// banded so inventories do not lengthen it, and the same every time.
/// </summary>
public class AnsibleLayoutTests
{
    private static readonly AnsibleProjectReader Reader = new();

    private static AnsibleGraph Graph(string fixture) => AnsibleGraph.Derive(Reader.Read(IoPath.Combine("Fixtures", fixture)));

    private static IReadOnlyDictionary<string, AnsibleBox> Layout(string fixture) => AnsibleLayout.Compute(Graph(fixture));

    // ---- determinism -------------------------------------------------------------------------

    [Fact]
    public void TheSameGraph_YieldsTheSamePositionsTwice()
    {
        // Act.
        var first = Layout("infrastructure");
        var second = Layout("infrastructure");

        // Assert.
        // Boxes are readonly record structs, so this is a value comparison of the whole layout.
        Assert.Equal(first.OrderBy(pair => pair.Key, StringComparer.Ordinal), second.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryNode_IsPlaced()
    {
        // Act.
        var graph = Graph("infrastructure");
        var boxes = AnsibleLayout.Compute(graph);

        // Assert.
        // A node with no box would simply not be drawn, and nothing else would say so.
        Assert.Equal(graph.Nodes.Count, boxes.Count);
        Assert.All(graph.Nodes, node => Assert.True(boxes.ContainsKey(node.Id), $"{node.Id} was not placed."));
    }

    // ---- ranks -------------------------------------------------------------------------------

    [Fact]
    public void TheExecutionStory_ReadsLeftToRight()
    {
        // Act.
        var boxes = Layout("infrastructure");

        // Assert.
        // site.yml imports the other two, so it is the entry point and sits leftmost; roles are
        // right of the playbooks that use them; the included task file is right of its role.
        var entry = boxes["playbook:site.yml"].X;
        var imported = boxes["playbook:webservers.yml"].X;
        var role = boxes["role:nginx"].X;
        var taskFile = boxes["taskfile:roles/nginx/tasks/tls.yml"].X;

        Assert.True(entry < imported, "An entry playbook sits left of what it imports.");
        Assert.True(imported < role, "A playbook sits left of the roles it uses.");
        Assert.True(role < taskFile, "A role sits left of the task files it includes.");
    }

    [Fact]
    public void TwoPlaybooksAtTheSameDepth_ShareAColumn()
    {
        // Act.
        var boxes = Layout("infrastructure");

        // Assert.
        Assert.Equal(boxes["playbook:webservers.yml"].X, boxes["playbook:dbservers.yml"].X);
        Assert.NotEqual(boxes["playbook:webservers.yml"].Y, boxes["playbook:dbservers.yml"].Y);
    }

    [Fact]
    public void EveryRoleSharesOneColumn_SoTheBandOfRolesReadsAsOne()
    {
        // Act.
        var boxes = Layout("infrastructure");

        // Assert.
        var roleColumns = boxes.Where(pair => pair.Key.StartsWith("role:", StringComparison.Ordinal))
            .Select(pair => pair.Value.X)
            .Distinct()
            .ToArray();
        Assert.Single(roleColumns);
    }

    [Fact]
    public void APlay_SitsRightOfThePlaybookThatDeclaresIt()
    {
        // Act.
        // broken/playbooks/deploy.yml has three plays, so plays are drawn.
        var boxes = Layout("broken");

        // Assert.
        var playbook = boxes["playbook:playbooks/deploy.yml"].X;
        var plays = boxes.Where(pair => pair.Key.StartsWith("play:", StringComparison.Ordinal)).Select(pair => pair.Value.X);
        Assert.All(plays, x => Assert.True(x > playbook, "A play sits right of its playbook."));
    }

    // ---- the band ----------------------------------------------------------------------------

    [Fact]
    public void InventoriesAndVariableFolders_SitBeneathTheFlow_RatherThanInIt()
    {
        // Act.
        var boxes = Layout("infrastructure");

        // Assert.
        // Not ranked, so a play's edge into an inventory drops out of the execution story
        // instead of lengthening it (Requirement 6.2).
        var bandTop = boxes.Where(pair => pair.Key.StartsWith("inventory:", StringComparison.Ordinal) ||
                                          pair.Key.StartsWith("vars:", StringComparison.Ordinal))
            .Select(pair => pair.Value.Y)
            .ToArray();
        var flowBottom = boxes.Where(pair => !pair.Key.StartsWith("inventory:", StringComparison.Ordinal) &&
                                             !pair.Key.StartsWith("vars:", StringComparison.Ordinal))
            .Select(pair => pair.Value.Bottom)
            .Max();

        Assert.NotEmpty(bandTop);
        Assert.All(bandTop, y => Assert.True(y > flowBottom, "The band sits below everything in the flow."));
    }

    [Fact]
    public void TheBand_IsOneRow()
    {
        // Act.
        var boxes = Layout("infrastructure");

        // Assert.
        var band = boxes.Where(pair => pair.Key.StartsWith("inventory:", StringComparison.Ordinal) ||
                                       pair.Key.StartsWith("vars:", StringComparison.Ordinal))
            .Select(pair => pair.Value.Y)
            .Distinct()
            .ToArray();
        Assert.Single(band);
    }

    // ---- no overlap --------------------------------------------------------------------------

    [Theory]
    [InlineData("infrastructure")]
    [InlineData("broken")]
    public void NoTwoNodesOverlap(string fixture)
    {
        // Act.
        var boxes = Layout(fixture).ToArray();

        // Assert.
        for (var i = 0; i < boxes.Length; i++)
        {
            for (var j = i + 1; j < boxes.Length; j++)
            {
                Assert.False(
                    boxes[i].Value.Intersects(boxes[j].Value),
                    $"{boxes[i].Key} overlaps {boxes[j].Key}.");
            }
        }
    }

    [Fact]
    public void ALongNodeName_WidensItsWholeColumn_RatherThanOverlappingItsNeighbour()
    {
        // Arrange.
        // A column is as wide as its widest member, so a long role name pushes the next column
        // right instead of running into it.
        var metrics = AnsibleMetrics.Default;

        // Act.
        var boxes = Layout("infrastructure");

        // Assert.
        var nginx = boxes["role:nginx"];
        Assert.True(nginx.Width >= metrics.Measure("postgres"), "The column fits its widest member.");
        Assert.Equal(boxes["role:postgres"].Width, nginx.Width);
    }

    // ---- degenerate input --------------------------------------------------------------------

    [Fact]
    public void AnEmptyGraph_LaysOutToNothing_RatherThanThrowing()
    {
        // Act.
        var boxes = Layout("unconventional");

        // Assert.
        Assert.Empty(boxes);
    }

    [Fact]
    public void AnImportCycle_Terminates()
    {
        // Arrange.
        // Ansible would refuse such a project, but ADP reads folders somebody is still editing,
        // and a half-written cycle must not hang the diagram.
        var scratch = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            File.WriteAllText(IoPath.Combine(scratch, "a.yml"), "---\n- import_playbook: b.yml\n");
            File.WriteAllText(IoPath.Combine(scratch, "b.yml"), "---\n- import_playbook: a.yml\n");

            // Act.
            var boxes = AnsibleLayout.Compute(AnsibleGraph.Derive(Reader.Read(scratch)));

            // Assert.
            Assert.Equal(2, boxes.Count);
        }
        finally
        {
            TestFolder.TryDelete(scratch);
        }
    }
}
