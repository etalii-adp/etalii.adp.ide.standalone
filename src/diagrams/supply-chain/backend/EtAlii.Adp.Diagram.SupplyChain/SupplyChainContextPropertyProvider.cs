using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
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
    : DiagramPropertyProvider<SupplyChainBody, SupplyChainModel>(historyStacks, documents)
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

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <inheritdoc />
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(DocumentEntry<SupplyChainBody, SupplyChainModel> entry, string elementId) =>
        // Derived from the DISL definition: a node's, a flow's or a group's form.
        SupplyChainDefinition.Rows(entry.Model, elementId);

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
}
