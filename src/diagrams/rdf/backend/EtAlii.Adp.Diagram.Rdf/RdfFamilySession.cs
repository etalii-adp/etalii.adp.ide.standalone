using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One open diagram of the family for one connection, whichever reading it was opened under: the
/// session base over the family's one store, with what the reading draws supplied by an
/// <see cref="IRdfReading{TProjection}"/> (code-structure FR-038, FR-031). Four sessions that
/// differed in 24 to 47 lines are this one.
/// </summary>
/// <remarks>
/// <para>
/// <b>A drag never touches the RDF file.</b> A position is stored in the registration's
/// <c>layout:</c> block by the core <see cref="SetRegistrationLayoutCommand"/>, keyed by the
/// element's id, which is safe in the block's grammar because IRIs cannot contain spaces. Without
/// a registration there is nowhere to store one, and the reading refuses the ids whose identity
/// does not survive a reparse, such as a blank node's ordinal.
/// </para>
/// <para>
/// <b>The whole document is projected and laid out whatever the viewport says, and only then
/// filtered.</b> A layout of the visible set would move every element as the reader panned. The
/// layout is kept between reports, since a pan changes the viewport and not the document, and is
/// made again for the next model the store holds - which is the only thing that can invalidate
/// it, a change to the registration's positions included, because the reload bridge rereads the
/// body when its registration changes.
/// </para>
/// </remarks>
/// <typeparam name="TProjection">What the reading projects a document's model into.</typeparam>
public sealed class RdfFamilySession<TProjection> : DocumentDiagramSession<LineDocument, RdfModel>
{
    private readonly string? _registrationPath;

    /// <summary>The whole document's projection and layout, and the model they were made from.</summary>
    private (RdfModel Model, TProjection Projection, IReadOnlyDictionary<string, RegistrationPosition> Positions)? _laidOut;

    /// <param name="bodyPath">The document this session shows.</param>
    /// <param name="registrationPath">The registration that stores authored positions, or null for a body opened without one.</param>
    /// <param name="documents">The family's one store.</param>
    /// <param name="reading">What this reading draws, and what it refuses to arrange.</param>
    /// <param name="history">The project's history, or null for a session that may not edit.</param>
    public RdfFamilySession(
        string bodyPath,
        string? registrationPath,
        IRdfDocumentStore documents,
        IRdfReading<TProjection> reading,
        IHistoryStack? history = null)
        : base(bodyPath, documents, history)
    {
        ArgumentNullException.ThrowIfNull(reading);
        _registrationPath = registrationPath;
        Reading = reading;
    }

    /// <summary>The reading this session was opened under.</summary>
    public IRdfReading<TProjection> Reading { get; }

    /// <inheritdoc />
    protected override string ParentRefusal => Reading.ParentRefusal;

    /// <inheritdoc />
    protected override IReadOnlyList<DiagramElement> Render(RdfModel model, DiagramViewport viewport)
    {
        (TProjection projection, IReadOnlyDictionary<string, RegistrationPosition> positions) = LaidOut(model);
        return Reading.Elements(Reading.InView(projection, positions, viewport), positions);
    }

    /// <inheritdoc />
    protected override DiagramPlacement PlacementOf(RdfModel model, string elementId, double x, double y)
    {
        if (_registrationPath is not { Length: > 0 })
        {
            // Registering the file lifts this (rdf-diagram Requirement 4.3).
            return DiagramPlacement.Refused("This diagram was opened without a registration, so there is nowhere to store a position. Register the file to arrange it.");
        }

        return Reading.Refusal(elementId) is { } refusal
            ? DiagramPlacement.Refused(refusal)
            : DiagramPlacement.By(new SetRegistrationLayoutCommand(_registrationPath, elementId, x, y));
    }

    /// <summary>The whole of <paramref name="model"/>, projected and positioned, made again only for another model.</summary>
    private (TProjection Projection, IReadOnlyDictionary<string, RegistrationPosition> Positions) LaidOut(RdfModel model)
    {
        if (_laidOut is { } cached && ReferenceEquals(cached.Model, model))
        {
            return (cached.Projection, cached.Positions);
        }

        var projection = Reading.Project(model);
        var stored = _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);
        var positions = Reading.Positions(projection, stored);
        _laidOut = (model, projection, positions);
        return (projection, positions);
    }
}
