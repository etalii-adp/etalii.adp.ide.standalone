using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

public class DiagramOptionTreeTests
{
    private static DiagramDefinition Definition(string vendor, string type, string title, string subtype = "")
        => new(new DiagramOrigin(vendor, type, subtype), title);

    [Fact]
    public void Build_WithNoDefinitions_ReturnsNoOptions()
    {
        Assert.Empty(DiagramOptionTree.Build([]));
    }

    [Fact]
    public void Build_WithNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DiagramOptionTree.Build(null!));
    }

    [Fact]
    public void Build_GroupsByVendor_WithVendorsAsNonSelectableGroups()
    {
        var tree = DiagramOptionTree.Build([
            Definition("c4", "context", "System Context"),
            Definition("uml", "class", "Class diagram"),
            Definition("c4", "container", "Container"),
        ]);

        Assert.Equal(["c4", "uml"], tree.Select(vendor => vendor.Label));
        Assert.All(tree, vendor => Assert.False(vendor.Selectable));
        Assert.All(tree, vendor => Assert.Equal(vendor.Label, vendor.Id));
    }

    [Fact]
    public void Build_ListsEachTypeUnderItsVendor_ByTitle_WithTheOriginKeyAsId()
    {
        var tree = DiagramOptionTree.Build([
            Definition("c4", "context", "System Context"),
            Definition("c4", "container", "Container"),
        ]);

        var c4 = Assert.Single(tree);
        Assert.NotNull(c4.Children);
        Assert.Equal(["Container", "System Context"], c4.Children.Select(node => node.Label));
        Assert.Equal(["c4/container", "c4/context"], c4.Children.Select(node => node.Id));
        Assert.All(c4.Children, node => Assert.True(node.Selectable));
    }

    [Fact]
    public void Build_DoesNotGroupByType()
    {
        // Requirement 5.2: Type is the diagram type itself, so it is never a group of one.
        var tree = DiagramOptionTree.Build([Definition("c4", "context", "System Context")]);

        var leaf = Assert.Single(Assert.Single(tree).Children!);
        Assert.Equal("System Context", leaf.Label);
        Assert.True(leaf.Selectable);
        Assert.Null(leaf.Children);
    }

    [Fact]
    public void Build_NestsASubtypeUnderTheTypeItRefines()
    {
        var tree = DiagramOptionTree.Build([
            Definition("c4", "component", "Component"),
            Definition("c4", "component", "Code", subtype: "code"),
        ]);

        var component = Assert.Single(Assert.Single(tree).Children!);
        Assert.Equal("c4/component", component.Id);
        Assert.True(component.Selectable);
        var code = Assert.Single(component.Children!);
        Assert.Equal("c4/component/code", code.Id);
        Assert.Equal("Code", code.Label);
        Assert.True(code.Selectable);
    }

    [Fact]
    public void Build_SynthesisesANonSelectableParent_WhenOnlySubtypesExist()
    {
        var tree = DiagramOptionTree.Build([
            Definition("archimate", "layer", "Business", subtype: "business"),
            Definition("archimate", "layer", "Application", subtype: "application"),
        ]);

        var layer = Assert.Single(Assert.Single(tree).Children!);
        Assert.Equal("archimate/layer", layer.Id);
        Assert.Equal("layer", layer.Label);
        Assert.False(layer.Selectable);
        Assert.Equal(["Application", "Business"], layer.Children!.Select(node => node.Label));
        Assert.Equal(["archimate/layer/application", "archimate/layer/business"], layer.Children!.Select(node => node.Id));
    }

    [Fact]
    public void Build_IsDeterministic_RegardlessOfInputOrder()
    {
        var forward = DiagramOptionTree.Build([
            Definition("uml", "class", "Class"),
            Definition("c4", "context", "System Context"),
            Definition("c4", "container", "Container"),
        ]);
        var backward = DiagramOptionTree.Build([
            Definition("c4", "container", "Container"),
            Definition("c4", "context", "System Context"),
            Definition("uml", "class", "Class"),
        ]);

        // Projected, because a record holding an array compares that array by reference.
        Assert.Equal(Shape(forward), Shape(backward));
    }

    private static IEnumerable<(string Id, string Label, bool Selectable)> Shape(IEnumerable<ContextOptionNode> nodes)
        => Flatten(nodes).Select(node => (node.Id, node.Label, node.Selectable));

    [Fact]
    public void Build_EverySelectableIdIsAnOriginKey_AndUnique()
    {
        var definitions = new[]
        {
            Definition("c4", "context", "System Context"),
            Definition("c4", "component", "Component"),
            Definition("c4", "component", "Code", subtype: "code"),
            Definition("uml", "class", "Class"),
        };

        var ids = Flatten(DiagramOptionTree.Build(definitions))
            .Where(node => node.Selectable)
            .Select(node => node.Id)
            .ToList();

        Assert.Equal(definitions.Select(d => d.Origin.Key).OrderBy(k => k, StringComparer.Ordinal), ids.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static IEnumerable<ContextOptionNode> Flatten(IEnumerable<ContextOptionNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children ?? []))
            {
                yield return child;
            }
        }
    }
}
