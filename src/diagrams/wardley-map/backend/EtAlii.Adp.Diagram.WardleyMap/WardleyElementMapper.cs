using EtAlii.Adp.Backend.Diagrams;
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

        var ids = Index(identities);
        var elements = new List<DiagramElement> { EvolutionAxisElement(map) };

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
                elements.Add(PipelineChildElement(pipeline, child, parent, ids));
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
    /// The difference between two renderings of a map, as adds and removes. An edit is an add
    /// carrying the element in its new state, which is what makes the contract's four actions
    /// enough (Requirement 10.3).
    /// </summary>
    public IReadOnlyList<DiagramDelta> Diff(
        IReadOnlyList<DiagramElement> before,
        IReadOnlyList<DiagramElement> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var previous = before.ToDictionary(element => element.Id, StringComparer.Ordinal);
        var deltas = new List<DiagramDelta>();

        var changed = after
            .Where(element => !previous.TryGetValue(element.Id, out var was) || !Same(was, element))
            .ToArray();

        if (changed.Length > 0)
        {
            deltas.Add(new DiagramAddDelta(changed));
        }

        var current = after.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        var gone = before.Select(element => element.Id).Where(id => !current.Contains(id)).ToArray();
        if (gone.Length > 0)
        {
            deltas.Add(new DiagramRemoveDelta(gone));
        }

        return deltas;
    }

    /// <summary>
    /// A pipeline as the contract's grouping: the parent stands for the children it holds
    /// (Requirement 10.4).
    /// </summary>
    public IReadOnlyList<DiagramDelta> Group(WardleyMap map, IReadOnlyList<WardleyIdentityEntry> identities)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(identities);

        var ids = Index(identities);
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
                .Select(child => Id(ids, WardleyIdentityKind.PipelineChild, WardleyIdentityKeys.Of(pipeline, child)))
                .Where(id => id.Length > 0)
                .ToArray();

            if (children.Length > 0)
            {
                deltas.Add(new DiagramGroupDelta(children, ComponentElement(map, parent, ids)));
            }
        }

        return deltas;
    }

    private static bool Same(DiagramElement left, DiagramElement right) =>
        left.X.Equals(right.X)
        && left.Y.Equals(right.Y)
        && left.Type == right.Type
        && left.Payload.Span.SequenceEqual(right.Payload.Span);

    private static Dictionary<(string Kind, string Key), string> Index(IReadOnlyList<WardleyIdentityEntry> identities)
    {
        var ids = new Dictionary<(string, string), string>();
        foreach (var entry in identities)
        {
            ids.TryAdd((entry.Kind, entry.Key), entry.Id);
        }

        return ids;
    }

    private static string Id(Dictionary<(string Kind, string Key), string> ids, string kind, string key) =>
        ids.TryGetValue((kind, key), out var id) ? id : "";

    private DiagramElement ComponentElement(
        WardleyMap map,
        WardleyComponent component,
        Dictionary<(string Kind, string Key), string> ids)
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

        if (map.Evolves.FirstOrDefault(evolve => evolve.Name == component.Name) is { } evolving)
        {
            payload.Evolve = new WardleyEvolvePayload
            {
                Maturity = evolving.Maturity,
                EvolutionStage = WardleyEvolution.StageOf(evolving.Maturity).Label,
                OverrideName = evolving.Override,
            };
        }

        var (x, y) = WardleyAxis.ToPoint(component.Position);
        return Element(
            Id(ids, WardleyIdentityKind.Component, WardleyIdentityKeys.Of(component)),
            x,
            y,
            WardleyElementTypes.Element,
            payload);
    }

    private static DiagramElement PipelineChildElement(
        WardleyPipeline pipeline,
        WardleyPipelineChild child,
        WardleyComponent? parent,
        Dictionary<(string Kind, string Key), string> ids)
    {
        // A child takes its visibility from its parent - the format gives it none of its own,
        // which is why only its maturity may be dragged (Requirement 7.4).
        var visibility = parent?.Position.Visibility ?? 0d;
        var position = new WardleyCoordinate(visibility, child.Maturity);
        var parentId = parent is null
            ? ""
            : Id(ids, WardleyIdentityKind.Component, WardleyIdentityKeys.Of(parent));

        var payload = new WardleyElementPayload
        {
            Name = child.Name,
            Kind = WardleyElementKind.Component,
            Visibility = visibility,
            Maturity = child.Maturity,
            EvolutionStage = WardleyEvolution.StageOf(child.Maturity).Label,
            PipelineParentId = parentId,
        };

        var (x, y) = WardleyAxis.ToPoint(position);
        return Element(
            Id(ids, WardleyIdentityKind.PipelineChild, WardleyIdentityKeys.Of(pipeline, child)),
            x,
            y,
            WardleyElementTypes.Element,
            payload);
    }

    private static DiagramElement LinkElement(WardleyLink link, Dictionary<(string Kind, string Key), string> ids)
    {
        var payload = new WardleyLinkPayload
        {
            SourceId = Id(ids, WardleyIdentityKind.Component, link.Source),
            TargetId = Id(ids, WardleyIdentityKind.Component, link.Target),
            IsFlow = link.Kind == WardleyLinkKind.Flow,
            Context = link.Context,
            // Carried even when the ids resolve, so a canvas or a problem can name the endpoint
            // the author wrote rather than an id nobody recognises.
            SourceName = link.Source,
            TargetName = link.Target,
        };

        // A link has no position of its own: it is drawn between its endpoints.
        return Element(Id(ids, WardleyIdentityKind.Link, WardleyIdentityKeys.Of(link)), 0d, 0d, WardleyElementTypes.Link, payload);
    }

    private static DiagramElement NoteElement(WardleyNote note, Dictionary<(string Kind, string Key), string> ids)
    {
        var (x, y) = WardleyAxis.ToPoint(note.Position);
        return Element(
            Id(ids, WardleyIdentityKind.Note, WardleyIdentityKeys.Of(note)),
            x,
            y,
            WardleyElementTypes.Note,
            new WardleyNotePayload { Text = note.Text });
    }

    private static DiagramElement AnnotationElement(
        WardleyAnnotation annotation,
        Dictionary<(string Kind, string Key), string> ids)
    {
        var payload = new WardleyAnnotationPayload { Number = annotation.Number, Text = annotation.Text };
        foreach (var occurrence in annotation.Occurrences)
        {
            var (ox, oy) = WardleyAxis.ToPoint(occurrence);
            payload.Occurrences.Add(new WardleyPointPayload { X = ox, Y = oy });
        }

        // The element sits at the first occurrence; the rest travel in the payload, because a
        // core Element has one position and Requirement 6.8 says none may be lost.
        var (x, y) = WardleyAxis.ToPoint(annotation.Occurrences[0]);
        return Element(
            Id(ids, WardleyIdentityKind.Annotation, WardleyIdentityKeys.Of(annotation)),
            x,
            y,
            WardleyElementTypes.Annotation,
            payload);
    }

    private static DiagramElement AcceleratorElement(
        WardleyAccelerator accelerator,
        Dictionary<(string Kind, string Key), string> ids)
    {
        var (x, y) = WardleyAxis.ToPoint(accelerator.Position);
        return Element(
            Id(ids, WardleyIdentityKind.Accelerator, WardleyIdentityKeys.Of(accelerator)),
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
        Dictionary<(string Kind, string Key), string> ids)
    {
        var (x, y) = WardleyAxis.ToPoint(attitude.From);
        var (x2, y2) = WardleyAxis.ToPoint(attitude.To);

        return Element(
            Id(ids, WardleyIdentityKind.Attitude, WardleyIdentityKeys.Of(attitude)),
            x,
            y,
            WardleyElementTypes.Attitude,
            new WardleyAttitudePayload
            {
                Kind = attitude.Kind,
                Opposite = new WardleyPointPayload { X = x2, Y = y2 },
            });
    }

    private static DiagramElement EvolutionAxisElement(WardleyMap map)
    {
        var payload = new WardleyEvolutionAxisPayload
        {
            Title = map.Title,
            Width = map.Size?.Width ?? 0d,
            Height = map.Size?.Height ?? 0d,
        };

        foreach (var stage in WardleyEvolution.Stages)
        {
            payload.Stages.Add(new WardleyEvolutionStagePayload
            {
                Label = stage.Label,
                Start = stage.Start,
                End = stage.End,
            });
        }

        return Element(WardleyElementTypes.EvolutionAxisId, 0d, 0d, WardleyElementTypes.EvolutionAxis, payload);
    }

    /// <summary>The address a `url(name)` reference resolves to, or empty when nothing defines it.</summary>
    private static string AddressOf(WardleyMap map, string urlName) =>
        urlName.Length == 0
            ? ""
            : map.Urls.FirstOrDefault(url => url.Name == urlName)?.Address ?? "";

    private static DiagramElement Element(string id, double x, double y, string type, IMessage payload) =>
        new(id, x, y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
