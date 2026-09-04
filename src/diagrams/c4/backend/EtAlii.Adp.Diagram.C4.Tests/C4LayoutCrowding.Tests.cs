using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// What happens when a lot of elements have to leave one boundary at once.
/// </summary>
/// <remarks>
/// Elements that are not inside a view's boundary are pushed clear of it, each by its own
/// shortest route and knowing nothing of the others, so two that share an edge arrive at the
/// same place. Separating them afterwards is easy for two and gets harder the more there are:
/// moving one clear of its neighbour can push it onto the next.
/// <para>
/// These tests exist because the first attempt at that separation could not work. It nudged
/// one pair apart at a time, which cannot separate two boxes that are exactly coincident:
/// identical geometry means identical arithmetic, so both compute the same escape and move
/// together for as many passes as they are given. When the passes ran out it returned with
/// the overlaps still in place and nothing said.
/// </para>
/// </remarks>
public class C4LayoutCrowdingTests
{
    /// <summary>
    /// A container view whose system has two containers and <paramref name="outsiderCount"/>
    /// other systems talking to it. Every one of those is outside the boundary and has to be
    /// pushed out of it.
    /// </summary>
    private static C4Workspace Crowded(int outsiderCount)
    {
        var dsl = new System.Text.StringBuilder();
        dsl.AppendLine("workspace {");
        dsl.AppendLine("    model {");
        dsl.AppendLine("        subject = softwareSystem \"Subject\" \"The system in scope.\" {");
        dsl.AppendLine("            web = container \"Web\" \"Serves pages.\" \"React\"");
        dsl.AppendLine("            api = container \"Api\" \"Answers calls.\" \"Java\"");
        dsl.AppendLine("        }");
        dsl.AppendLine("        web -> api \"Calls\" \"HTTPS\"");

        for (var index = 0; index < outsiderCount; index++)
        {
            dsl.AppendLine($"        outside{index} = softwareSystem \"Outside {index}\" \"An external system.\"");
            dsl.AppendLine($"        api -> outside{index} \"Calls\" \"HTTPS\"");
        }

        dsl.AppendLine("    }");
        dsl.AppendLine("    views {");
        dsl.AppendLine("        container subject \"containers\" {");
        dsl.AppendLine("            include *");
        dsl.AppendLine("        }");
        dsl.AppendLine("    }");
        dsl.AppendLine("}");

        return C4Parser.Parse(C4Document.Parse(dsl.ToString()));
    }

    private static (string First, string Second)? FirstOverlap(C4Layout layout)
    {
        var boxes = layout.Boxes.ToArray();
        for (var i = 0; i < boxes.Length; i++)
        {
            for (var j = i + 1; j < boxes.Length; j++)
            {
                if (boxes[i].Value.Overlaps(boxes[j].Value))
                {
                    return (boxes[i].Key, boxes[j].Key);
                }
            }
        }

        return null;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(20)]
    [InlineData(40)]
    public void HoweverManyElementsLeaveTheBoundary_NoneEndUpDrawnOnAnother(int outsiderCount)
    {
        // Arrange.
        var workspace = Crowded(outsiderCount);

        // Act.
        var layout = C4LayoutEngine.Compute(workspace, workspace.Views[0], C4Metrics.Default);

        // Assert.
        var overlap = FirstOverlap(layout);
        Assert.True(overlap is null, $"With {outsiderCount} outsiders, '{overlap?.First}' overlaps '{overlap?.Second}'.");
    }

    [Fact]
    public void TheElementsInsideTheBoundary_StayInsideIt()
    {
        // Arrange.
        // Separating the outsiders must not be paid for by walking one back in: the whole point
        // of pushing them out is that a boundary means "these are the parts of that system".
        var workspace = Crowded(20);

        // Act.
        var layout = C4LayoutEngine.Compute(workspace, workspace.Views[0], C4Metrics.Default);

        // Assert.
        var boundary = Assert.Single(layout.Boundaries).Box;
        foreach (var id in new[] { "web", "api" })
        {
            var box = layout.Boxes[id];
            Assert.True(box.X >= boundary.X && box.Right <= boundary.Right, $"'{id}' has left its boundary horizontally.");
            Assert.True(box.Y >= boundary.Y && box.Bottom <= boundary.Bottom, $"'{id}' has left its boundary vertically.");
        }
    }

    [Fact]
    public void NoOutsiderIsLeftSittingOnTheBoundary()
    {
        // Arrange.
        var workspace = Crowded(20);

        // Act.
        var layout = C4LayoutEngine.Compute(workspace, workspace.Views[0], C4Metrics.Default);

        // Assert.
        var boundary = Assert.Single(layout.Boundaries).Box;
        foreach (var (id, box) in layout.Boxes.Where(entry => entry.Key.StartsWith("outside", StringComparison.Ordinal)))
        {
            Assert.False(box.Overlaps(boundary), $"'{id}' is drawn on top of the boundary it was pushed out of.");
        }
    }

    [Fact]
    public void TheLayoutIsStable_SoTheSameModelDrawsTheSameTwice()
    {
        // Arrange.
        // Separation that depends on dictionary order would redraw the diagram differently on
        // each open, which reads as the canvas twitching for no reason.
        var workspace = Crowded(20);

        // Act.
        var first = C4LayoutEngine.Compute(workspace, workspace.Views[0], C4Metrics.Default);
        var second = C4LayoutEngine.Compute(workspace, workspace.Views[0], C4Metrics.Default);

        // Assert.
        Assert.Equal(first.Boxes, second.Boxes);
    }

    /// <summary>
    /// The model this was found on, kept as a fixture because nothing simpler reproduced it.
    /// </summary>
    /// <remarks>
    /// A copy of the industrial example with its component view left as <c>include *</c>, which
    /// puts every container in the plant outside a boundary drawn around one of them. Several
    /// synthetic models were tried first - a fan of external systems, a chain of them, a
    /// component view with a dozen siblings - and none produced the coincidence that breaks
    /// pairwise nudging. Real rank structure did.
    /// </remarks>
    private static C4Workspace TheModelThatBrokeIt() =>
        C4Parser.Parse(C4Document.Parse(File.ReadAllText(IoPath.Combine("Fixtures", "crowded-component-view.dsl"))));

    [Fact]
    public void OnTheModelThisWasFoundOn_NothingIsDrawnOnAnythingElse()
    {
        // Arrange.
        var workspace = TheModelThatBrokeIt();

        // The floor: this test is named for the one model that produced the bug, so a fixture
        // that stopped parsing - or stopped being found - would take the regression's only
        // witness with it and report success.
        Assert.True(
            workspace.Views.Count >= 1,
            $"Only {workspace.Views.Count} views were parsed from crowded-component-view.dsl; this guard has stopped finding the model it was written for.");

        // Act and assert, view by view.
        // Before the sweep this reported five overlapping pairs on one view, three of them
        // mutually coincident - the lockstep case, where identical geometry means both boxes
        // compute the same escape and travel together however many passes they are given.
        foreach (var view in workspace.Views)
        {
            var layout = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default);
            var overlap = FirstOverlap(layout);

            Assert.True(overlap is null, $"'{view.Key}': '{overlap?.First}' overlaps '{overlap?.Second}'.");
        }
    }

    [Fact]
    public void OnThatSameModel_NoTwoElementsShareAPosition()
    {
        // Arrange.
        // The sharper statement: it is not enough that no overlap is reported. Two boxes drawn
        // at the same coordinates look like one box, and a reader never learns the other is
        // there.
        var workspace = TheModelThatBrokeIt();
        var view = workspace.FindView("OrderServiceComponents")!;

        // Act.
        var positions = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default)
            .Boxes
            .Select(entry => (entry.Value.X, entry.Value.Y))
            .ToArray();

        // Assert.
        Assert.Equal(positions.Length, positions.Distinct().Count());
    }

    [Fact]
    public void OnThatSameModel_TheLayoutIsStable()
    {
        // Arrange.
        var workspace = TheModelThatBrokeIt();
        var view = workspace.FindView("OrderServiceComponents")!;

        // Act.
        // Sweeping orders by position and then by id rather than by whatever order a dictionary
        // enumerated, so the same model draws the same way twice.
        var first = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default);
        var second = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default);

        // Assert.
        Assert.Equal(first.Boxes, second.Boxes);
    }

}
