using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Layout;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Turns a parsed <see cref="WardleyMap"/> into the core element and delta vocabulary
/// (Requirement 10).
/// </summary>
/// <remarks>
/// <para>
/// This is where names become identities. The document says `Cup of Tea-&gt;Kettle`; the wire
/// says one id points at another. A name that resolves to nothing is <b>not</b> a failure here
/// - the link still goes out, carrying the names as written, so the canvas can mark which end
/// is dangling and the validator can report it (Requirements 14.2, 8.6).
/// </para>
/// <para>
/// Positions come from <see cref="WardleyAxis"/> and nowhere else. There is no layout: every
/// position on the wire is the author's own claim, read from the document (Requirement 7.1).
/// </para>
/// </remarks>
public sealed class WardleyElementMapper
{
    private readonly WardleyIdentities _identities;

    public WardleyElementMapper(WardleyIdentities identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        _identities = identities;
    }

    /// <summary>
    /// Every element of <paramref name="map"/> as one add - the baseline a connection opens
    /// with. One message rather than a stream of per-element ones, because the whole map is
    /// delivered anyway (Requirement 10.5).
    /// </summary>
    public IReadOnlyList<DiagramElement> Elements(WardleyMap map, IReadOnlyList<WardleyIdentityEntry> identities)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(identities);

        var ids = new Ids(map, identities);
        var elements = new List<DiagramElement> { DiagramElementOf(map) };

        // Pipeline children first, so a component that is also a pipeline parent is emitted
        // with its children already known - and so a child's own element carries its parent.
        // var childOf = new Dictionary<string, WardleyPipeline>(StringComparer.Ordinal);
        // foreach (var pipeline in map.Pipelines)
        // {
        //     foreach (var child in pipeline.Children)
        //     {
        //         childOf[WardleyIdentityKeys.Of(pipeline, child)] = pipeline;
        //     }
        // }

        foreach (var component in map.Components)
        {
            elements.Add(ComponentElement(map, component, ids));
        }

        foreach (var pipeline in map.Pipelines)
        {
            var parent = map.Components.FirstOrDefault(candidate => candidate.Name == pipeline.Parent);
            foreach (var child in pipeline.Children)
            {
                elements.Add(PipelineChildElement(child, parent, ids));
            }
        }

        foreach (var link in map.Links)
        {
            elements.Add(LinkElement(link, ids));
        }

        foreach (var note in map.Notes)
        {
            elements.Add(NoteElement(note, ids));
        }

        foreach (var annotation in map.Annotations)
        {
            elements.Add(AnnotationElement(annotation, ids));
        }

        foreach (var accelerator in map.Accelerators)
        {
            elements.Add(AcceleratorElement(accelerator, ids));
        }

        foreach (var attitude in map.Attitudes)
        {
            elements.Add(AttitudeElement(attitude, ids));
        }

        return elements;
    }

    /// <summary>
    /// The elements a reported viewport can see, in the map own 0..1 space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The viewport arrives in the map own units, which is the module business and not the
    /// shared client code (view-delta-adoption Requirement 3.4). Nothing here converts anything:
    /// an element X and Y already come from <see cref="WardleyAxis.ToPoint"/>, so both sides of
    /// the comparison are 0..1 already.
    /// </para>
    /// <para>
    /// <b>Two kinds carry a placeholder position and must never be judged by it.</b> A link is
    /// emitted at (0, 0) because it has no position of its own - it is drawn between its
    /// endpoints - and the diagram element is emitted at (0, 0) because it is the map itself
    /// rather than a thing on the map. A plain "is the point inside the rectangle" filter would
    /// therefore keep every link and the diagram element only while the reader happens to be
    /// looking at the top-left corner, and cull them everywhere else. The diagram element is
    /// always kept; a link is kept when an endpoint is kept.
    /// </para>
    /// <para>
    /// A visible component pulls in the components it links to, the way the mindmap mapper pulls
    /// in a visible node parent and children: without that a link would be delivered with one end
    /// attached to an element this connection was never sent.
    /// </para>
    /// <para>
    /// An attitude spans from one coordinate to another, so it is judged on that rectangle rather
    /// than on its corner - a reader inside a large attitude box would otherwise lose it.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DiagramElement> Visible(
        WardleyMap map,
        IReadOnlyList<WardleyIdentityEntry> identities,
        DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(identities);

        var all = Elements(map, identities);
        if (IsUnbounded(viewport))
        {
            // The viewport every connection has before it reports one. Short-circuited so the
            // baseline costs no filtering at all rather than a comparison against infinity.
            return all;
        }

        var ids = new Ids(map, identities);
        var kept = new HashSet<string>(StringComparer.Ordinal) { WardleyElementTypes.DiagramId };

        // The components in view, and then the ones they link to, so no link dangles.
        var inView = map.Components
            .Where(component => Contains(viewport, WardleyAxis.ToPoint(component.Position)))
            .Select(component => component.Name)
            .ToHashSet(StringComparer.Ordinal);

        var partners = map.Links
            .Where(link => inView.Contains(link.Source) || inView.Contains(link.Target))
            .SelectMany(link => new[] { link.Source, link.Target });
        var reachable = inView.Concat(partners).ToHashSet(StringComparer.Ordinal);

        foreach (var component in map.Components.Where(component => reachable.Contains(component.Name)))
        {
            kept.Add(ids.Of(WardleyIdentityKind.Component, component.Line));
        }

        foreach (var link in map.Links.Where(link => reachable.Contains(link.Source) && reachable.Contains(link.Target)))
        {
            kept.Add(ids.Of(WardleyIdentityKind.Link, link.Line));
        }

        foreach (var attitude in map.Attitudes.Where(attitude => Overlaps(viewport, attitude)))
        {
            kept.Add(ids.Of(WardleyIdentityKind.Attitude, attitude.Line));
        }

        // The remaining kinds are single points and are judged as such. Filtered back over the
        // full rendering rather than rebuilt, so the order the document has is the order this
        // returns and one viewport always yields one sequence.
        return all
            .Where(element => kept.Contains(element.Id) || IsPointInView(element, viewport))
            .ToArray();
    }

    private static bool IsUnbounded(DiagramViewport viewport) =>
        double.IsInfinity(viewport.MinX) || double.IsInfinity(viewport.MinY)
        || double.IsInfinity(viewport.MaxX) || double.IsInfinity(viewport.MaxY);

    /// <summary>Whether an element judged by its own point falls inside the viewport.</summary>
    /// <remarks>
    /// The two placeholder-position kinds are excluded here rather than at the call site, so a
    /// kind added later at (0, 0) cannot slip into being filtered by a position it does not have.
    /// </remarks>
    private static bool IsPointInView(DiagramElement element, DiagramViewport viewport) =>
        element.Type is not (WardleyElementTypes.Link or WardleyElementTypes.Diagram)
        && Contains(viewport, (element.X, element.Y));

    private static bool Contains(DiagramViewport viewport, (double X, double Y) point) =>
        viewport.Intersects(new Rect(point.X, point.Y, 0, 0));

    private static bool Overlaps(DiagramViewport viewport, WardleyAttitude attitude)
    {
        (double x1, double y1) = WardleyAxis.ToPoint(attitude.From);
        (double x2, double y2) = WardleyAxis.ToPoint(attitude.To);
        return viewport.Intersects(new Rect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x1 - x2), Math.Abs(y1 - y2)));
    }

    /// <summary>
    /// A pipeline as the contract's grouping: the parent stands for the children it holds
    /// (Requirement 10.4).
    /// </summary>
    public IReadOnlyList<DiagramDelta> Group(WardleyMap map, IReadOnlyList<WardleyIdentityEntry> identities)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(identities);

        var ids = new Ids(map, identities);
        var deltas = new List<DiagramDelta>();

        foreach (var pipeline in map.Pipelines.Where(candidate => candidate.Children.Count > 0))
        {
            var parent = map.Components.FirstOrDefault(candidate => candidate.Name == pipeline.Parent);
            if (parent is null)
            {
                // A pipeline naming a component that does not exist is a problem the validator
                // reports; there is simply nothing here to group under.
                continue;
            }

            var children = pipeline.Children
                .Select(child => ids.Of(WardleyIdentityKind.PipelineChild, child.Line))
                .Where(id => id.Length > 0)
                .ToArray();

            if (children.Length > 0)
            {
                deltas.Add(new DiagramGroupDelta(children, ComponentElement(map, parent, ids)));
            }
        }

        return deltas;
    }

    /// <summary>
    /// The ids of one map's elements: each element's own, by the line that declares it, and the
    /// one a statement naming a key reaches, which is its first holder's (finding B9, ruling 3).
    /// </summary>
    private sealed class Ids
    {
        private readonly Dictionary<(string Kind, uint Line), string> _byLine = [];
        private readonly Dictionary<(string Kind, string Key), string> _byKey = [];

        public Ids(WardleyMap map, IReadOnlyList<WardleyIdentityEntry> identities)
        {
            foreach (var entry in identities)
            {
                _byKey.TryAdd((entry.Kind, entry.Key), entry.Id);
            }

            foreach (var holder in WardleyIdentityKeys.All(map))
            {
                if (_byKey.TryGetValue((holder.Kind, holder.Key), out var id))
                {
                    _byLine.TryAdd((holder.Kind, holder.Line), id);
                }
            }
        }

        /// <summary>The id of the element of <paramref name="kind"/> declared on <paramref name="line"/>, or empty.</summary>
        public string Of(string kind, uint line) => _byLine.GetValueOrDefault((kind, line), "");

        /// <summary>The id of the first holder of <paramref name="key"/>, or empty.</summary>
        public string Named(string kind, string key) => _byKey.GetValueOrDefault((kind, key), "");
    }

    private DiagramElement ComponentElement(
        WardleyMap map,
        WardleyComponent component,
        Ids ids)
    {
        _ = _identities;

        var payload = new WardleyElementPayload
        {
            Name = component.Name,
            Kind = component.Kind,
            Visibility = component.Position.Visibility,
            Maturity = component.Position.Maturity,
            EvolutionStage = WardleyEvolution.StageOf(component.Position.Maturity).Label,
            Inertia = component.Inertia,
            UrlName = component.Url,
            UrlAddress = AddressOf(map, component.Url),
        };

        payload.Decorators.AddRange(component.Decorators);

        if (component.LabelOffset is { } offset)
        {
            payload.LabelOffset = new WardleyLabelOffsetPayload { X = offset.X, Y = offset.Y };
        }

        // A later holder of a repeated name shows no evolution: the `evolve` naming it attaches to
        // the first holder (ruling 3).
        if (WardleyIdentityKeys.IsFirstHolder(map, component)
            && map.Evolves.FirstOrDefault(evolve => evolve.Name == component.Name) is { } evolving)
        {
            payload.Evolve = new WardleyEvolvePayload
            {
                Maturity = evolving.Maturity,
                EvolutionStage = WardleyEvolution.StageOf(evolving.Maturity).Label,
                OverrideName = evolving.Override,
            };
        }

        (double x, double y) = WardleyAxis.ToPoint(component.Position);
        return Element(
            ids.Of(WardleyIdentityKind.Component, component.Line),
            x,
            y,
            WardleyElementTypes.Element,
            payload);
    }

    private static DiagramElement PipelineChildElement(
        WardleyPipelineChild child,
        WardleyComponent? parent,
        Ids ids)
    {
        // A child takes its visibility from its parent - the format gives it none of its own,
        // which is why only its maturity may be dragged (Requirement 7.4).
        var visibility = parent?.Position.Visibility ?? 0d;
        var position = new WardleyCoordinate(visibility, child.Maturity);
        var parentId = parent is null
            ? ""
            : ids.Of(WardleyIdentityKind.Component, parent.Line);

        var payload = new WardleyElementPayload
        {
            Name = child.Name,
            Kind = WardleyElementKind.Component,
            Visibility = visibility,
            Maturity = child.Maturity,
            EvolutionStage = WardleyEvolution.StageOf(child.Maturity).Label,
            PipelineParentId = parentId,
        };

        (double x, double y) = WardleyAxis.ToPoint(position);
        return Element(
            ids.Of(WardleyIdentityKind.PipelineChild, child.Line),
            x,
            y,
            WardleyElementTypes.Element,
            payload);
    }

    private static DiagramElement LinkElement(WardleyLink link, Ids ids)
    {
        var payload = new WardleyLinkPayload
        {
            SourceId = ids.Named(WardleyIdentityKind.Component, link.Source),
            TargetId = ids.Named(WardleyIdentityKind.Component, link.Target),
            IsFlow = link.Kind == WardleyLinkKind.Flow,
            Context = link.Context,
            // Carried even when the ids resolve, so a canvas or a problem can name the endpoint
            // the author wrote rather than an id nobody recognises.
            SourceName = link.Source,
            TargetName = link.Target,
        };

        // A link has no position of its own: it is drawn between its endpoints.
        return Element(ids.Of(WardleyIdentityKind.Link, link.Line), 0d, 0d, WardleyElementTypes.Link, payload);
    }

    private static DiagramElement NoteElement(WardleyNote note, Ids ids)
    {
        (double x, double y) = WardleyAxis.ToPoint(note.Position);
        return Element(
            ids.Of(WardleyIdentityKind.Note, note.Line),
            x,
            y,
            WardleyElementTypes.Note,
            new WardleyNotePayload { Text = note.Text });
    }

    private static DiagramElement AnnotationElement(
        WardleyAnnotation annotation,
        Ids ids)
    {
        var payload = new WardleyAnnotationPayload { Number = annotation.Number, Text = annotation.Text };
        foreach (var occurrence in annotation.Occurrences)
        {
            (double ox, double oy) = WardleyAxis.ToPoint(occurrence);
            payload.Occurrences.Add(new WardleyPointPayload { X = ox, Y = oy });
        }

        // The element sits at the first occurrence; the rest travel in the payload, because a
        // core Element has one position and Requirement 6.8 says none may be lost.
        (double x, double y) = WardleyAxis.ToPoint(annotation.Occurrences[0]);
        return Element(
            ids.Of(WardleyIdentityKind.Annotation, annotation.Line),
            x,
            y,
            WardleyElementTypes.Annotation,
            payload);
    }

    private static DiagramElement AcceleratorElement(
        WardleyAccelerator accelerator,
        Ids ids)
    {
        (double x, double y) = WardleyAxis.ToPoint(accelerator.Position);
        return Element(
            ids.Of(WardleyIdentityKind.Accelerator, accelerator.Line),
            x,
            y,
            WardleyElementTypes.Accelerator,
            new WardleyAcceleratorPayload
            {
                Name = accelerator.Name,
                IsDeaccelerator = accelerator.IsDeaccelerator,
            });
    }

    private static DiagramElement AttitudeElement(
        WardleyAttitude attitude,
        Ids ids)
    {
        (double x, double y) = WardleyAxis.ToPoint(attitude.From);
        (double x2, double y2) = WardleyAxis.ToPoint(attitude.To);

        return Element(
            ids.Of(WardleyIdentityKind.Attitude, attitude.Line),
            x,
            y,
            WardleyElementTypes.Attitude,
            new WardleyAttitudePayload
            {
                Kind = attitude.Kind,
                Opposite = new WardleyPointPayload { X = x2, Y = y2 },
            });
    }

    /// <summary>The map itself, carrying what the definition's `metamodel.diagram` declares, as the document states it.</summary>
    private static DiagramElement DiagramElementOf(WardleyMap map)
    {
        var payload = new WardleyDiagramPayload
        {
            Title = map.Title,
            Style = map.Style,
            Width = map.Size?.Width ?? 0d,
            Height = map.Size?.Height ?? 0d,
            AnnotationsPosition = map.AnnotationsPosition is { } position
                ? new WardleyPositionPayload { Visibility = position.Visibility, Maturity = position.Maturity }
                : null,
        };

        return Element(WardleyElementTypes.DiagramId, 0d, 0d, WardleyElementTypes.Diagram, payload);
    }

    /// <summary>The address a `url(name)` reference resolves to, or empty when nothing defines it.</summary>
    private static string AddressOf(WardleyMap map, string urlName) =>
        urlName.Length == 0
            ? ""
            : map.Urls.FirstOrDefault(url => url.Name == urlName)?.Address ?? "";

    private static DiagramElement Element(string id, double x, double y, string type, IMessage payload) =>
        new(id, x, y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
