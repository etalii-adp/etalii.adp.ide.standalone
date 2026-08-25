using EtAlii.Adp.Diagram;
using Xunit;

namespace EtAlii.Adp.C4.Tests;

/// <summary>
/// The palette a view offers: exactly what it permits, and nothing it would then refuse
/// (c4-diagrams Requirement 12.2).
/// </summary>
public class C4ToolboxProviderTests
{
    private static IReadOnlyList<ToolboxItemDefinition> Items(C4ViewKind kind) =>
        new C4ToolboxProvider(new DiagramOrigin("c4", "test"), kind).Items;

    [Fact]
    public void AContextView_OffersPeopleAndSoftwareSystemsOnly()
    {
        // Act.
        var labels = Items(C4ViewKind.SystemContext).Select(item => item.Label).ToArray();

        // Assert.
        Assert.Equal(["Person", "Software System"], labels);
    }

    [Fact]
    public void AContainerView_OffersContainers_ButNotComponents()
    {
        // Act.
        var labels = Items(C4ViewKind.Container).Select(item => item.Label).ToArray();

        // Assert.
        Assert.Contains("Container", labels);
        Assert.DoesNotContain("Component", labels);
    }

    [Fact]
    public void ADeploymentView_OffersNodesAndInstances_AndNoPeople()
    {
        // Act.
        var labels = Items(C4ViewKind.Deployment).Select(item => item.Label).ToArray();

        // Assert.
        Assert.Contains("Deployment Node", labels);
        Assert.Contains("Infrastructure Node", labels);
        Assert.Contains("Container Instance", labels);
        Assert.DoesNotContain("Person", labels);
    }

    [Theory]
    [InlineData(C4ViewKind.SystemLandscape)]
    [InlineData(C4ViewKind.SystemContext)]
    [InlineData(C4ViewKind.Container)]
    [InlineData(C4ViewKind.Component)]
    [InlineData(C4ViewKind.Dynamic)]
    [InlineData(C4ViewKind.Deployment)]
    public void NoViewOffersAKindItWouldThenRefuse(C4ViewKind kind)
    {
        // Act.
        // The palette and the validator read the same rule, so a user cannot be offered
        // something that is immediately reported as a violation.
        var permitted = C4RuleSet.PermittedKinds(kind);

        // Assert.
        Assert.Equal(permitted.Count, Items(kind).Count);
    }

    [Fact]
    public void EveryItem_IsBackedByAnAction_SoADropDoesWhatTheMenuDoes()
    {
        // Arrange, act and assert.
        Assert.All(Items(C4ViewKind.Container), item =>
        {
            Assert.NotEmpty(item.DropActionId);
            Assert.StartsWith("c4.", item.Id, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AContainersDescription_SaysItIsNotADockerContainer()
    {
        // Act.
        // C4 is explicit that a container is an application or a data store, and the word
        // misleads almost everyone who has met Docker first (Requirement 6.3).
        var container = Items(C4ViewKind.Container).Single(item => item.Label == "Container");

        // Assert.
        Assert.Contains("not a Docker container", container.Description, StringComparison.OrdinalIgnoreCase);
    }
}
