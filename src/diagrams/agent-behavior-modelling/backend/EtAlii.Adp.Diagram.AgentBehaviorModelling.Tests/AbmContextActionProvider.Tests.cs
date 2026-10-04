using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

public class AbmContextActionProviderTests
{
    private static readonly Dictionary<string, RegistrationPosition> NoneStored = new(StringComparer.Ordinal);

    private static readonly AbmModel Model = AbmParser.Parse(LineDocument.Parse(
        "## Behavior\n- **Do in order:** A\n  - **Do:** B\n  - **Do in order:** C\n    - **Do:** D\n"));

    private static (double X, double Y) Centre(string id)
    {
        var topLeft = AbmLayout.Compute(Model)[id];
        return (topLeft.X + (AbmLayout.NodeWidth / 2), topLeft.Y + (AbmLayout.NodeHeight / 2));
    }

    [Fact]
    public void ADropUnderAParent_GoesBeneathIt_AmongItsChildrenByPosition()
    {
        // Arrange: just below C, right of D.
        (double x, double y) = Centre("1.2.1");

        // Act.
        (AbmNode? parent, int index, string refusal) = AbmContextActionProvider.PlaceDrop(Model, NoneStored, x + 150, y + 20);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal("1.2", parent!.Id);
        Assert.Equal(1, index);
    }

    [Fact]
    public void ADropBetweenTheRootsChildren_LandsBetweenThem()
    {
        // Arrange: on the children's row, between B and C.
        (double bx, double by) = Centre("1.1");
        (double cx, _) = Centre("1.2");

        // Act.
        (AbmNode? parent, int index, _) = AbmContextActionProvider.PlaceDrop(Model, NoneStored, (bx + cx) / 2, by);

        // Assert.
        Assert.Equal("1", parent!.Id);
        Assert.Equal(1, index);
    }

    [Fact]
    public void ADropAboveEverything_IsRefused_AndOnAnEmptyModelBecomesTheRoot()
    {
        // Act.
        var above = AbmContextActionProvider.PlaceDrop(Model, NoneStored, 0, -500);
        var empty = AbmContextActionProvider.PlaceDrop(AbmModel.Empty, NoneStored, 0, -500);

        // Assert.
        Assert.NotEqual("", above.Refusal);
        Assert.Null(empty.Parent);
        Assert.Equal("", empty.Refusal);
    }
}
