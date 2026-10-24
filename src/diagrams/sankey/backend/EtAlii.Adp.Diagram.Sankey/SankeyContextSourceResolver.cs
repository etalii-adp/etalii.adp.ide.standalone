using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Resolves a node or a flow of a <c>.skv</c> document to a selection.</summary>
public sealed class SankeyContextSourceResolver : DiagramElementSourceResolver<SankeyBody, SankeyModel>
{
    private readonly SankeyElementMapper _mapper;

    public SankeyContextSourceResolver(DiagramFileRouter router, ISankeyDocumentStore documents, SankeyElementMapper mapper)
        : base(router, documents)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override ToolOrigin Origin => Diagram.Sankey.Origin;

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a Sankey diagram.",
        "That is no longer in this diagram.",
        "New node",
        "New flow");

    /// <inheritdoc />
    protected override DiagramElement? Drawn(SankeyModel model, string elementId) =>
        _mapper.All(model).FirstOrDefault(candidate => candidate.Id == elementId);

    /// <inheritdoc />
    protected override (IReadOnlyList<string> Path, string Text)? Describe(SankeyModel model, string elementId)
    {
        if (model.NodeOf(elementId) is { } node)
        {
            return ([node.Label], node.Label);
        }

        if (model.FlowOf(elementId) is { } flow)
        {
            var text = $"{Label(model, flow.From)} → {Label(model, flow.To)}";
            return ([text], text);
        }

        return null;
    }

    private static string Label(SankeyModel model, string id) => model.NodeOf(id)?.Label ?? id;
}
