using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Makes a Databricks element selectable: resolves an <c>element_id</c> against the file named
/// by the enclosing level and verifies the id really is in it. One is registered per type of the
/// family, each answering only for files of its own type.
/// </summary>
/// <remarks>
/// Every diagram type answers for <c>element_id</c>, and none can tell from the id alone
/// whether the element is one of its own - only the file above it says that. So this refuses a
/// file that does not route to its type, and the selection resolver moves on.
/// </remarks>
public sealed class DatabricksContextSourceResolver : DiagramElementSourceResolver<LineDocument, DatabricksModel>
{
    private readonly DatabricksElementMapper _mapper;

    public DatabricksContextSourceResolver(DiagramOrigin origin, DiagramFileRouter router, IDatabricksDocumentStore documents, DatabricksElementMapper mapper)
        : base(router, documents)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(mapper);

        Origin = origin;
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override DiagramOrigin Origin { get; }

    /// <inheritdoc />
    /// <remarks>A drop and a drawn relation are both proposed as the element about to exist.</remarks>
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a Databricks configuration.",
        "That element is no longer in this file.",
        "New element",
        "New element");

    /// <inheritdoc />
    protected override (IReadOnlyList<string> Path, string Text)? Describe(DatabricksModel model, string elementId) =>
        DatabricksSelection.Describe(model, elementId) is { } text ? ([text], text) : null;

    /// <inheritdoc />
    /// <remarks>
    /// The same payload the canvas already has, taken from the mapper rather than assembled here,
    /// so the selection and the stream can never describe one element two ways.
    /// </remarks>
    protected override DiagramElement? Drawn(DatabricksModel model, string elementId) =>
        _mapper.Every(model, Origin.Type).FirstOrDefault(candidate => candidate.Id == elementId);
}
