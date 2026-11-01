using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// One <c>.dsl</c> body read through FBL: the module's binding (<c>structurizr.fbl</c> with <c>structurizr.lark</c>,
/// embedded) applied by <c>EtAlii.Adp.Specification.Fbl</c>, in the shape of the hype cycle's body class
/// (c4-disl-fbl task 14, Requirements 4.2 to 4.7).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in this module reads the DSL any more.</b> The body's workspace (<see cref="Workspace"/>) is what the
/// binding reads, and the DISL model each view is drawn from is built by <see cref="DislModelBuilder"/> from that
/// reading through the definition's <c>typeMap</c> (<see cref="ModelOf"/>). A view's own statements, its
/// <c>include</c>, <c>exclude</c>, <c>title</c>, <c>autoLayout</c>, scope, environment and interactions, are the
/// binding's <c>selectedView</c> rules, read for that view alone.
/// </para>
/// <para>
/// <b>An element written without an identifier is named by the host</b>, as the definitions' <c>persistence.ids</c>
/// say (<c>missing: ephemeral</c>, finding A8): <c>&lt;kind&gt;_&lt;name&gt;_&lt;n&gt;</c>, counted in document order,
/// and stored nowhere.
/// </para>
/// <para>
/// <b>One text is read once.</b> Reading is a pure function of the bytes, so the bodies of the texts read lately are
/// kept, and every reading of one text, by any store, command or validator, is the same object.
/// </para>
/// </remarks>
internal sealed class C4Body
{
    /// <summary>The view header no view of a body has: the reading of a target with no view selects none (FBL 0.11 §9.3).</summary>
    private const string NoView = "\u0000";

    /// <summary>How many recently read texts are kept.</summary>
    private const int Kept = 64;

    private static readonly Lazy<FblBinding> _binding = new(() => BundledBinding.Load(typeof(C4Body).Assembly, "structurizr.fbl", "workspace"));

    private static readonly ConcurrentDictionary<string, C4Body> _recent = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<string> _order = new();

    private static readonly ConditionalWeakTable<C4Workspace, C4Body> _bodies = new();

    private const string WrittenEnd = "writtenEnd";

    /// <summary>The attribute of an interaction that holds its number.</summary>
    private const string Order = "order";

    /// <summary>The diagram attribute that holds the key of the view a reading shows.</summary>
    private const string ViewKey = "viewKey";

    /// <summary>The suffix of the block rule that reads a view written without a key.</summary>
    private const string WithoutKey = "WithoutKey";

    /// <summary>The element kinds of the binding, by the type its rules read them as.</summary>
    private static readonly Dictionary<string, C4ElementKind> Kinds = Enum.GetValues<C4ElementKind>().ToDictionary(kind => kind.ToString(), StringComparer.Ordinal);

    private readonly byte[] _bytes;
    private readonly ConcurrentDictionary<string, FblModel> _readings = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<DislModel>> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<C4View, string> _keys = new(ReferenceEqualityComparer.Instance);
    private readonly IReadOnlyDictionary<string, string> _names;

    private C4Body(string text)
    {
        _bytes = Encoding.UTF8.GetBytes(text);
        var reading = Reading(NoView);
        _names = HostNames(reading);
        Workspace = Project(reading);
        _bodies.AddOrUpdate(Workspace, this);
    }

    /// <summary>The binding every <c>.dsl</c> body is read with.</summary>
    public static FblBinding Binding => _binding.Value;

    /// <summary>What the binding reads from the body: its model, its views and the files it does not follow.</summary>
    public C4Workspace Workspace { get; }

    /// <summary>The body of <paramref name="text"/>. Never throws on content: what the grammar does not read is kept, unread.</summary>
    public static C4Body Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_recent.TryGetValue(text, out var known)) return known;
        var body = new C4Body(text);
        if (_recent.TryAdd(text, body))
        {
            _order.Enqueue(text);
            while (_order.Count > Kept && _order.TryDequeue(out var oldest)) _recent.TryRemove(oldest, out _);
        }

        return body;
    }

    /// <summary>The body <paramref name="workspace"/> was read from.</summary>
    /// <exception cref="InvalidOperationException">The workspace was not read by this class, as <see cref="C4Workspace.Empty"/> was not.</exception>
    public static C4Body Of(C4Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (_bodies.TryGetValue(workspace, out var body)) return body;
        if (ReferenceEquals(workspace, C4Workspace.Empty)) return Read("");
        throw new InvalidOperationException("The workspace was not read through the binding (C4Body).");
    }

    /// <summary>The implementations of the definitions' plugin functions: <c>writtenEnd</c>.</summary>
    public static DislPluginFunctions Plugins() => new DislPluginFunctions()
        .Add(WrittenEnd, arguments => WrittenEndOf(arguments[0], arguments[1] as string));

    /// <summary>The file name a body's elements are located in (§12.2): the body's own, without its folder.</summary>
    public static string FileOf(string bodyPath) => Path.GetFileName(bodyPath);

    /// <summary>
    /// The model of <paramref name="workspace"/>, read from the body named <paramref name="file"/>, for <paramref name="view"/>
    /// under <paramref name="specification"/>, built once per body and kept while it is; read it, never change it.
    /// </summary>
    public static DislModel Of(C4Workspace workspace, C4View? view, DislSpecification specification, string file)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(file);
        var body = Of(workspace);
        var key = $"{specification.Origin}|{(view is null ? "" : body.KeyOf(view))}|{view?.Line}|{file}";
        return body._models.GetOrAdd(key, _ => new Lazy<DislModel>(() => body.Build(view, specification, file, null))).Value;
    }

    /// <summary>
    /// A model of <paramref name="workspace"/> for <paramref name="view"/> of its own, for an operation to change as it runs.
    /// Its elements are located in <paramref name="file"/>, so <c>location()</c> gives the line each was read at (§12.2).
    /// </summary>
    public static DislModel Build(C4Workspace workspace, C4View? view, DislSpecification specification, string file)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(file);
        return Of(workspace).Build(view, specification, file, null);
    }

    /// <summary>
    /// The model of <paramref name="workspace"/> that its findings are evaluated over, under <paramref name="specification"/>:
    /// no view shown, and every view of the workspace with the members the model of that view shows, under the definition
    /// of its kind (<paramref name="definitionOf"/>); built once per body and kept while it is; read it, never change it.
    /// </summary>
    /// <remarks>
    /// A rule about the model asks every view what it shows (<c>diagram.views</c>), and one definition declares the
    /// viewpoint of its own kind only, while a view's members depend on its own statements and, for a dynamic view, on its
    /// own interactions, which only the model that shows it holds. So each view's members are computed where the view is
    /// shown, and handed to this model as the host's (§12.2).
    /// </remarks>
    public static DislModel OfTheWholeModel(C4Workspace workspace, DislSpecification specification, string file, Func<C4ViewKind, DislSpecification> definitionOf)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(definitionOf);
        var body = Of(workspace);
        var key = $"{specification.Origin}|(whole model)|{file}";
        return body._models.GetOrAdd(key, _ => new Lazy<DislModel>(() => body.Build(null, specification, file, each => MembersOf(workspace, each, file, definitionOf(each.Kind))))).Value;
    }

    /// <summary>What <paramref name="view"/> shows in the model that shows it, under <paramref name="specification"/>, the definition of its kind.</summary>
    private static (DislDiagram Diagram, IReadOnlyList<DislElement> Members) MembersOf(C4Workspace workspace, C4View view, string file, DislSpecification specification)
    {
        var shown = Of(workspace, view, specification, file).Diagram;
        var own = shown.Views.FirstOrDefault(each => specification.IdComparer.Equals(each.Id, view.Key));
        return (shown, own?.Members ?? []);
    }

    /// <summary>
    /// The model of <paramref name="view"/>: the binding's reading for that view, its elements named as the host names them,
    /// built by <see cref="DislModelBuilder"/> through the definition's <c>typeMap</c>, the view's key set as the reading
    /// shows it, and every view of the workspace added, the one shown first.
    /// </summary>
    private DislModel Build(C4View? view, DislSpecification specification, string file, Func<C4View, (DislDiagram Diagram, IReadOnlyList<DislElement> Members)>? membersOf)
    {
        var reading = Counted(Named(Reading(view is null ? NoView : KeyOf(view))));
        if (view is not null)
        {
            // The key of the view this reading shows is the registration's, which the host knows and the body does not
            // say: a diagram attribute of the host's (DISL §11.2, hostAttributes), beside the workspace's.
            var key = new Dictionary<string, object?>(StringComparer.Ordinal) { [ViewKey] = view.Key };
            reading = reading with { Elements = [new FblElement(ViewKey, false, "Workspace", ViewKey, false, key, null, null, null, null, default, 0), .. reading.Elements] };
        }

        var viewpoint = view is not null ? ViewpointOf(view.Kind) : DefaultViewpoint(specification);
        var model = DislModelBuilder.From(reading, specification, new DislEnv(Viewpoint: viewpoint), file);

        // The shown view comes first, so that its key finds it also when an earlier view has the same key; a model of no
        // view lists them as the workspace does. A second view of one key is read, and the first is the one a key finds.
        IEnumerable<C4View> views = view is null ? Workspace.Views : [view, .. Workspace.Views.Where(each => !ReferenceEquals(each, view))];
        foreach (var each in views)
        {
            if (model.Diagram.Views.Any(added => specification.IdComparer.Equals(added.Id, each.Key))) continue;
            model.Diagram.AddView(each.Key, ViewpointOf(each.Kind), each.Title ?? "", ViewAttributes(each), membersOf is null ? null : Mapped(membersOf(each), model.Diagram));
        }

        return model;
    }

    /// <summary><paramref name="other"/>'s members as the elements of <paramref name="diagram"/> they stand for, matched by place and id.</summary>
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
    }

    private static IEnumerable<DislElement> StoredRelationships(DislDiagram of) => of.Relations.Where(relation => !relation.IsDerived && relation.IsA("Relationship"));

    /// <summary>The view <paramref name="view"/> is in <paramref name="diagram"/>: the first of its key.</summary>
    public static DislView ShownView(DislDiagram diagram, C4View view)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(view);
        return diagram.Views.First(each => diagram.Specification.IdComparer.Equals(each.Id, view.Key));
    }

    /// <summary>The workspace's element of each node of <paramref name="diagram"/>, which holds them in the workspace's order.</summary>
    public static IReadOnlyDictionary<DislElement, C4Element> ElementsOf(DislDiagram diagram, C4Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(workspace);
        var elements = new Dictionary<DislElement, C4Element>(ReferenceEqualityComparer.Instance);
        foreach ((DislElement node, C4Element element) in diagram.Nodes.Where(node => !node.IsA("IncludedFile")).Zip(workspace.Elements))
        {
            if (!string.Equals(node.Id, element.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The model's node '{node.Id}' is not the workspace's element '{element.Id}' in its place.");
            }

            elements[node] = element;
        }

        return elements;
    }

    /// <summary>The node or stored relationship <paramref name="wireId"/> names in <paramref name="diagram"/>; null for the empty id and for an id that names neither.</summary>
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

    /// <summary>The text <paramref name="relation"/>'s <paramref name="end"/> is written with, also when it names no element.</summary>
    public static string WrittenEndOf(object? relation, string? end) => relation is DislElement { Type.IsRelation: true } element && end is "source" or "target"
        ? (end == "source" ? element.SourceId ?? element.Source?.Id : element.TargetId ?? element.Target?.Id) ?? ""
        : throw new CelException("writtenEnd() takes a relationship or an interaction of this model and 'source' or 'target'.");

    /// <summary>The viewpoint of the definitions that a view of <paramref name="kind"/> is drawn in.</summary>
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

    /// <summary>The kind of view the binding's block rule <paramref name="block"/> reads, with its key or without one; null for none.</summary>
    private static C4ViewKind? KindOfBlock(string block) => block.EndsWith(WithoutKey, StringComparison.Ordinal) ? KindOfBlock(block[..^WithoutKey.Length]) : block switch
    {
        "systemLandscapeView" => C4ViewKind.SystemLandscape,
        "systemContextView" => C4ViewKind.SystemContext,
        "containerView" => C4ViewKind.Container,
        "componentView" => C4ViewKind.Component,
        "dynamicView" => C4ViewKind.Dynamic,
        "deploymentView" => C4ViewKind.Deployment,
        _ => null,
    };

    /// <summary>A view's own statements as the attributes of a view of the model (DISL §12.2), which a rule about the model reads.</summary>
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
            [ViewKey] = view.Key,
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
        specification.Json.TryGetProperty("viewpoints", out var viewpoints) && viewpoints.ValueKind == System.Text.Json.JsonValueKind.Object
            ? viewpoints.EnumerateObject().Select(viewpoint => viewpoint.Name).FirstOrDefault() ?? ""
            : "";

    /// <summary>The view header that selects <paramref name="view"/>: its key, or the name the binding gives a view written without one.</summary>
    private string KeyOf(C4View view)
    {
        lock (_keys) return _keys.TryGetValue(view, out var key) ? key : view.Key;
    }

    /// <summary>The binding's reading of the body with the view <paramref name="header"/> names selected.</summary>
    private FblModel Reading(string header) =>
        _readings.GetOrAdd(header, selected => OpenBody.Open(_bytes, Binding, new FblOptions { FileName = "body.dsl", View = selected, Many = Many }).Model);

    /// <summary>The attributes the tool types hold as lists (DISL): an element's tags and a view's included and excluded members.</summary>
    private static bool Many(string type, string attribute) => attribute == "tags" || (type == "ViewInclude" && attribute is "members" or "excludes");

    /// <summary>
    /// The names the host gives the elements written without an identifier (<c>missing: ephemeral</c>, finding A8):
    /// <c>&lt;kind&gt;_&lt;name&gt;_&lt;n&gt;</c>, the name's letters and digits, <c>unnamed</c> for none, and the count in document order.
    /// </summary>
    private static Dictionary<string, string> HostNames(FblModel reading)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in reading.Elements.Where(element => !element.IdIsStored && Kinds.ContainsKey(element.Type)))
        {
            var slug = new string(NameOf(element).Where(char.IsLetterOrDigit).ToArray());
            names[element.Id] = $"{element.Type.ToLowerInvariant()}_{(slug.Length > 0 ? slug : "unnamed")}_{(names.Count + 1).ToString(CultureInfo.InvariantCulture)}";
        }

        return names;
    }

    /// <summary>
    /// What an element written without an identifier is named after, as <c>C4Parser</c> named it: its first argument,
    /// which is its name, or for an instance the element it is an instance of, as written; the keyword for none.
    /// </summary>
    private static string NameOf(FblElement element)
    {
        foreach (var attribute in (string[])["name", "container", "softwareSystem"])
        {
            if (element.Attributes.ContainsKey(attribute)) return Text(element, attribute);
        }

        return element.Rule;
    }

    /// <summary><paramref name="reading"/> with every element written without an identifier under the name the host gives it, and every parent so named.</summary>
    private FblModel Named(FblModel reading)
    {
        if (_names.Count == 0) return reading;
        return reading with
        {
            Elements = [.. reading.Elements.Select(element => element with { Id = _names.GetValueOrDefault(element.Id, element.Id), ParentId = element.ParentId is { } parent ? _names.GetValueOrDefault(parent, parent) : null })],
        };
    }

    /// <summary>
    /// <paramref name="reading"/> with each interaction of the view it shows numbered as the host counts it, from 1 in
    /// document order (the definitions' <c>Interaction.order</c>, "as written or counted"): the binding reads no number.
    /// </summary>
    private static FblModel Counted(FblModel reading)
    {
        var count = 0;
        return reading with
        {
            Elements = [.. reading.Elements.Select(element => element.Type != "Interaction" || element.Attributes.ContainsKey(Order)
                ? element
                : element with { Attributes = new Dictionary<string, object?>(element.Attributes, StringComparer.Ordinal) { [Order] = (++count).ToString(CultureInfo.InvariantCulture) } })],
        };
    }

    /// <summary>The workspace of the reading of no view, and each view's own statements from the reading of that view.</summary>
    private C4Workspace Project(FblModel reading)
    {
        var named = Named(reading);
        var elements = named.Elements.Where(element => Kinds.ContainsKey(element.Type)).Select(Element).ToList();
        var relationships = named.Elements.Where(element => element.Type == "Relationship")
            .Select(element => new C4Relationship(Text(element, "from"), Text(element, "to"), Text(element, "description"), Text(element, "technology"), (uint)element.Line))
            .ToList();
        var views = new List<C4View>();
        foreach (var block in reading.Views)
        {
            if (KindOfBlock(block.Block) is not { } kind) continue;
            var view = ViewOf(block, kind, Reading(block.Name));
            lock (_keys) _keys[view] = block.Name;
            views.Add(view);
        }

        var name = Text(named.Elements.FirstOrDefault(element => element.Type == "Workspace"), "name");
        var includes = named.Elements.Where(element => element.Type == "IncludedFile").Select(element => Text(element, "path")).ToList();
        return new C4Workspace(name, elements, relationships, views, [], includes);
    }

    private static C4Element Element(FblElement element)
    {
        var kind = Kinds[element.Type];
        var referenced = kind switch
        {
            C4ElementKind.ContainerInstance => Text(element, "container"),
            C4ElementKind.SoftwareSystemInstance => Text(element, "softwareSystem"),
            _ => null,
        };
        return new C4Element(element.Id, kind, Text(element, "name"), Text(element, "description"), Text(element, "technology"), Texts(element, "tags"), element.ParentId, (uint)element.Line, referenced);
    }

    /// <summary>A view as the reading that selects it reads its own statements.</summary>
    private static C4View ViewOf(FblView block, C4ViewKind kind, FblModel selected)
    {
        var statements = selected.Elements.Where(element => element.Type == "ViewInclude").ToList();
        var members = statements.SelectMany(statement => Texts(statement, "members")).ToList();
        var excludes = statements.SelectMany(statement => Texts(statement, "excludes")).ToList();
        var layout = statements.FirstOrDefault(statement => statement.Attributes.ContainsKey("autoLayout"));
        var interactions = selected.Elements.Where(element => element.Type == "Interaction")
            .Select((element, index) => new C4Interaction(Text(element, "from"), Text(element, "to"), Text(element, "description"), (index + 1).ToString(CultureInfo.InvariantCulture), (uint)element.Line))
            .ToList();
        return new C4View(
            kind,
            block.Name,
            First(statements, "scope"),
            First(statements, "environment"),
            First(statements, "title"),
            members.Contains("*"),
            [.. members.Where(member => member != "*")],
            excludes,
            layout is null ? null : new C4AutoLayout(Text(layout, "autoLayout"), Number(layout, "rankSeparation"), Number(layout, "nodeSeparation")),
            interactions,
            (uint)block.Line);
    }

    /// <summary>The first value of <paramref name="attribute"/> among a view's statements; null when none states it.</summary>
    private static string? First(List<FblElement> statements, string attribute) =>
        statements.Where(statement => statement.Attributes.ContainsKey(attribute)).Select(statement => Text(statement, attribute)).FirstOrDefault();

    private static string Text(FblElement? element, string attribute) =>
        element?.Attributes.GetValueOrDefault(attribute) switch
        {
            string text => text,
            null => "",
            var other => Convert.ToString(other, CultureInfo.InvariantCulture) ?? "",
        };

    private static List<string> Texts(FblElement element, string attribute) =>
        element.Attributes.GetValueOrDefault(attribute) switch
        {
            IEnumerable<object?> items => [.. items.Select(item => Convert.ToString(item, CultureInfo.InvariantCulture) ?? "")],
            string text => [text],
            _ => [],
        };

    private static int? Number(FblElement element, string attribute) =>
        int.TryParse(Text(element, attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
}
