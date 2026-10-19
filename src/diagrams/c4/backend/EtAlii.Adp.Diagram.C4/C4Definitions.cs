using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Specification.Disl;
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
/// ordered as today by <see cref="C4FindingOrder"/>; the drawn lines, the title and the legend are still
/// the module's own until the tasks after it. The toolbox is the origin's. A menu
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
    public static IReadOnlyList<(string Path, DislDiagnostic Diagnostic)> Diagnostics =>
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
    public static string? Created(C4Workspace workspace, string file, C4View? view, ToolOrigin? origin, string operation, string parentId, string name, string identifier)
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
        return transaction is { WasApplied: true, Changes: [DislChange.Create created] } ? created.Id : null;
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
