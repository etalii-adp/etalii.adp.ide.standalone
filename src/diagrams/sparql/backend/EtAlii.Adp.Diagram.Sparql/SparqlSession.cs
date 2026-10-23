using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// One open query diagram for one connection. Core owns the stream, the authorization and the
/// connection lifetime; this owns the query reading and expresses every change as a
/// <see cref="DiagramDelta"/>.
/// </summary>
/// <remarks>
/// <para>
/// Repositioning is the only mutating gesture this module has, and it never touches the
/// <c>.rq</c>: it dispatches the core <see cref="SetRegistrationLayoutCommand"/>, which writes
/// the <c>.adp</c>'s <c>layout:</c> block. The query file has no writer anywhere in this module,
/// so its byte-identical round trip holds by construction rather than by care.
/// </para>
/// <para>
/// Following the store, the baseline, the viewport and the dispatch are the session base's; this
/// says how a query renders and where a drop is stored.
/// </para>
/// </remarks>
public sealed class SparqlSession(
    string bodyPath,
    string? registrationPath,
    ISparqlDocumentStore documents,
    SparqlElementMapper mapper,
    IHistoryStack? history = null)
    : DocumentDiagramSession<string, SparqlDocument>(bodyPath, documents, history)
{
    private readonly SparqlElementMapper _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));

    /// <inheritdoc />
    protected override string ParentRefusal => SparqlDefinition.ReparentRefusal;

    /// <summary>
    /// Stores an authored position in the registration's <c>layout:</c> block, as one undoable
    /// command - the module's single mutating gesture, and one that leaves the query untouched
    /// (Requirement 5.1, 6.3).
    /// </summary>
    protected override DiagramPlacement PlacementOf(SparqlDocument model, string elementId, double x, double y)
    {
        if (registrationPath is not { Length: > 0 })
        {
            // Registering the file lifts this (Requirement 5.3).
            return DiagramPlacement.Refused("This query was opened without a registration, so there is nowhere to store a position. Register the file to arrange it.");
        }

        // Why this element cannot carry an authored position (Requirement 5.4): chrome by its own sentence, an element
        // by the definition's, the notation's refusal of a move of its kind or the reason of its ephemeral id rule.
        return (SparqlChrome.MoveRefusal(elementId) ?? SparqlDefinition.MoveRefusal(model, elementId)) is { } refusal
            ? DiagramPlacement.Refused(refusal)
            : DiagramPlacement.By(new SetRegistrationLayoutCommand(registrationPath, elementId, x, y));
    }

    /// <inheritdoc />
    protected override IReadOnlyList<DiagramElement> Render(SparqlDocument model, DiagramViewport viewport)
    {
        var projection = model.Drawing;
        var stored = registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

        // Layout applies the stored positions itself, because a region anchor moves its whole
        // frame rather than one element - which the element-by-element overlay could not express.
        var layout = SparqlLayout.Compute(projection, stored);

        // Through the viewport, always. When this rendered unfiltered while UpdateView
        // filtered, an ordinary edit re-sent every element the viewport had just culled and
        // the two paths disagreed about what the client held.
        return _mapper.Visible(projection, layout, viewport);
    }
}
