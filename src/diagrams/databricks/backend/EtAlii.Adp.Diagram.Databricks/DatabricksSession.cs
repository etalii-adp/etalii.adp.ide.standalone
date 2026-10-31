using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// One open Databricks diagram for one connection. Core owns the stream, the authorization and
/// the connection lifetime; this owns which of the family's three readings the diagram shows
/// and expresses every change as a <see cref="DiagramDelta"/>.
/// </summary>
/// <remarks>
/// <para>
/// It filters to what the reader can see: <see cref="DocumentDiagramSession{TBody, TModel}.UpdateView"/>
/// renders against the last reported viewport and answers with the difference from what this
/// connection already holds, so panning and zooming bring content in rather than merely moving
/// over a set fixed at open (view-delta-adoption Requirement 1.1). A connection that never reports
/// keeps the whole diagram, because the viewport starts <see cref="DiagramViewport.Unbounded"/>.
/// </para>
/// <para>
/// A drag never touches the body file: repositioning dispatches the core
/// <see cref="PlaceInRegistrationCommand"/>, which writes the <c>.adp</c>'s <c>layout:</c>
/// block through the FBL library's registration writer (databricks-disl-fbl Requirement 5.5): one
/// entry spliced in or changed and every other byte left as it was, with no stale entry pruned,
/// because the command sets no known ids (ruled by the user in chat, 2026-10-11). The change
/// comes back through the registration's reload, and every session on the file re-renders with
/// the authored position overlaid.
/// </para>
/// </remarks>
public sealed class DatabricksSession : DocumentDiagramSession<LineDocument, DatabricksModel>
{
    private readonly string? _registrationPath;
    private readonly ToolOrigin _origin;
    private readonly string? _resourceKey;
    private readonly DatabricksElementMapper _mapper;

    /// <param name="history">The project's history, so a drag is one undo away. Null makes the diagram read-only.</param>
    public DatabricksSession(
        string bodyPath,
        string? registrationPath,
        ToolOrigin origin,
        string? resourceKey,
        IDatabricksDocumentStore documents,
        DatabricksElementMapper mapper,
        IHistoryStack? history = null)
        : base(bodyPath, documents, history)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(mapper);

        _registrationPath = registrationPath;
        _origin = origin;
        _resourceKey = resourceKey;
        _mapper = mapper;
    }

    /// <summary>Nothing in these diagrams nests under a parent - their place is a position.</summary>
    protected override string ParentRefusal => "A Databricks element has no parent to move it under; dragging changes where it sits on the canvas.";

    /// <inheritdoc />
    protected override IReadOnlyList<DiagramElement> Render(DatabricksModel model, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(model);

        var stored = _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

        return _mapper.Visible(Elements(model, stored), viewport);
    }

    /// <summary>
    /// Stores an authored position in the registration's <c>layout:</c> block, as one undoable
    /// command - and never writes the body file (Requirement 7.6/7.7).
    /// </summary>
    protected override DiagramPlacement PlacementOf(DatabricksModel model, string elementId, double x, double y)
    {
        if (_registrationPath is not { Length: > 0 })
        {
            return DiagramPlacement.Refused("This diagram was opened without a registration, so there is nowhere to store a position.");
        }

        if (elementId.StartsWith("edge:", StringComparison.Ordinal)
            || elementId.StartsWith("override:", StringComparison.Ordinal)
            || elementId.StartsWith("flow:", StringComparison.Ordinal))
        {
            // An edge has no position of its own - it follows its endpoints.
            return DiagramPlacement.Refused("That element is not something this diagram can move.");
        }

        return DiagramPlacement.By(new PlaceInRegistrationCommand(_registrationPath, elementId, x, y));
    }

    /// <summary>The reading this session's type shows of <paramref name="model"/>, with the stored positions overlaid.</summary>
    private IReadOnlyList<DiagramElement> Elements(DatabricksModel model, IReadOnlyDictionary<string, RegistrationPosition> stored)
    {
        switch (_origin.Type)
        {
            case "job":
            {
                var job = Pick(model.Jobs, job => job.Key);
                if (job is null)
                {
                    return [];
                }

                var positions = RegistrationLayout.Apply(DatabricksJobLayout.Positions(job), stored);
                return _mapper.JobElements(job, positions);
            }

            case "pipeline":
            {
                var pipeline = Pick(model.Pipelines, pipeline => pipeline.Key);
                if (pipeline is null)
                {
                    return [];
                }

                var positions = RegistrationLayout.Apply(DatabricksPipelineLayout.Positions(pipeline), stored);
                return _mapper.PipelineElements(pipeline, positions);
            }

            default:
            {
                var positions = RegistrationLayout.Apply(DatabricksBundleLayout.Positions(model.Bundle), stored);
                return _mapper.BundleElements(model.Bundle, positions);
            }
        }
    }

    /// <summary>
    /// The declaration this session shows: the one the <c>resource:</c> header names, or the
    /// file's first. A key that names nothing yields the empty diagram rather than a crash -
    /// the header may outlive a rename made outside ADP.
    /// </summary>
    private T? Pick<T>(IReadOnlyList<T> candidates, Func<T, string> key)
        where T : class =>
        _resourceKey is { Length: > 0 }
            ? candidates.FirstOrDefault(candidate => key(candidate) == _resourceKey)
            : candidates.FirstOrDefault();
}
