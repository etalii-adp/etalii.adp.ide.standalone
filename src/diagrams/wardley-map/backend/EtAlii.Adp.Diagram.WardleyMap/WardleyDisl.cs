using System.Runtime.CompilerServices;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The DISL model (DISL §4) of a Wardley map, built from what <see cref="WardleyParser"/> read and the identities the
/// store keeps beside it. It stands in for gap 1 of section C of wardley-map-disl-fbl: FBL cannot yet state a pipeline
/// whose brace is on the line below it, so the body is not read through a binding. It is deleted in task 11, when the
/// model is the binding's reading.
/// </summary>
/// <remarks>
/// <para>
/// <b>One element per statement, in the order <see cref="WardleyIdentities.Reconcile"/> walks them</b>: the components,
/// the links, each pipeline followed by its components, the notes, the annotations, the accelerators and the attitudes,
/// each under the id the store gives it; then the evolutions and the <c>url</c> definitions, which have no identity, under
/// ids no identity can equal (<c>wardley:evolution:N</c>, <c>wardley:url:N</c>).
/// </para>
/// <para>
/// <b>A statement naming an element by its name names the first holder of that name</b> (ruling 3): a link's ends, an
/// evolution's and a pipeline's <c>component</c> are that holder; one that names no element has no end, and the name
/// as written is kept for <c>writtenEnd</c> (<see cref="WardleyFunctions"/>).
/// </para>
/// </remarks>
internal static class WardleyDisl
{
    /// <summary>
    /// The host attribute holding a link as the menus choose it: its source, target and kind as written, joined by
    /// <see cref="WardleyIdentityKeys.Separator"/>, which every repeat of one link shares.
    /// </summary>
    public const string ChoiceKey = "choiceKey";

    private static readonly ConditionalWeakTable<WardleyMap, Built> _built = new();

    /// <summary>
    /// The model of <paramref name="map"/>, read from <paramref name="body"/>, with <paramref name="identities"/>, built
    /// once for them; read it, never change it.
    /// </summary>
    public static DislDiagram Of(LineDocument body, WardleyMap map, IReadOnlyList<WardleyIdentityEntry> identities)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(identities);
        if (_built.TryGetValue(map, out var built) && ReferenceEquals(built.Identities, identities)) return built.Diagram;

        var diagram = Build(body, map, identities);
        _built.AddOrUpdate(map, new Built(identities, diagram));
        return diagram;
    }

    /// <summary>
    /// A model of <paramref name="map"/>, read from <paramref name="body"/>, with <paramref name="identities"/> of its own,
    /// for an operation to change as it runs.
    /// </summary>
    public static DislDiagram Build(LineDocument body, WardleyMap map, IReadOnlyList<WardleyIdentityEntry> identities)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(identities);

        var ids = new Ids(identities);
        var read = new List<FblElement>();
        var holders = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string key, WardleyComponent component) in WardleyIdentityKeys.Holders(map.Components, WardleyIdentityKeys.Of))
        {
            var id = ids.Of(WardleyIdentityKind.Component, key);
            holders.TryAdd(component.Name, id);
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = component.Name,
                ["visibility"] = component.Position.Visibility,
                ["maturity"] = component.Position.Maturity,
                ["inertia"] = component.Inertia,
                ["url"] = component.Url,
                [WardleyFunctions.WrittenDecorators] = WardleyDecorators.Join(component.Decorators),
            };
            foreach (var decorator in WardleyDecorators.All)
            {
                attributes[WardleyDecorators.Spell(decorator)] = component.Decorators.Contains(decorator);
            }

            if (component.LabelOffset is { } offset)
            {
                attributes["labelOffset"] = new CelMap { ["dx"] = offset.X, ["dy"] = offset.Y };
            }

            read.Add(Node(id, TypeOf(component.Kind), attributes, component.Line));
        }

        foreach ((string key, WardleyLink link) in WardleyIdentityKeys.Holders(map.Links, WardleyIdentityKeys.Of))
        {
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["context"] = link.Context,
                [WardleyFunctions.WrittenSource] = link.Source,
                [WardleyFunctions.WrittenTarget] = link.Target,
                [ChoiceKey] = $"{link.Source}{WardleyIdentityKeys.Separator}{link.Target}{WardleyIdentityKeys.Separator}{link.Kind}",
            };
            read.Add(new FblElement(
                ids.Of(WardleyIdentityKind.Link, key), true, link.Kind == WardleyLinkKind.Flow ? "Flow" : "Dependency", "link", true, attributes,
                null, null, holders.GetValueOrDefault(link.Source), holders.GetValueOrDefault(link.Target), default, (int)link.Line));
        }

        var children = WardleyIdentityKeys.Holders(map.Pipelines.SelectMany(pipeline => pipeline.Children.Select(child => (pipeline, child))), item => WardleyIdentityKeys.Of(item.pipeline, item.child))
            .Select(held => held.Key)
            .ToArray();
        var next = 0;
        foreach ((string key, WardleyPipeline pipeline) in WardleyIdentityKeys.Holders(map.Pipelines, WardleyIdentityKeys.Of))
        {
            var id = ids.Of(WardleyIdentityKind.Pipeline, key);
            var attributes = Referring(holders, pipeline.Parent);
            attributes["form"] = pipeline.Form == WardleyPipelineForm.Legacy ? "legacy" : "nested";
            // Closed as the edit that adds a component to it finds its brace: a line that is only `}` below it.
            attributes["closed"] = pipeline.Form == WardleyPipelineForm.Legacy || SetWardleyPipelineMembershipCommandHandler.ClosingBraceOf(body, pipeline) is not null;
            if (pipeline.LegacyExtent is { } extent)
            {
                attributes["legacyExtent"] = new CelMap { ["visibility"] = extent.Visibility, ["maturity"] = extent.Maturity };
            }

            read.Add(Node(id, "Pipeline", attributes, pipeline.Line));
            foreach (var child in pipeline.Children)
            {
                var childAttributes = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = child.Name, ["maturity"] = child.Maturity };
                read.Add(Node(ids.Of(WardleyIdentityKind.PipelineChild, children[next++]), "PipelineComponent", childAttributes, child.Line) with { ParentId = id });
            }
        }

        foreach ((string key, WardleyNote note) in WardleyIdentityKeys.Holders(map.Notes, WardleyIdentityKeys.Of))
        {
            read.Add(Node(ids.Of(WardleyIdentityKind.Note, key), "Note", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["text"] = note.Text,
                ["visibility"] = note.Position.Visibility,
                ["maturity"] = note.Position.Maturity,
            }, note.Line));
        }

        foreach ((string key, WardleyAnnotation annotation) in WardleyIdentityKeys.Holders(map.Annotations, WardleyIdentityKeys.Of))
        {
            read.Add(Node(ids.Of(WardleyIdentityKind.Annotation, key), "Annotation", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["number"] = (long)annotation.Number,
                ["positions"] = WardleyFunctions.PositionsText(annotation.Occurrences),
                ["text"] = annotation.Text,
            }, annotation.Line));
        }

        foreach ((string key, WardleyAccelerator accelerator) in WardleyIdentityKeys.Holders(map.Accelerators, WardleyIdentityKeys.Of))
        {
            read.Add(Node(ids.Of(WardleyIdentityKind.Accelerator, key), "Accelerator", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = accelerator.Name,
                ["visibility"] = accelerator.Position.Visibility,
                ["maturity"] = accelerator.Position.Maturity,
                ["deaccelerator"] = accelerator.IsDeaccelerator,
            }, accelerator.Line));
        }

        foreach ((string key, WardleyAttitude attitude) in WardleyIdentityKeys.Holders(map.Attitudes, WardleyIdentityKeys.Of))
        {
            read.Add(Node(ids.Of(WardleyIdentityKind.Attitude, key), "Attitude", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["attitude"] = attitude.Kind switch
                {
                    WardleyAttitudeKind.Pioneers => "pioneers",
                    WardleyAttitudeKind.Settlers => "settlers",
                    _ => "townplanners",
                },
                ["visibility1"] = attitude.From.Visibility,
                ["maturity1"] = attitude.From.Maturity,
                ["visibility2"] = attitude.To.Visibility,
                ["maturity2"] = attitude.To.Maturity,
            }, attitude.Line));
        }

        var evolution = 0;
        foreach (var evolve in map.Evolves)
        {
            var attributes = Referring(holders, evolve.Name);
            attributes["maturity"] = evolve.Maturity;
            attributes["renamedTo"] = evolve.Override;
            read.Add(Node($"wardley:evolution:{++evolution}", "Evolution", attributes, evolve.Line) with { IdIsStored = false });
        }

        var url = 0;
        foreach (var definition in map.Urls)
        {
            read.Add(Node($"wardley:url:{++url}", "Url", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = definition.Name,
                ["address"] = definition.Address,
            }, definition.Line) with { IdIsStored = false });
        }

        return DislModelBuilder.From(new FblModel(read, [], false), WardleyDefinition.Specification, new DislEnv(), "").Diagram;
    }

    /// <summary>The id a menu's choice gives <paramref name="option"/>: a link's <see cref="ChoiceKey"/>, else its model id; its text for anything but an element.</summary>
    public static string ChoiceIdOf(object? option) => option switch
    {
        DislElement element => element.HostAttributes.TryGetValue(ChoiceKey, out var key) && key is string text ? text : element.Id,
        _ => Convert.ToString(option, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>The element written with the wire id <paramref name="id"/>: the identity the store gives it; null for none.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string? id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return id is { Length: > 0 } ? diagram.ElementById(id) : null;
    }

    /// <summary>The attributes of a statement that names an element by <paramref name="name"/>: its <c>component</c>, the first holder of it, when there is one, and the name as written.</summary>
    private static Dictionary<string, object?> Referring(Dictionary<string, string> holders, string name)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { [WardleyFunctions.WrittenComponent] = name };
        if (holders.TryGetValue(name, out var id)) attributes["component"] = id;
        return attributes;
    }

    private static FblElement Node(string id, string type, Dictionary<string, object?> attributes, uint line) =>
        new(id, true, type, type, false, attributes, null, null, null, null, default, (int)line);

    private static string TypeOf(WardleyElementKind kind) => kind switch
    {
        WardleyElementKind.Anchor => "Anchor",
        WardleyElementKind.Submap => "Submap",
        _ => "Component",
    };

    /// <summary>The ids the store gives a map's elements, by kind and key; an element it gives none gets one no identity can equal.</summary>
    private sealed class Ids(IReadOnlyList<WardleyIdentityEntry> identities)
    {
        private readonly Dictionary<(string Kind, string Key), string> _ids = identities
            .GroupBy(entry => (entry.Kind, entry.Key))
            .ToDictionary(group => group.Key, group => group.First().Id);

        private int _unidentified;

        public string Of(string kind, string key) =>
            _ids.TryGetValue((kind, key), out var id) ? id : $"wardley:unidentified:{++_unidentified}";
    }

    private sealed record Built(IReadOnlyList<WardleyIdentityEntry> Identities, DislDiagram Diagram);
}
