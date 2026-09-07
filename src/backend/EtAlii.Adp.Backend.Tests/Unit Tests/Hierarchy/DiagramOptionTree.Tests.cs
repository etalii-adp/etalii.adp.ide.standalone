using EtAlii.Adp.Common;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

public class DiagramOptionTreeTests
{
    private static DiagramDefinition Definition(
        string vendor,
        string type,
        string title,
        string subtype = "",
        DiagramSubject subject = DiagramSubject.Document)
        => new(new DiagramOrigin(vendor, type, subtype), title, Subject: subject);

    /// <summary>
    /// The shape the provider will use in task 3: one look at the definition decides all three
    /// values. A folder-subject type wants no name and says why - and here is also already
    /// registered, so it cannot be chosen either. Everything else suggests a file name.
    /// </summary>
    private static ContextOptionAnnotations Annotate(DiagramDefinition definition) =>
        definition.HasFolderSubject
            ? new ContextOptionAnnotations(
                NameSuppressedReason: "This type registers the folder itself.",
                UnavailableReason: "This folder is already registered by `structure.adp`")
            : new ContextOptionAnnotations(SuggestedValue: $"{definition.Origin.Type}.adp");

    [Fact]
    public void Build_ForAFolderSubjectType_CarriesTheSuppressionAndNoSuggestion()
    {
        // Arrange and act.
        var tree = DiagramOptionTree.Build(
            [Definition("ansible", "structure", "Ansible structure", subject: DiagramSubject.Folder)],
            Annotate);

        // Assert: all three annotations reach the leaf through one seam. The suggestion being
        // empty is the point rather than an omission - an option that wants no name has
        // nothing to suggest, and a leftover suggestion would be offered for a field that is
        // not there.
        var option = Assert.Single(Assert.Single(tree).Children!);
        Assert.Equal("This type registers the folder itself.", option.NameSuppressedReason);
        Assert.Equal("This folder is already registered by `structure.adp`", option.UnavailableReason);
        Assert.Equal("", option.SuggestedValue);
    }

    [Fact]
    public void Build_ForADocumentSubjectType_CarriesTheSuggestionAndNoSuppression()
    {
        // Arrange and act.
        var tree = DiagramOptionTree.Build([Definition("c4", "context", "System Context")], Annotate);

        // Assert: today's behaviour, unchanged by the new seam.
        var option = Assert.Single(Assert.Single(tree).Children!);
        Assert.Equal("context.adp", option.SuggestedValue);
        Assert.Equal("", option.NameSuppressedReason);
        Assert.Equal("", option.UnavailableReason);
    }

    [Fact]
    public void Build_ForAGroup_CarriesNoAnnotationsAndDoesNotConsultTheCallback()
    {
        // Arrange: a vendor group, and a synthesised type group over a lone subtype - the two
        // ways a non-selectable node appears. Neither is a definition, so neither has anything
        // for the callback to answer about.
        var consulted = new List<string>();

        // Act.
        var tree = DiagramOptionTree.Build(
            [Definition("c4", "component", "Code", subtype: "code")],
            definition =>
            {
                consulted.Add(definition.Origin.Key);
                return Annotate(definition);
            });

        // Assert.
        var vendor = Assert.Single(tree);
        var typeGroup = Assert.Single(vendor.Children!);
        foreach (var group in new[] { vendor, typeGroup })
        {
            Assert.False(group.Selectable);
            Assert.Equal("", group.SuggestedValue);
            Assert.Equal("", group.NameSuppressedReason);
            Assert.Equal("", group.UnavailableReason);
        }

        // And the callback was asked once, about the one real definition - not about either
        // group, which would mean asking what a heading suggests.
        Assert.Equal(["c4/component/code"], consulted);
    }

    [Fact]
    public void Build_WithNoDefinitions_ReturnsNoOptions()
    {
        // Arrange, act and assert.
        Assert.Empty(DiagramOptionTree.Build([]));
    }

    [Fact]
    public void Build_WithNull_Throws()
    {
        // Arrange, act and assert.
        Assert.Throws<ArgumentNullException>(() => DiagramOptionTree.Build(null!));
    }

    [Fact]
    public void Build_GroupsByVendor_WithVendorsAsNonSelectableGroups()
    {
        // Arrange and act.
        var tree = DiagramOptionTree.Build([
            Definition("c4", "context", "System Context"),
            Definition("uml", "class", "Class diagram"),
            Definition("c4", "container", "Container"),
        ]);

        // Assert.
        Assert.Equal(["c4", "uml"], tree.Select(vendor => vendor.Label));
        Assert.All(tree, vendor => Assert.False(vendor.Selectable));
        Assert.All(tree, vendor => Assert.Equal(vendor.Label, vendor.Id));
    }

    [Fact]
    public void Build_ListsEachTypeUnderItsVendor_ByTitle_WithTheOriginKeyAsId()
    {
        // Arrange.
        var tree = DiagramOptionTree.Build([
            Definition("c4", "context", "System Context"),
            Definition("c4", "container", "Container"),
        ]);

        // Act and assert, step by step.
        var c4 = Assert.Single(tree);
        Assert.NotNull(c4.Children);
        Assert.Equal(["Container", "System Context"], c4.Children.Select(node => node.Label));
        Assert.Equal(["c4/container", "c4/context"], c4.Children.Select(node => node.Id));
        Assert.All(c4.Children, node => Assert.True(node.Selectable));
    }

    [Fact]
    public void Build_DoesNotGroupByType()
    {
        // Arrange.
        // Requirement 5.2: Type is the diagram type itself, so it is never a group of one.
        var tree = DiagramOptionTree.Build([Definition("c4", "context", "System Context")]);

        // Act and assert, step by step.
        var leaf = Assert.Single(Assert.Single(tree).Children!);
        Assert.Equal("System Context", leaf.Label);
        Assert.True(leaf.Selectable);
        Assert.Null(leaf.Children);
    }

    [Fact]
    public void Build_NestsASubtypeUnderTheTypeItRefines()
    {
        // Arrange.
        var tree = DiagramOptionTree.Build([
            Definition("c4", "component", "Component"),
            Definition("c4", "component", "Code", subtype: "code"),
        ]);

        // Act and assert, step by step.
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
        // Arrange.
        var tree = DiagramOptionTree.Build([
            Definition("archimate", "layer", "Business", subtype: "business"),
            Definition("archimate", "layer", "Application", subtype: "application"),
        ]);

        // Act and assert, step by step.
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
        // Arrange and act.
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

        // Assert.
        // Projected, because a record holding an array compares that array by reference.
        Assert.Equal(Shape(forward), Shape(backward));
    }

    private static IEnumerable<(string Id, string Label, bool Selectable)> Shape(IEnumerable<ContextOptionNode> nodes)
        => Flatten(nodes).Select(node => (node.Id, node.Label, node.Selectable));

    [Fact]
    public void Build_EverySelectableIdIsAnOriginKey_AndUnique()
    {
        // Arrange.
        var definitions = new[]
        {
            Definition("c4", "context", "System Context"),
            Definition("c4", "component", "Component"),
            Definition("c4", "component", "Code", subtype: "code"),
            Definition("uml", "class", "Class"),
        };

        // Act.
        var ids = Flatten(DiagramOptionTree.Build(definitions))
            .Where(node => node.Selectable)
            .Select(node => node.Id)
            .ToList();

        // Assert.
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
