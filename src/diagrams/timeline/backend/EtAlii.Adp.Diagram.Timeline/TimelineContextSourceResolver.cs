using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Makes a timeline element - or a connection - selectable: resolves an <c>element_id</c>
/// against the document named by the enclosing level and verifies the id really is in it.
/// </summary>
/// <remarks>
/// Every diagram type answers for <c>element_id</c>, and none can tell from the id alone
/// whether the element is one of its own - only the file above it says that. So this refuses a
/// file that is not a <c>.tml</c> and the selection resolver moves on to the next resolver,
/// which is what lets every module's element resolver coexist.
/// </remarks>
public sealed class TimelineContextSourceResolver : DiagramElementSourceResolver<LineDocument, TimelineModel>
{
    private readonly TimelineElementMapper _mapper;

    public TimelineContextSourceResolver(DiagramFileRouter router, ITimelineDocumentStore documents, TimelineElementMapper mapper)
        : base(router, documents)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override DiagramOrigin Origin => Diagram.Timeline.Origin;

    /// <inheritdoc />
    /// <remarks>A drop and a drawn relation are both proposed as the element about to exist.</remarks>
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a timeline.",
        "That element is no longer on this timeline.",
        "New element",
        "New element");

    /// <inheritdoc />
    /// <remarks>
    /// The same payload the canvas already has, taken from the mapper rather than assembled here,
    /// so the selection and the stream can never describe one element two ways.
    /// </remarks>
    protected override DiagramElement? Drawn(TimelineModel model, string elementId) =>
        _mapper.Elements(model).FirstOrDefault(candidate => candidate.Id == elementId);

    /// <inheritdoc />
    protected override (IReadOnlyList<string> Path, string Text)? Describe(TimelineModel model, string elementId)
    {
        var element = TimelineEdits.ElementOf(model, elementId);
        if (element is not null)
        {
            var text = element.Label.Length > 0 ? element.Label : element.Id;
            return ([text], text);
        }

        var connection = TimelineEdits.ConnectionOf(model, elementId);
        if (connection is not null)
        {
            var text = connection.Label.Length > 0
                ? connection.Label
                : $"{connection.From} → {connection.To}";
            return ([text], text);
        }

        return null;
    }
}
