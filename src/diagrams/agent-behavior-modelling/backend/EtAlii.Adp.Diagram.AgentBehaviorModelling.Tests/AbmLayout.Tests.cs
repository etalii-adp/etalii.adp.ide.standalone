using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

public class AbmLayoutTests
{
    [Fact]
    public void ChildrenRunLeftToRight_InDocumentOrder_UnderACentredParent()
    {
        // Arrange.
        var model = AbmParser.Parse(LineDocument.Parse("## Behavior\n- **Do in order:** A\n  - **Do:** B\n  - **Do in order:** C\n    - **Do:** D\n    - **Do:** E\n  - **Do:** F\n"));

        // Act.
        var positions = AbmLayout.Compute(model);

        // Assert: order is left to right, every child is one row below its parent.
        Assert.True(positions["1.1"].X < positions["1.2"].X && positions["1.2"].X < positions["1.3"].X);
        Assert.True(positions["1.2.1"].X < positions["1.2.2"].X);
        Assert.Equal(positions["1"].Y + AbmLayout.NodeHeight + AbmLayout.VerticalGap, positions["1.1"].Y);

        // Assert: a parent sits centred over its first and last child.
        Assert.Equal((positions["1.1"].X + positions["1.3"].X) / 2, positions["1"].X, 6);
        Assert.Equal((positions["1.2.1"].X + positions["1.2.2"].X) / 2, positions["1.2"].X, 6);
    }

    [Fact]
    public void ARowTakesItsStoredHeight_AndTheRowsBeneathItFollow()
    {
        // Arrange: the second child's row is stored 300 lower than computed; its x is ignored.
        var model = AbmParser.Parse(LineDocument.Parse("## Behavior\n- **Do in order:** A\n  - **Do:** B\n  - **Do in order:** C\n    - **Do:** D\n"));
        var computed = AbmLayout.Compute(model);
        var stored = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal) { ["1.2"] = new(-5000, computed["1.2"].Y + 300) };

        // Act.
        var positions = AbmLayout.Arrange(model, stored);

        // Assert.
        Assert.Equal(computed["1"], positions["1"]);
        Assert.Equal(new RegistrationPosition(computed["1.1"].X, computed["1.1"].Y + 300), positions["1.1"]);
        Assert.Equal(new RegistrationPosition(computed["1.2"].X, computed["1.2"].Y + 300), positions["1.2"]);
        Assert.Equal(new RegistrationPosition(computed["1.2.1"].X, computed["1.2.1"].Y + 300), positions["1.2.1"]);
    }

    [Fact]
    public void ARowIsNeverDrawnAboveItsParent()
    {
        // Arrange: a hand-edited height far above the parent.
        var model = AbmParser.Parse(LineDocument.Parse("## Behavior\n- **Do in order:** A\n  - **Do:** B\n"));
        var stored = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal) { ["1.1"] = new(0, -1000) };

        // Act.
        var positions = AbmLayout.Arrange(model, stored);

        // Assert.
        Assert.Equal(positions["1"].Y + AbmLayout.NodeHeight + AbmLayout.MinimumGap, positions["1.1"].Y);
    }

    [Fact]
    public void NoTwoNodesOverlap_InAnyExample()
    {
        foreach (var name in AbmExamples.Names)
        {
            // Arrange.
            var model = AbmParser.Parse(LineDocument.Parse(File.ReadAllText(AbmExamples.BodyOf(name))));

            // Act.
            var positions = AbmLayout.Compute(model).Values.ToList();

            // Assert.
            for (var first = 0; first < positions.Count; first++)
            {
                for (var second = first + 1; second < positions.Count; second++)
                {
                    Assert.False(Overlap(positions[first], positions[second]), $"{name}: two nodes overlap at {positions[first]} and {positions[second]}");
                }
            }
        }
    }

    private static bool Overlap(RegistrationPosition a, RegistrationPosition b) =>
        Math.Abs(a.X - b.X) < AbmLayout.NodeWidth && Math.Abs(a.Y - b.Y) < AbmLayout.NodeHeight;
}
