using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Json;
using Serilog;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// The six C4 diagram types, each paired with the view kind it binds and with its DISL definition,
/// bundled from etalii-adp/etalii.adp into <c>definition/</c> (<c>c4-context.dis</c> and the five beside
/// it), loaded once as one <see cref="BundledDefinitionSet"/> and found by origin or by view kind.
/// </summary>
/// <remarks>
/// <para>
/// <b>The toolbox, the menus and the rows are derived from them</b> (c4-disl-fbl task 7), mapped onto
/// today's wire ids by each definition's <c>x-c4</c> block, and so are <b>the findings</b> (task 9),
/// ordered as today by <see cref="C4FindingOrder"/>, and so is <b>what a view draws</b> (task 10): its
/// members, its lines, its title and its legend (<see cref="Drawing"/>). The toolbox is the origin's. A menu
/// and a row are derived from the definition of the view's own kind, over the model <see cref="C4Disl"/>
/// builds from the parser's reading; a target with no view is derived from the origin's definition. A
/// line the view derives rather than a stored relationship, and an unknown id, get nothing (finding A10).
/// </para>
/// <para>
/// <b><c>c4/code</c> has no definition</b> (c4-disl-fbl Requirement 2.7): it keeps its registration and
/// its refusing empty document, and <see cref="Find(ToolOrigin)"/> answers null for it.
/// </para>
/// <para>
/// The definitions declare one plugin function, <c>writtenEnd</c>, which the module implements from its
/// reading (<see cref="C4Disl.Plugins"/>): the identifier an end is written with, also when it names no
/// element.
/// </para>
/// </remarks>
internal static class C4Definitions
{
    private static readonly ILogger _logger = Log.ForContext(typeof(C4Definitions));

    /// <summary>Each C4 diagram type that has a definition, with the view kind it binds and the resource its definition is embedded as.</summary>
    internal static IReadOnlyList<(ToolOrigin Origin, C4ViewKind ViewKind, string Resource)> Types { get; } =
    [
        (new ToolOrigin("c4", "system-landscape"), C4ViewKind.SystemLandscape, "c4-system-landscape.dis"),
        (new ToolOrigin("c4", "context"), C4ViewKind.SystemContext, "c4-context.dis"),
        (new ToolOrigin("c4", "container"), C4ViewKind.Container, "c4-container.dis"),
        (new ToolOrigin("c4", "component"), C4ViewKind.Component, "c4-component.dis"),
        (new ToolOrigin("c4", "dynamic"), C4ViewKind.Dynamic, "c4-dynamic.dis"),
        (new ToolOrigin("c4", "deployment"), C4ViewKind.Deployment, "c4-deployment.dis"),
    ];

    /// <summary>Loaded on first use, so registering the types loads nothing.</summary>
    private static readonly Lazy<BundledDefinitionSet> _set = new(() => BundledDefinitionSet.Load(
        typeof(C4Definitions).Assembly,
        C4Disl.Plugins(),
        Types.Select(type => type.Resource)));

    /// <summary>The six definitions, in the order of <see cref="Types"/>.</summary>
    /// <exception cref="InvalidOperationException">A definition is missing, is not the bytes its provenance names, or does not load.</exception>
    public static BundledDefinitionSet Set => _set.Value;

    /// <summary>The definition of the C4 type <paramref name="origin"/> names, or null for one that has none, as <c>c4/code</c> has none.</summary>
    public static BundledDefinition? Find(ToolOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return Set.Find(origin.Key);
    }

    /// <summary>The definition of the C4 type <paramref name="origin"/> names.</summary>
    /// <exception cref="KeyNotFoundException">The type has no definition, as <c>c4/code</c> has none.</exception>
    public static BundledDefinition For(ToolOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return Set.For(origin.Key);
    }

    /// <summary>The definition of the C4 type that binds views of <paramref name="viewKind"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No C4 type binds that kind.</exception>
    public static BundledDefinition For(C4ViewKind viewKind)
    {
        foreach ((ToolOrigin origin, C4ViewKind kind, string _) in Types)
        {
            if (kind == viewKind)
            {
                return For(origin);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(viewKind), viewKind, "No C4 diagram type binds this view kind.");
    }

    /// <summary>What the runtime had to say about the six as it loaded them, each with the file it is about.</summary>
    public static IReadOnlyList<(string Path, LoadDiagnostic Diagnostic)> Diagnostics =>
        [.. Set.Definitions.SelectMany(definition => definition.Diagnostics.Select(diagnostic => (definition.Provenance.Path, diagnostic)))];

    /// <summary>What <c>env</c> reads for a menu and a row: editable, in the viewpoint of the view the target is drawn on.</summary>
    private static DislEnv EnvOf(DislDiagram diagram) => new(Viewpoint: diagram.Env.Viewpoint);

    /// <summary>The wire ids of each definition's <c>x-c4</c> block, by origin.</summary>
    private static readonly Lazy<Dictionary<string, WireIdMap>> _ids = new(() =>
        Set.Definitions.ToDictionary(definition => definition.Specification.Origin!, definition => WireIdMap.Of(definition.Specification, WireBlock), StringComparer.Ordinal));

    /// <summary>The block holding the wire ids.</summary>
    private const string WireBlock = "x-c4";

    /// <summary>The palette of the C4 type <paramref name="origin"/> names: its definition's toolbox, with today's wire ids.</summary>
    /// <exception cref="KeyNotFoundException">The type has no definition, as <c>c4/code</c> has none.</exception>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox(ToolOrigin origin)
    {
        var specification = For(origin).Specification;
        return
        [
            .. ToolboxDerivation.Derive(specification, IdsOf(specification))
                .Select(tool => new ToolboxItemDefinition(tool.Id, tool.Label, tool.Icon, tool.Description, tool.DropActionId ?? "")),
        ];
    }

    /// <summary>
    /// The view a target of <paramref name="origin"/> is drawn on in <paramref name="workspace"/>: the first
    /// of the kind the type binds, else the first; null for a workspace that declares none.
    /// </summary>
    public static C4View? ViewOf(C4Workspace workspace, ToolOrigin? origin)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var kind = origin is null ? null : Types.Where(type => type.Origin == origin).Select(type => (C4ViewKind?)type.ViewKind).FirstOrDefault();
        return workspace.Views.FirstOrDefault(view => view.Kind == kind) ?? workspace.Views.FirstOrDefault();
    }

    /// <summary>
    /// The context menu of <paramref name="elementId"/> drawn on <paramref name="view"/> of <paramref name="workspace"/>,
    /// read from the body named <paramref name="file"/>:
    /// an element's or a stored relationship's, the empty canvas's for the empty id; none for anything else.
    /// </summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(C4Workspace workspace, string file, C4View? view, ToolOrigin? origin, string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);
        var specification = DefinitionOf(view, origin).Specification;
        var diagram = C4Disl.Of(workspace, view, specification, file).Diagram;
        DislMenuTarget? target = elementId.Length == 0
            ? DislMenuTarget.Canvas(diagram)
            : C4Disl.ElementOf(diagram, elementId) is { } element
                ? DislMenuTarget.Element(element)
                : null;
        return target is null
            ? []
            :
            [
                .. ContextMenuDerivation.Derive(specification, target, EnvOf(diagram), IdsOf(specification))
                    .Select(group => new ContextActionGroupDefinition([.. group.Entries.Select(Action)])),
            ];
    }

    /// <summary>
    /// What a drop of the toolbox entry whose drop action is <paramref name="dropActionId"/> does on
    /// <paramref name="elementId"/> drawn on <paramref name="view"/> of <paramref name="workspace"/> (the empty id for the
    /// empty canvas), read from the body named <paramref name="file"/>: the definition's <c>drop.targets</c> tried against
    /// the element's kind (DISL 7.2). A target that matches runs the entry's drop action there; a target whose operation
    /// is unavailable, and a drop no target takes, are refused with the definition's sentence. Null for an entry the
    /// definition does not drop, for an id that names no element, and for a drop that would do anything else.
    /// </summary>
    public static ContextActionDefinition? Drop(C4Workspace workspace, string file, C4View? view, ToolOrigin? origin, string elementId, string dropActionId)
    {
        ArgumentNullException.ThrowIfNull(elementId);
        ArgumentNullException.ThrowIfNull(dropActionId);
        var specification = DefinitionOf(view, origin).Specification;
        var tool = ToolboxDerivation.Derive(specification, IdsOf(specification)).FirstOrDefault(each => each.DropActionId == dropActionId);
        if (tool is null)
        {
            return null;
        }

        var diagram = C4Disl.Of(workspace, view, specification, file).Diagram;
        var on = C4Disl.ElementOf(diagram, elementId);
        if (elementId.Length > 0 && on is null)
        {
            return null;
        }

        return DislDrop.Resolve(specification, tool.Name, diagram, on, EnvOf(diagram)) switch
        {
            DislDropOutcome.Target { Kind: "operation" } => new ContextActionDefinition(dropActionId, tool.Label, tool.Icon),
            DislDropOutcome.Refused refused => new ContextActionDefinition(dropActionId, tool.Label, tool.Icon, Available: false, UnavailableReason: refused.Reason),
            _ => null,
        };
    }

    /// <summary>The property rows of <paramref name="elementId"/> drawn on <paramref name="view"/> of <paramref name="workspace"/>: an element's or a stored relationship's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(C4Workspace workspace, string file, C4View? view, ToolOrigin? origin, string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);
        var specification = DefinitionOf(view, origin).Specification;
        var diagram = C4Disl.Of(workspace, view, specification, file).Diagram;
        return C4Disl.ElementOf(diagram, elementId) is { } element
            ? [.. FormDerivation.Derive(specification, element, EnvOf(diagram), IdsOf(specification)).Select(DislDiagramDefinition.Row)]
            : [];
    }

    /// <summary>
    /// The identifier the definition's add <paramref name="operation"/> gives the element it creates, run
    /// inside <paramref name="parentId"/> (the empty canvas for none) with <paramref name="name"/> and handed
    /// <paramref name="identifier"/> (<see cref="DislIds.Fixed"/>), on a model of its own; null when it
    /// refuses, creates anything else, or the parent names nothing.
    /// </summary>
    public static string? Created(C4Workspace workspace, string file, C4View? view, ToolOrigin? origin, string operation, string parentId, string name, string identifier) =>
        Creation(workspace, file, view, origin, operation, parentId, name, identifier)?.Id;

    /// <summary>
    /// The element the definition's add <paramref name="operation"/> creates, as <see cref="Created"/> runs it: with
    /// the parent it puts it inside, which is not the element it ran on for an add that goes at the top of the model,
    /// as a person dropped on another element does. Null where <see cref="Created"/> is.
    /// </summary>
    public static DislChange.Create? Creation(C4Workspace workspace, string file, C4View? view, ToolOrigin? origin, string operation, string parentId, string name, string identifier)
    {
        ArgumentNullException.ThrowIfNull(parentId);
        var specification = DefinitionOf(view, origin).Specification;
        var diagram = C4Disl.Build(workspace, view, specification, file).Diagram;
        var parent = C4Disl.ElementOf(diagram, parentId);
        if (parentId.Length > 0 && parent is null)
        {
            return null;
        }

        var transaction = OperationInterpreter.Run(
            specification,
            operation,
            diagram,
            parent,
            DislIds.Fixed(identifier),
            new DislInvocation(Parameters: new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = name }),
            EnvOf(diagram));
        return transaction is { WasApplied: true, Changes: [DislChange.Create created] } ? created : null;
    }

    /// <summary>
    /// The findings of <paramref name="workspace"/>, read from the body named <paramref name="file"/>, under the
    /// definition of the C4 type <paramref name="origin"/> names, in today's order: the definition's rules
    /// evaluated over the model, which is the same for every view and every origin, with the one finding no
    /// rule can state yet added from the reading, and ordered by <see cref="C4FindingOrder"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Where each is</b>: a finding about an element names it, one about a relationship is at its line,
    /// one about a view is at the line of the view its subject names, and the include finding is nowhere.
    /// </para>
    /// <para>
    /// <b>Gap C13</b> (the definitions' companion, gap 13): a <c>View</c> exposes neither the scope it is
    /// written with nor its line, so <c>c4.unknown-view-scope</c> is no rule of the definition, and the
    /// empty view's line is not the finding's. Both come from the reading here until it does.
    /// </para>
    /// <para>
    /// <b>A finding the runtime raises while it builds the model</b> (<c>std.derivedFailed</c>,
    /// <c>std.derivedEnds</c>, <c>std.derivedId</c>) is logged and not reported, because the tool gives
    /// none today (c4-disl-fbl Requirement 3.3, Q4).
    /// </para>
    /// <para>
    /// <b>A type without a definition</b>, as <c>c4/code</c> has none, is judged under the system context's,
    /// as a target without a view is: the rules are about the model, so every definition gives one list.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<DiagramProblem> Problems(C4Workspace workspace, string file, ToolOrigin origin)
    {
        (IReadOnlyList<DislFinding> findings, DislDiagram diagram) = Evaluated(workspace, file, origin);
        return [.. C4FindingOrder.Order(findings, workspace).Select(finding => Problem(finding, diagram, workspace))];
    }

    /// <summary>
    /// The findings <see cref="Problems"/> reports, as the evaluator gives them, before they are ordered:
    /// by rule, and within a rule by element in model order, the one added from the reading last; with
    /// the model they are about.
    /// </summary>
    internal static (IReadOnlyList<DislFinding> Findings, DislDiagram Diagram) Evaluated(C4Workspace workspace, string file, ToolOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(origin);
        var specification = DefinitionOf(null, origin).Specification;
        var model = C4Disl.OfTheWholeModel(workspace, specification, file, kind => For(kind).Specification);
        foreach (var finding in model.Findings)
        {
            _logger.Warning("{File} under {Origin}: {Code} {Message}, which the tool does not report", file, origin.Key, finding.Code, finding.Message);
        }

        var findings = ConstraintEvaluator.Evaluate(specification, model.Diagram, new DislConstraintOptions(EnvOf(model.Diagram)));
        return ([.. findings, .. UnknownViewScopes(workspace)], model.Diagram);
    }

    /// <summary>
    /// <c>c4.unknown-view-scope</c> for each view whose scope names no element, at the view's line, from the
    /// reading: no rule can state it while a <c>View</c> does not expose its scope (gap C13).
    /// </summary>
    private static IEnumerable<DislFinding> UnknownViewScopes(C4Workspace workspace) =>
        workspace.Views
            .Where(view => view.ScopeId is { } scope && workspace.Find(scope) is null)
            .Select(view => new DislFinding(
                C4Rules.UnknownViewScope,
                C4Rules.UnknownViewScope,
                "warning",
                $"The '{view.Key}' view is scoped to '{view.ScopeId}', which the model does not declare.",
                [],
                (int)view.Line,
                Subject: view.Key));

    /// <summary>A finding as the host reports it: about an element by its id, about a relationship or a view at its line, else nowhere.</summary>
    private static DiagramProblem Problem(DislFinding finding, DislDiagram diagram, C4Workspace workspace)
    {
        var about = finding.ElementIds is [var id, ..] ? diagram.ElementById(id) : null;
        DiagramProblemLocation? location = about switch
        {
            { Type.IsRelation: true } => new DiagramProblemLineLocation((uint)(finding.Line ?? about.Line ?? 0)),
            not null => new DiagramProblemElementLocation(finding.ElementIds[0]),
            null when (finding.Line ?? ViewLineOf(workspace, finding.Subject)) is { } line => new DiagramProblemLineLocation((uint)line),
            null => null,
        };
        return new DiagramProblem(DislDiagramDefinition.SeverityOf(finding), finding.Message, finding.Code, location);
    }

    /// <summary>The line of the view <paramref name="key"/> names, the first of that key, for a finding about a view (gap C13).</summary>
    private static int? ViewLineOf(C4Workspace workspace, string? key) =>
        key is null ? null : workspace.Views.FirstOrDefault(each => string.Equals(each.Key, key, StringComparison.Ordinal)) is { } view ? (int)view.Line : null;

    /// <summary>
    /// What <paramref name="view"/> of <paramref name="workspace"/>, read from the body named <paramref name="file"/>,
    /// draws, from the definition of the view's kind: its members, its lines, its title and its legend.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The members</b> are the elements the viewpoint's <c>members</c> holds for (L2), in model order;
    /// of two elements of one identifier the first, as an edit finds it, because the wire carries one
    /// element per id (Q4: <c>std.duplicateId</c> is switched off).
    /// </para>
    /// <para>
    /// <b>The lines</b> are the relations the view shows: a stored relationship drawn as itself, and the
    /// derived <c>LiftedRelationship</c>, <c>InstanceLine</c> and <c>InteractionLine</c>, each with the id the
    /// definition computes. They come in the order of the relationship or interaction each is drawn for,
    /// the first of a merged group's, which is the order the body declares them: the model lists every
    /// derived relation after the stored ones. A line drawn between the two ends of the one statement it
    /// is drawn for names them as that statement writes them; any other names the elements it is drawn between.
    /// </para>
    /// <para>
    /// <b>The title and the legend</b> are the canvas's (DISL 6.13, L7), over the members; a legend entry
    /// carries the first member it stands for, from which the host paints its swatch.
    /// </para>
    /// <para>
    /// A finding the runtime raises while it derives the lines (<c>std.derivedFailed</c>,
    /// <c>std.derivedEnds</c>, <c>std.derivedId</c>) is logged and
    /// the line is not drawn, as for the findings (<see cref="Evaluated"/>).
    /// </para>
    /// </remarks>
    public static C4Drawing Drawing(C4Workspace workspace, string file, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(view);
        var specification = For(view.Kind).Specification;
        (DislDiagram diagram, IReadOnlyDictionary<DislElement, C4Element> elements, IReadOnlyList<DislElement> shown, List<DislElement> nodes) = Shown(workspace, file, view, specification);
        IReadOnlyList<C4Element> members = [.. nodes.Select(node => elements[node])];

        var declared = new Dictionary<DislElement, int>(ReferenceEqualityComparer.Instance);
        foreach (var relation in diagram.Relations.Where(relation => !relation.IsDerived))
        {
            declared[relation] = declared.Count;
        }

        IReadOnlyList<C4DrawnLine> lines =
        [
            .. shown.Where(member => member.Type.IsRelation)
                .OrderBy(line => declared.GetValueOrDefault(line.IsDerived ? line.Sources[0] : line, int.MaxValue))
                .Select(Line),
        ];

        var env = EnvOf(diagram);
        var chrome = CanvasChromeDerivation.Derive(diagram, nodes, env);

        // One entry per key, in the order of the members and painted from the first member of each
        // (gap 15): the derivation orders its groups by model order, so each member's entry is derived
        // on its own, and the first of every key is kept.
        IReadOnlyList<C4LegendItem> legend =
        [
            .. nodes
                .Select(node => (Node: node, Entry: CanvasChromeDerivation.Derive(diagram, [node], env).Legend?.Entries.FirstOrDefault()))
                .Where(each => each.Entry is not null)
                .DistinctBy(each => each.Entry!.Key, StringComparer.Ordinal)
                .Select(each => new C4LegendItem(each.Entry!.Label, elements[each.Node])),
        ];
        return new C4Drawing(members, lines, chrome.Title ?? "", legend);
    }

    /// <summary>
    /// <paramref name="nodes"/>, the members of <paramref name="view"/>, in the order of the statements that
    /// select them, which is the order the tool draws, lays out and keys them in: what an <c>include</c> names,
    /// in the order of the includes; then what <c>include *</c> selects, in model order; then, on a dynamic
    /// view, what its interactions name, in their order.
    /// </summary>
    /// <remarks>
    /// <b>Gap 15</b>, widened (c4-disl-fbl task 10): DISL 3.5 gives a view's members in model order, and
    /// nothing orders them by the statement that selects them. The kinds <c>include *</c> selects are the
    /// definition's own (its <c>permitted</c> function), asked only on a dynamic view that has a wildcard,
    /// the one case where an element both a wildcard and an interaction select is placed by the wildcard.
    /// </remarks>
    private static IEnumerable<DislElement> InStatementOrder(IEnumerable<DislElement> nodes, IReadOnlyDictionary<DislElement, C4Element> elements, C4View view, DislDiagram diagram)
    {
        List<string> participants = [.. view.Interactions.SelectMany(interaction => new[] { interaction.SourceId, interaction.DestinationId })];
        IReadOnlyList<string> wildcard = view.IncludesEverything && participants.Count > 0 ? PermittedTypes(diagram) : [];
        return nodes
            .Select((node, position) => (Node: node, Rank: RankOf(elements[node], node, position)))
            .OrderBy(each => each.Rank.Group)
            .ThenBy(each => each.Rank.Index)
            .Select(each => each.Node);

        (int Group, int Index) RankOf(C4Element element, DislElement node, int position)
        {
            var included = view.Includes.ToList().FindIndex(include => string.Equals(include, element.Id, StringComparison.OrdinalIgnoreCase));
            if (included >= 0)
            {
                return (0, included);
            }

            var participant = participants.FindIndex(id => string.Equals(id, element.Id, StringComparison.OrdinalIgnoreCase));
            return view.IncludesEverything && (participant < 0 || wildcard.Contains(node.Type.Name, StringComparer.Ordinal))
                ? (1, position)
                : (2, participant < 0 ? position : participant);
        }
    }

    /// <summary>The types <c>include *</c> selects on the view <paramref name="diagram"/> shows: the definition's <c>permitted(env.viewpoint)</c>.</summary>
    private static IReadOnlyList<string> PermittedTypes(DislDiagram diagram) =>
        diagram.Specification.Environment(DislContext.Element).Compile("permitted(env.viewpoint)")
            .Evaluate(new Dictionary<string, object?> { ["self"] = null, ["diagram"] = diagram, ["env"] = diagram.Env.ToCel() }) is IReadOnlyList<object?> types
            ? [.. types.OfType<string>()]
            : [];

    /// <summary>The elements <paramref name="view"/> shows, as <see cref="Drawing"/> gives them, without its lines, title and legend.</summary>
    public static IReadOnlyList<C4Element> Members(C4Workspace workspace, string file, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(view);
        (_, IReadOnlyDictionary<DislElement, C4Element> elements, _, List<DislElement> nodes) = Shown(workspace, file, view, For(view.Kind).Specification);
        return [.. nodes.Select(node => elements[node])];
    }

    /// <summary>
    /// The model of <paramref name="view"/>, the workspace's element of each of its nodes, everything the view
    /// shows, and of that the nodes, in the order of the view's statements and one per identifier.
    /// </summary>
    private static (DislDiagram Diagram, IReadOnlyDictionary<DislElement, C4Element> Elements, IReadOnlyList<DislElement> Shown, List<DislElement> Nodes) Shown(C4Workspace workspace, string file, C4View view, DislSpecification specification)
    {
        var model = C4Disl.Of(workspace, view, specification, file);
        foreach (var finding in model.Findings)
        {
            _logger.Warning("{File}, view {View}: {Code} {Message}, which the tool does not report", file, view.Key, finding.Code, finding.Message);
        }

        var diagram = model.Diagram;
        var elements = C4Disl.ElementsOf(diagram, workspace);
        var shown = C4Disl.ShownView(diagram, view).Members;
        List<DislElement> nodes =
        [
            .. InStatementOrder(shown.Where(member => !member.Type.IsRelation), elements, view, diagram)
                .DistinctBy(node => elements[node].Id, StringComparer.OrdinalIgnoreCase),
        ];
        return (diagram, elements, shown, nodes);
    }

    /// <summary>A line as the mapper draws it, from the relation the view shows.</summary>
    private static C4DrawnLine Line(DislElement line)
    {
        var statement = line.IsDerived ? line.Sources[0] : line;
        var asWritten = !line.IsDerived || (line.Sources.Count == 1 && ReferenceEquals(statement.Source, line.Source) && ReferenceEquals(statement.Target, line.Target));
        return new C4DrawnLine(
            line.Id,
            asWritten ? C4Disl.WrittenEndOf(statement, "source") : line.Source?.Id ?? "",
            asWritten ? C4Disl.WrittenEndOf(statement, "target") : line.Target?.Id ?? "",
            TextOf(line, "description"),
            TextOf(line, "technology"),
            TextOf(line, "order"));
    }

    /// <summary>A text attribute of <paramref name="element"/>; empty when its type has none.</summary>
    private static string TextOf(DislElement element, string attribute) =>
        element.Type.Attributes.ContainsKey(attribute) && element.ValueOf(attribute) is string text ? text : "";

    /// <summary>The definition a target is derived from: its view's kind's, else its origin's, else the system context's.</summary>
    private static BundledDefinition DefinitionOf(C4View? view, ToolOrigin? origin) =>
        view is not null ? For(view.Kind)
        : origin is not null && Find(origin) is { } own ? own
        : For(C4ViewKind.SystemContext);

    private static WireIdMap IdsOf(DislSpecification specification) => _ids.Value[specification.Origin!];

    /// <summary>A menu entry as the host sends it, its shortcut as a browser spells the key.</summary>
    private static ContextActionDefinition Action(DerivedMenuEntry entry) =>
        new(entry.Id, entry.Label, entry.Icon, DislDiagramDefinition.ShortcutOf(entry), entry.Available, entry.UnavailableReason);
}
