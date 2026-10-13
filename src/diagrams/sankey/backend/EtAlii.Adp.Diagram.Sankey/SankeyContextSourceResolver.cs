using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Resolves a node or a flow of a <c>.skv</c> document to a selection.</summary>
public sealed class SankeyContextSourceResolver : DiagramElementSourceResolver<SankeyModel>
{
    private readonly ISankeyDocumentStore _documents;
    private readonly SankeyElementMapper _mapper;

    public SankeyContextSourceResolver(DiagramFileRouter router, ISankeyDocumentStore documents, SankeyElementMapper mapper)
        : base(router)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        _documents = documents;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override DiagramOrigin Origin => Diagram.Sankey.Origin;

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a Sankey diagram.",
        "That is no longer in this diagram.",
        "New node",
        "New flow");

    /// <inheritdoc />
    protected override SankeyModel Load(string bodyPath) => _documents.GetOrLoad(bodyPath).Model;

    /// <inheritdoc />
    protected override DiagramElement? Drawn(SankeyModel model, string elementId) =>
        _mapper.All(model).FirstOrDefault(candidate => candidate.Id == elementId);

    /// <inheritdoc />
    protected override IDisposable Follow(ShortGuid watchId, string bodyPath, string elementId, Action changed) =>
        FollowStore(_documents, bodyPath, changed);

    /// <inheritdoc />
    protected override (IReadOnlyList<string> Path, string Text)? Describe(SankeyModel model, string elementId)
    {
        if (SankeyEdits.NodeOf(model, elementId) is { } node)
        {
            return ([node.Label], node.Label);
        }

        if (SankeyEdits.FlowOf(model, elementId) is { } flow)
        {
            var text = $"{Label(model, flow.From)} → {Label(model, flow.To)}";
            return ([text], text);
        }

        return null;
    }

    private static string Label(SankeyModel model, string id) => SankeyEdits.NodeOf(model, id)?.Label ?? id;
}
