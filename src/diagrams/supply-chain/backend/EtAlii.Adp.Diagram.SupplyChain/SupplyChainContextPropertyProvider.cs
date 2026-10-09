using System.Globalization;
using EtAlii.Adp.Context;
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
public sealed class SupplyChainContextPropertyProvider : IContextPropertyProvider
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

    private readonly IHistoryStackStore _historyStacks;
    private readonly ISupplyChainDocumentStore _documents;

    public SupplyChainContextPropertyProvider(IHistoryStackStore historyStacks, ISupplyChainDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Diagram.IsBody(target.ResolvedFullPath))
        {
            return Rows([]);
        }

        var model = _documents.GetOrLoad(target.ResolvedFullPath).Model;

        if (SupplyChainEdits.NodeOf(model, target.ElementId) is { } node)
        {
            var group = SupplyChainEdits.GroupOf(model, node.Group);
            return Rows(
            [
                new ContextPropertyDefinition(NameProperty, "Name", node.Name, Group: IdentityGroup),
                new ContextPropertyDefinition(StageProperty, "Stage", node.Type, ContextPropertyEditor.Choice, Group: IdentityGroup, Candidates: SupplyChainNodeTypes.All),
                new ContextPropertyDefinition(GroupProperty, "Group", group is null ? NoGroup : NameOf(group), ContextPropertyEditor.Choice, Group: IdentityGroup, Candidates: GroupChoices(model)),
                new ContextPropertyDefinition(DescriptionProperty, "Description", node.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
                new ContextPropertyDefinition(QuantityProperty, "Quantity", Number(node.Quantity), Group: AmountGroup),
                new ContextPropertyDefinition(UnitProperty, "Unit", node.Unit, Group: AmountGroup),
                new ContextPropertyDefinition(StepProperty, "Step", Number(node.Step ?? SupplyChainGeometry.DefaultStep), Group: AmountGroup),
            ]);
        }

        if (SupplyChainEdits.FlowOf(model, target.ElementId) is { } flow)
        {
            return Rows(
            [
                new ContextPropertyDefinition(ProductProperty, "Product", flow.Product, Group: IdentityGroup),
                new ContextPropertyDefinition(DescriptionProperty, "Description", flow.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
                new ContextPropertyDefinition(VolumeProperty, "Volume", Number(flow.Volume), Group: AmountGroup),
                new ContextPropertyDefinition(UnitProperty, "Unit", flow.Unit, Group: AmountGroup),
                new ContextPropertyDefinition(StepProperty, "Step", Number(flow.Step ?? SupplyChainGeometry.DefaultStep), Group: AmountGroup),
            ]);
        }

        if (SupplyChainEdits.GroupOf(model, target.ElementId) is { } frame)
        {
            return Rows(
            [
                new ContextPropertyDefinition(NameProperty, "Name", frame.Name, Group: IdentityGroup),
                new ContextPropertyDefinition(DescriptionProperty, "Description", frame.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
            ]);
        }

        return Rows([]);
    }

    /// <inheritdoc />
    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var body = target.ResolvedFullPath;
        var model = _documents.GetOrLoad(body).Model;

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
            return ContextPropertyResult.Failure($"'{propertyId}' cannot be edited on this selection.");
        }

        if (key == SupplyChainKeys.Group)
        {
            // The grid offers names; the document keeps ids.
            value = value == NoGroup || value.Length == 0
                ? ""
                : model.Groups.FirstOrDefault(group => NameOf(group) == value)?.Id ?? value;
        }

        var command = new SetSupplyChainPropertyCommand(body, target.ElementId, key, value);
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    private static string NameOf(SupplyChainGroup group) => group.Name.Length > 0 ? group.Name : group.Id;

    private static IReadOnlyList<string> GroupChoices(SupplyChainModel model) =>
    [
        NoGroup,
        .. model.Groups.Where(group => group.Id.Length > 0).Select(NameOf).Distinct(StringComparer.Ordinal),
    ];

    private static string Number(double? value) => value is { } number ? number.ToString("0.####", CultureInfo.InvariantCulture) : "";

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Rows(IReadOnlyList<ContextPropertyDefinition> rows) =>
        ValueTask.FromResult(rows);
}
