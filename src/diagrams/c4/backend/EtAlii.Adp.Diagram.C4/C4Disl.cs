using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// The DISL model of one parsed <see cref="C4Workspace"/> for one view, built from
/// <see cref="C4Parser"/>'s reading with hand-made <see cref="FblElement"/> records through
/// <see cref="DislModelBuilder.From(FblModel, DislSpecification, DislEnv, string?)"/>, under the definition of
/// that view's kind (c4-disl-fbl design, <i>C4Disl</i>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It stands in for gaps C1, C2 and C6</b> (c4-disl-fbl Q5): the module's binding cannot yet read
/// the selected view's statements, a comment after a brace or a statement, or an unbalanced body,
/// so the body is still read by the parser and this class turns that reading into the model the
/// definitions derive from. It is deleted by task 14, when the body is read through the binding.
/// </para>
/// <para>
/// <b>Ids.</b> A node's model id is the identifier the parser gives it, which is today's wire id; one
/// the declaration did not write (<c>&lt;kind&gt;_&lt;name&gt;_&lt;n&gt;</c>, finding A8) is marked not
/// stored. A stored relationship's id is computed by the definition's id rule from its ends as written
/// and its line, which is today's <c>&lt;source&gt;-&gt;&lt;destination&gt;@&lt;line&gt;</c> (finding A7).
/// </para>
/// <para>
/// <b>A relationship whose end names no element</b> is built with that end null and the identifier
/// it was written with kept as read (<see cref="DislElement.SourceId"/>, <see cref="DislElement.TargetId"/>),
/// which the definitions' <c>writtenEnd</c> plugin function answers from (<see cref="Plugins"/>).
/// </para>
/// <para>
/// <b>The shown view's statements</b> become the diagram's attributes, its interactions become
/// <c>Interaction</c> relations, and every view of the workspace is handed to the model as a view of
/// its own, with its own statements, so a model-wide rule can ask <c>diagram.views</c>.
/// </para>
/// </remarks>
internal static class C4Disl
{
    /// <summary>The definitions' plugin function: the text a relationship's or an interaction's end is written with.</summary>
    private const string WrittenEnd = "writtenEnd";

    /// <summary>The models already built, per parsed workspace and per definition and view.</summary>
    private static readonly ConditionalWeakTable<C4Workspace, ConcurrentDictionary<string, Lazy<DislModel>>> _built = new();

    /// <summary>The viewpoint each view kind is drawn in, as the definitions name it.</summary>
    private static string ViewpointOf(C4ViewKind kind) => kind switch
    {
        C4ViewKind.SystemLandscape => "systemLandscape",
        C4ViewKind.SystemContext => "systemContext",
        C4ViewKind.Container => "container",
        C4ViewKind.Component => "component",
        C4ViewKind.Dynamic => "dynamic",
        C4ViewKind.Deployment => "deployment",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown C4 view kind."),
    };

    /// <summary>The file name a body's elements are located in: the body's own, as a finding names it.</summary>
    public static string FileOf(string bodyPath) => Path.GetFileName(bodyPath);

    /// <summary>The implementations of the definitions' plugin functions: <c>writtenEnd</c>.</summary>
    public static DislPluginFunctions Plugins() => new DislPluginFunctions()
        .Add(WrittenEnd, arguments => WrittenEndOf(arguments[0], arguments[1] as string));

    /// <summary>
    /// The model of <paramref name="workspace"/>, read from the body named <paramref name="file"/>, for
    /// <paramref name="view"/> under <paramref name="specification"/>, built once per parsed workspace and
    /// kept while it is; read it, never change it.
    /// </summary>
    public static DislModel Of(C4Workspace workspace, C4View? view, DislSpecification specification, string file)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(file);
        var key = $"{specification.Origin}|{view?.Key}|{view?.Line}|{file}";
        return _built.GetValue(workspace, _ => new ConcurrentDictionary<string, Lazy<DislModel>>(StringComparer.Ordinal))
            .GetOrAdd(key, _ => new Lazy<DislModel>(() => Build(workspace, view, specification, file)))
            .Value;
    }

    /// <summary>
    /// A model of <paramref name="workspace"/> for <paramref name="view"/> of its own, for an operation to
    /// change as it runs. Its elements are located in <paramref name="file"/>, the body's file name, so
    /// <c>location()</c> gives the line the parser read each at (§12.2).
    /// </summary>
    public static DislModel Build(C4Workspace workspace, C4View? view, DislSpecification specification, string file) =>
        Build(workspace, view, specification, file, null);

    /// <summary>
    /// The model of <paramref name="workspace"/> that its findings are evaluated over, under
    /// <paramref name="specification"/>: no view shown, and every view of the workspace with the members
    /// the model of that view shows, under the definition of its kind (<paramref name="definitionOf"/>);
    /// built once per parsed workspace and kept while it is; read it, never change it.
    /// </summary>
    /// <remarks>
    /// A rule about the model asks every view what it shows (<c>diagram.views</c>), and one definition
    /// declares the viewpoint of its own kind only, while a view's members depend on its own statements
    /// and, for a dynamic view, on its own interactions, which only the model that shows it holds. So each
    /// view's members are computed where the view is shown, and handed to this model as the host's (§12.2).
    /// </remarks>
    public static DislModel OfTheWholeModel(C4Workspace workspace, DislSpecification specification, string file, Func<C4ViewKind, DislSpecification> definitionOf)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(definitionOf);
        var key = $"{specification.Origin}|(whole model)|{file}";
        return _built.GetValue(workspace, _ => new ConcurrentDictionary<string, Lazy<DislModel>>(StringComparer.Ordinal))
            .GetOrAdd(key, _ => new Lazy<DislModel>(() => Build(workspace, null, specification, file, each => MembersOf(workspace, each, file, definitionOf(each.Kind)))))
            .Value;
    }

    /// <summary>What <paramref name="view"/> shows in the model that shows it, under <paramref name="specification"/>, the definition of its kind.</summary>
    private static (DislDiagram Diagram, IReadOnlyList<DislElement> Members) MembersOf(C4Workspace workspace, C4View view, string file, DislSpecification specification)
    {
        var shown = Of(workspace, view, specification, file).Diagram;
        var own = shown.Views.FirstOrDefault(each => specification.IdComparer.Equals(each.Id, view.Key));
        return (shown, own?.Members ?? []);
    }

    private static DislModel Build(C4Workspace workspace, C4View? view, DislSpecification specification, string file, Func<C4View, (DislDiagram Diagram, IReadOnlyList<DislElement> Members)>? membersOf)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(file);

        var elements = new List<FblElement> { DiagramAttributes(workspace, view) };
        elements.AddRange(workspace.Includes.Select((path, index) => Node($"!include:{index}", false, "IncludedFile", new Dictionary<string, object?> { ["path"] = path }, null, 0)));
        foreach (var element in workspace.Elements)
        {
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = element.Name,
                ["description"] = element.Description,
                ["technology"] = element.Technology,
                ["tags"] = element.Tags.ToList(),
            };
            if (element.ReferencedId is { } referenced)
            {
                attributes[element.Kind == C4ElementKind.ContainerInstance ? "container" : "softwareSystem"] = referenced;
            }

            elements.Add(Node(element.Id, !IsGenerated(element), element.Kind.ToString(), attributes, element.ParentId, (int)element.Line));
        }

        elements.AddRange(workspace.Relationships.Select(relationship => Relation(
            relationship.Id,
            "Relationship",
            relationship.SourceId,
            relationship.DestinationId,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["description"] = relationship.Description, ["technology"] = relationship.Technology },
            relationship.Line)));
        if (view is not null)
        {
            elements.AddRange(view.Interactions.Select(interaction => Relation(
                $"{interaction.SourceId}->{interaction.DestinationId}@{interaction.Line}",
                "Interaction",
                interaction.SourceId,
                interaction.DestinationId,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["description"] = interaction.Description, ["order"] = interaction.Order },
                interaction.Line)));
        }

        var viewpoint = view is not null ? ViewpointOf(view.Kind) : DefaultViewpoint(specification);
        var model = DislModelBuilder.From(new FblModel(elements, [], false), specification, new DislEnv(Viewpoint: viewpoint), file);
        foreach (var each in workspace.Views)
        {
            if (model.Diagram.Views.Any(added => specification.IdComparer.Equals(added.Id, each.Key)))
            {
                // A second view of one key is read, as today, and the first is the one a registration finds.
                continue;
            }

            model.Diagram.AddView(each.Key, ViewpointOf(each.Kind), each.Title ?? "", ViewAttributes(each), membersOf is null ? null : Mapped(membersOf(each), model.Diagram));
        }

        return model;
    }

    /// <summary>
    /// The members of a view in <paramref name="other"/>, another model of the same workspace, as the elements of
    /// <paramref name="diagram"/> they are: a node by its place among the nodes, a stored relationship by
    /// its place among the stored relationships, both read from one workspace in one order; what the
    /// other model derives or holds only for the view it shows has no counterpart here, and is left out.
    /// </summary>
    private static List<DislElement> Mapped((DislDiagram Diagram, IReadOnlyList<DislElement> Members) other, DislDiagram diagram)
    {
        var counterparts = new Dictionary<DislElement, DislElement>(ReferenceEqualityComparer.Instance);
        foreach ((DislElement theirs, DislElement ours) in other.Diagram.Nodes.Zip(diagram.Nodes).Concat(StoredRelationships(other.Diagram).Zip(StoredRelationships(diagram))))
        {
            if (string.Equals(theirs.Id, ours.Id, StringComparison.Ordinal))
            {
                counterparts[theirs] = ours;
            }
        }

        return [.. other.Members.Select(counterparts.GetValueOrDefault).OfType<DislElement>()];

        static IEnumerable<DislElement> StoredRelationships(DislDiagram of) => of.Relations.Where(relation => !relation.IsDerived && relation.IsA("Relationship"));
    }

    /// <summary>
    /// The element <paramref name="wireId"/> names in <paramref name="diagram"/>, as the hand-written
    /// providers found it: the first node of that identifier, compared without case, else the first
    /// stored relationship of exactly that id; null for none, for the empty id, and for a line the view
    /// derives (finding A10).
    /// </summary>
    public static DislElement? ElementOf(DislDiagram diagram, string wireId)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(wireId);
        if (wireId.Length == 0)
        {
            return null;
        }

        return diagram.Nodes.FirstOrDefault(node => !node.IsA("IncludedFile") && diagram.Specification.IdComparer.Equals(node.Id, wireId))
            ?? diagram.Relations.FirstOrDefault(relation => !relation.IsDerived && relation.IsA("Relationship") && string.Equals(relation.Id, wireId, StringComparison.Ordinal));
    }

    /// <summary>The diagram attributes: the workspace's name, and the shown view's own statements.</summary>
    private static FblElement DiagramAttributes(C4Workspace workspace, C4View? view)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = workspace.Name };
        if (view is not null)
        {
            foreach ((string name, object? value) in ViewAttributes(view))
            {
                attributes[name] = value;
            }
        }

        return Node("workspace", false, "Workspace", attributes, null, 0);
    }

    /// <summary>A view's own statements, as the diagram attributes the metamodel names for them.</summary>
    private static Dictionary<string, object?> ViewAttributes(C4View view)
    {
        var includes = new List<object?>();
        if (view.IncludesEverything)
        {
            includes.Add("*");
        }

        includes.AddRange(view.Includes);
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["viewKey"] = view.Key,
            ["title"] = view.Title ?? "",
            ["environment"] = view.Environment ?? "",
            ["includes"] = includes,
            ["excludes"] = view.Excludes.ToList<object?>(),
        };
        if (view.ScopeId is { } scope)
        {
            attributes["scope"] = scope;
        }

        if (view.AutoLayout is { } layout)
        {
            attributes["autoLayout"] = layout.Direction;
            if (layout.RankSeparation is { } rank)
            {
                attributes["rankSeparation"] = (long)rank;
            }

            if (layout.NodeSeparation is { } node)
            {
                attributes["nodeSeparation"] = (long)node;
            }
        }

        return attributes;
    }

    /// <summary>The viewpoint a definition declares first, for a model of no view.</summary>
    private static string DefaultViewpoint(DislSpecification specification) =>
        specification.Root.TryGetProperty("viewpoints", out var viewpoints) && viewpoints.ValueKind == System.Text.Json.JsonValueKind.Object
            ? viewpoints.EnumerateObject().Select(viewpoint => viewpoint.Name).FirstOrDefault() ?? ""
            : "";

    /// <summary>
    /// Whether the parser named <paramref name="element"/> itself because its declaration wrote no
    /// identifier: the <c>&lt;kind&gt;_&lt;name&gt;_&lt;n&gt;</c> it makes (finding A8).
    /// </summary>
    private static bool IsGenerated(C4Element element)
    {
        var prefix = element.Kind.ToString().ToLowerInvariant() + "_";
        if (!element.Id.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = element.Id[prefix.Length..];
        var last = rest.LastIndexOf('_');
        return last > 0
            && rest[..last].All(char.IsLetterOrDigit)
            && rest[(last + 1)..] is { Length: > 0 } number
            && number.All(char.IsAsciiDigit);
    }

    private static FblElement Node(string id, bool stored, string type, IReadOnlyDictionary<string, object?> attributes, string? parentId, int line) =>
        new(id, stored, type, type, false, attributes, parentId, parentId is null ? null : "children", null, null, default, line);

    private static FblElement Relation(string id, string type, string source, string target, IReadOnlyDictionary<string, object?> attributes, uint line) =>
        new(id, false, type, type, true, attributes, null, null, source, target, default, (int)line);

    /// <summary>The text <paramref name="relation"/>'s <paramref name="end"/> is written with, also when it names no element.</summary>
    private static string WrittenEndOf(object? relation, string? end) => relation is DislElement { Type.IsRelation: true } element && end is "source" or "target"
        ? (end == "source" ? element.SourceId ?? element.Source?.Id : element.TargetId ?? element.Target?.Id) ?? ""
        : throw new CelException("writtenEnd() takes a relationship or an interaction of this model and 'source' or 'target'.");
}
