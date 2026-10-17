using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The property rows of a selected node, flow or group, contributed as data the grid renders
/// without understanding.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row edits through <see cref="SetSupplyChainPropertyCommand"/></b>, so a value typed in
/// the grid is checked by the same rules as one stepped on the canvas, and lands on the project's
/// undo stack like any other edit.
/// </para>
/// <para>
/// <b>The group row offers names, and writes the id.</b> A person picks "Taiwan", not
/// <c>taiwan</c>; the document keeps the id so a renamed group keeps its members.
/// </para>
/// </remarks>
public sealed class SupplyChainContextPropertyProvider(IHistoryStackStore historyStacks, ISupplyChainDocumentStore documents)
    : DiagramPropertyProvider<LineDocument, SupplyChainModel>(historyStacks, documents)
{
    /// <summary>The name of a node or a group.</summary>
    public const string NameProperty = "supply-chain.name";

    /// <summary>The stage of a node.</summary>
    public const string StageProperty = "supply-chain.stage";

    /// <summary>The group a node is drawn inside.</summary>
    public const string GroupProperty = "supply-chain.group";

    /// <summary>A node's quantity.</summary>
    public const string QuantityProperty = "supply-chain.quantity";

    /// <summary>A flow's product.</summary>
    public const string ProductProperty = "supply-chain.product";

    /// <summary>A flow's volume.</summary>
    public const string VolumeProperty = "supply-chain.volume";

    /// <summary>What a quantity or a volume counts.</summary>
    public const string UnitProperty = "supply-chain.unit";

    /// <summary>How much one + or − changes a quantity or a volume.</summary>
    public const string StepProperty = "supply-chain.step";

    /// <summary>The description of anything.</summary>
    public const string DescriptionProperty = "supply-chain.description";

    /// <summary>The choice that takes a node out of every group.</summary>
    public const string NoGroup = "(none)";

    private const string IdentityGroup = "Identity";
    private const string AmountGroup = "Amount";

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <inheritdoc />
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(DocumentEntry<LineDocument, SupplyChainModel> entry, string elementId)
    {
        var model = entry.Model;

        if (model.NodeOf(elementId) is { } node)
        {
            var group = model.GroupOf(node.Group);
            return
            [
                new ContextPropertyDefinition(NameProperty, "Name", node.Name, Group: IdentityGroup),
                new ContextPropertyDefinition(StageProperty, "Stage", node.Type, ContextPropertyEditor.Choice, Group: IdentityGroup, Candidates: SupplyChainNodeTypes.All),
                new ContextPropertyDefinition(GroupProperty, "Group", group is null ? NoGroup : NameOf(group), ContextPropertyEditor.Choice, Group: IdentityGroup, Candidates: GroupChoices(model)),
                new ContextPropertyDefinition(DescriptionProperty, "Description", node.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
                new ContextPropertyDefinition(QuantityProperty, "Quantity", DslNumber.Format(node.Quantity), Group: AmountGroup),
                new ContextPropertyDefinition(UnitProperty, "Unit", node.Unit, Group: AmountGroup),
                new ContextPropertyDefinition(StepProperty, "Step", DslNumber.Format(node.Step ?? SupplyChainGeometry.DefaultStep), Group: AmountGroup),
            ];
        }

        if (model.FlowOf(elementId) is { } flow)
        {
            return
            [
                new ContextPropertyDefinition(ProductProperty, "Product", flow.Product, Group: IdentityGroup),
                new ContextPropertyDefinition(DescriptionProperty, "Description", flow.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
                new ContextPropertyDefinition(VolumeProperty, "Volume", DslNumber.Format(flow.Volume), Group: AmountGroup),
                new ContextPropertyDefinition(UnitProperty, "Unit", flow.Unit, Group: AmountGroup),
                new ContextPropertyDefinition(StepProperty, "Step", DslNumber.Format(flow.Step ?? SupplyChainGeometry.DefaultStep), Group: AmountGroup),
            ];
        }

        if (model.GroupOf(elementId) is { } frame)
        {
            return
            [
                new ContextPropertyDefinition(NameProperty, "Name", frame.Name, Group: IdentityGroup),
                new ContextPropertyDefinition(DescriptionProperty, "Description", frame.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
            ];
        }

        return [];
    }

    /// <inheritdoc />
    protected override DiagramPropertyChange Change(ContextTarget target, SupplyChainModel model, string propertyId, string value)
    {
        string? key = propertyId switch
        {
            NameProperty => SupplyChainKeys.Name,
            StageProperty => SupplyChainKeys.Type,
            GroupProperty => SupplyChainKeys.Group,
            QuantityProperty => SupplyChainKeys.Quantity,
            ProductProperty => SupplyChainKeys.Product,
            VolumeProperty => SupplyChainKeys.Volume,
            UnitProperty => SupplyChainKeys.Unit,
            StepProperty => SupplyChainKeys.Step,
            DescriptionProperty => SupplyChainKeys.Description,
            _ => null,
        };

        if (key is null)
        {
            return DiagramPropertyChange.Refused($"'{propertyId}' cannot be edited on this selection.");
        }

        if (key == SupplyChainKeys.Group)
        {
            // The grid offers names; the document keeps ids.
            value = value == NoGroup || value.Length == 0
                ? ""
                : model.Groups.FirstOrDefault(group => NameOf(group) == value)?.Id ?? value;
        }

        return DiagramPropertyChange.By(new SetSupplyChainPropertyCommand(target.ResolvedFullPath, target.ElementId, key, value));
    }

    private static string NameOf(SupplyChainGroup group) => group.Name.Length > 0 ? group.Name : group.Id;

    private static IReadOnlyList<string> GroupChoices(SupplyChainModel model) =>
    [
        NoGroup,
        .. model.Groups.Where(group => group.Id.Length > 0).Select(NameOf).Distinct(StringComparer.Ordinal),
    ];
}
