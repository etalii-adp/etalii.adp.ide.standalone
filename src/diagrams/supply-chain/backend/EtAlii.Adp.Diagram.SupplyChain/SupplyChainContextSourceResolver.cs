using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// Resolves a group, node or flow of a <c>.supply</c> document to a selection - and tells the
/// sessions what is selected, which is what the trace highlight is drawn from.
/// </summary>
/// <remarks>
/// <para>
/// <b>A placement or a finished connect gesture is never traced</b>: neither is a selection of
/// anything drawn.
/// </para>
/// </remarks>
public sealed class SupplyChainContextSourceResolver : DiagramElementSourceResolver<LineDocument, SupplyChainModel>
{
    private readonly SupplyChainElementMapper _mapper;
    private readonly SupplyChainSelections _selections;

    public SupplyChainContextSourceResolver(
        DiagramFileRouter router,
        ISupplyChainDocumentStore documents,
        SupplyChainElementMapper mapper,
        SupplyChainSelections selections)
        : base(router, documents)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(selections);
        _mapper = mapper;
        _selections = selections;
    }

    /// <inheritdoc />
    protected override DiagramOrigin Origin => Diagram.SupplyChain.Origin;

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a supply chain diagram.",
        "That is no longer in this diagram.",
        "New node",
        "New flow");

    /// <inheritdoc />
    protected override DiagramElement? Drawn(SupplyChainModel model, string elementId) =>
        _mapper.All(model).FirstOrDefault(candidate => candidate.Id == elementId);

    /// <inheritdoc />
    /// <remarks>A selection of something drawn is also told to the sessions, which trace from it.</remarks>
    protected override IDisposable Follow(ShortGuid watchId, string bodyPath, string elementId, Action changed)
    {
        var following = base.Follow(watchId, bodyPath, elementId, changed);
        var selected = GestureIds.IsPlacement(elementId) || GestureIds.IsRelation(elementId) || elementId.Length == 0
            ? null
            : _selections.Select(watchId, bodyPath, elementId);

        return Disposable.From(() =>
        {
            following.Dispose();
            selected?.Dispose();
        });
    }

    /// <inheritdoc />
    protected override (IReadOnlyList<string> Path, string Text)? Describe(SupplyChainModel model, string elementId)
    {
        if (model.Groups.FirstOrDefault(candidate => candidate.Id == elementId) is { } group)
        {
            var text = group.Name.Length > 0 ? group.Name : group.Id;
            return ([text], text);
        }

        if (model.Nodes.FirstOrDefault(candidate => candidate.Id == elementId) is { } node)
        {
            var text = node.Name.Length > 0 ? node.Name : node.Id;
            return ([text], text);
        }

        if (model.Flows.FirstOrDefault(candidate => candidate.Id == elementId) is { } flow)
        {
            var text = flow.Product.Length > 0 ? flow.Product : $"{flow.From} → {flow.To}";
            return ([text], text);
        }

        return null;
    }
}
