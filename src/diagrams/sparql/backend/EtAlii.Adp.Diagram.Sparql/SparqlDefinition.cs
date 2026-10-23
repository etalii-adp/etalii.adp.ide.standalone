using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The SPARQL query diagram's bundled DISL definition (<c>definition/sparql-query.dis</c>, from etalii-adp/etalii.adp),
/// loaded once, and what is derived from it: the model of a query and what it draws.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is the shared builder's.</b> <see cref="SparqlRecords"/> gives the stored records a query is read as,
/// and <see cref="DislModelBuilder"/> builds the model from them under the definition: the variables, one per name at
/// the shallowest scope that mentions it and lifted out of a <c>UNION</c>, the concrete and anonymous terms, and the
/// pattern and subquery edges are the definition's derived types, and every id is the definition's id rules'.
/// </para>
/// <para>
/// <b><see cref="Drawing"/> holds no placement, no id, no order and no cut.</b> It reads the drawn elements of the
/// model in the order of the definition's budget, which also decides what is cut at 500 and the two numbers the banner
/// reports, and fills the records <see cref="SparqlLayout"/> and <see cref="SparqlElementMapper"/> take.
/// </para>
/// <para>
/// <b>Everything is read-only</b> (<c>env.readOnly</c>): nothing a query diagram shows can be edited through it.
/// Every row carries the reason its form item gives, a write is refused with the definition's <c>std.readOnly</c>, a
/// move with the notation's refusal or the ephemeral id rule's reason, and a drag onto another parent with the
/// notation's <c>reparent</c> refusal.
/// </para>
/// <para>
/// <b>The findings are the definition's</b> (<see cref="ConstraintEvaluator"/>): the unparseable finding, worded by
/// <c>constraints.builtIn</c>, alone when the parser could not read the query, and otherwise the definition's rules.
/// </para>
/// </remarks>
internal static class SparqlDefinition
{
    /// <summary>The definition's budget of drawn elements (<c>language.limits.budgets</c>).</summary>
    private const string DrawnBudget = "drawn";

    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(SparqlDefinition).Assembly, "sparql-query.dis"));

    private static readonly Lazy<WireIdMap> LoadedIds = new(() => WireIdMap.Of(Specification, "x-sparql"));

    private static readonly Lazy<string> LoadedRefusal = new(RefusalOf);

    private static readonly Lazy<string> LoadedReparentRefusal = new(ReparentRefusalOf);

    /// <summary>What <c>env</c> reads: nothing in this diagram can be edited.</summary>
    private static readonly DislEnv Env = new(ReadOnly: true);

    /// <summary>The definition.</summary>
    internal static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>The wire ids of <c>x-sparql</c>.</summary>
    internal static WireIdMap Ids => LoadedIds.Value;

    /// <summary>Why nothing here can be written: the definition's <c>std.readOnly</c>.</summary>
    public static string Refusal => LoadedRefusal.Value;

    /// <summary>Why nothing here can be moved into another group: the notation's <c>reparent</c> refusal, which every kind it draws states alike.</summary>
    public static string ReparentRefusal => LoadedReparentRefusal.Value;

    /// <summary>The toolbox the definition declares: none, since a reading surface offers nothing to place.</summary>
    public static IReadOnlyList<DerivedTool> Toolbox => ToolboxDerivation.Derive(Specification, Ids);

    /// <summary>The model of <paramref name="query"/>, read from <paramref name="text"/>: the builder's, from its records.</summary>
    public static DislModel Model(SparqlQueryModel query, string text) => Model(query, text, Specification);

    /// <summary>The model of <paramref name="query"/>, read from <paramref name="text"/>, under <paramref name="specification"/>.</summary>
    private static DislModel Model(SparqlQueryModel query, string text, DislSpecification specification) =>
        DislModelBuilder.From(SparqlRecords.From(query, text), specification, Env);

    /// <summary>What <paramref name="document"/> draws: its model's drawn elements, in the budget's order, with its two numbers.</summary>
    public static SparqlProjectionResult Drawing(SparqlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Drawing(document.Disl);
    }

    /// <summary>What <paramref name="model"/> draws: its drawn elements, in the budget's order, with its two numbers.</summary>
    private static SparqlProjectionResult Drawing(DislModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var diagram = model.Diagram;
        var nodes = new List<SparqlNode>();
        var edges = new List<SparqlEdge>();
        var regions = new List<SparqlRegion>();
        var annotations = new List<SparqlAnnotation>();
        foreach (var element in diagram.InBudgetOrder(DrawnBudget))
        {
            if (element.IsA("Scope"))
            {
                regions.Add(new SparqlRegion(element.Id, Text(element, "scopeKind"), Text(element, "label"), element.Parent?.Id ?? "", Text(element, "path")));
            }
            else if (element.IsA("TriplePattern"))
            {
                edges.Add(new SparqlEdge(element.Id, element.Source!.Id, element.Target!.Id, Text(element, "predicate"), Flag(element, "isPath"), Text(element, "scope")));
            }
            else if (element.IsA("ProjectedJoin"))
            {
                // The join is labelled with the name it feeds, as the variable it reaches is.
                edges.Add(new SparqlEdge(element.Id, element.Source!.Id, element.Target!.Id, $"?{Text(element, "name")}", false, Text(element.Source, "scope")));
            }
            else if (element.IsA("Annotation"))
            {
                annotations.Add(new SparqlAnnotation(element.Id, Text(element, "constraintKind"), Text(element, "text"), Text(element, "attachedTo"), Text(element, "scope")));
            }
            else
            {
                nodes.Add(Node(element));
            }
        }

        var budget = diagram.Budget(DrawnBudget)!;
        var headerRows = Texts(diagram.ValueOf("datasetClauses")).Concat(Texts(diagram.ValueOf("modifierRows"))).ToList();
        return new SparqlProjectionResult(nodes, edges, regions, annotations, diagram.ValueOf("headerForm") as string ?? "", headerRows, (int)budget.Shown, (int)budget.Total);
    }

    /// <summary>
    /// The property rows of the element <paramref name="wireId"/> names in what <paramref name="document"/> draws: its
    /// form's; none for an element the budget cut or an id the model does not hold.
    /// </summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(SparqlDocument document, string wireId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(wireId);
        var diagram = document.Disl.Diagram;
        if (diagram.ElementById(wireId) is not { } element || !diagram.IsDrawn(element)) return [];

        return [.. FormDerivation.Derive(Specification, element, Env, Ids).Select(Row)];
    }

    /// <summary>The property rows of the query itself (the diagram's form), which the header band carries.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> DiagramRows(SparqlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return [.. FormDerivation.Derive(Specification, document.Disl.Diagram, Env, Ids).Select(Row)];
    }

    /// <summary>
    /// The findings of <paramref name="document"/>, read from a text that <paramref name="unparsed"/> says did not parse
    /// when it is given: then that finding alone, worded by the definition.
    /// </summary>
    public static IReadOnlyList<DislFinding> Findings(SparqlDocument document, DislReaderFinding? unparsed = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = new DislConstraintOptions(Env, unparsed is null ? [] : [unparsed]);
        return ConstraintEvaluator.Evaluate(Specification, document.Disl.Diagram, options);
    }

    /// <summary>
    /// Why the element <paramref name="wireId"/> names cannot be given a position, or null when it can or the model holds
    /// no such element: the refusal the notation states for a move of its kind, or the reason of its ephemeral id rule.
    /// </summary>
    public static string? MoveRefusal(SparqlDocument document, string wireId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(wireId);
        if (document.Disl.Diagram.ElementById(wireId) is not { } element) return null;

        var bounds = new Dictionary<string, object?>();
        return GestureConstraintEvaluator.Placement(Specification, element, "move", bounds, bounds, env: Env).FirstOrDefault()?.Message;
    }

    /// <summary>A row as the grid draws it: every item here is a <c>readonly</c> widget, which is one line.</summary>
    private static ContextPropertyDefinition Row(DerivedRow row) =>
        new(row.Id, row.Label, row.Value, ContextPropertyEditor.Line, row.ReadOnlyReason, row.Group);

    /// <summary>The sentence of <c>behavior.messages</c>' <c>std.readOnly</c>, as the definition writes it.</summary>
    private static string RefusalOf() =>
        Specification.Json.GetProperty("behavior").GetProperty("messages").GetProperty("std.readOnly") is { ValueKind: JsonValueKind.String } message
            ? message.GetString()!
            : throw new InvalidOperationException("The definition words no std.readOnly to refuse a write with.");

    /// <summary>
    /// The notation's <c>reparent</c> refusal. A drag onto another parent is refused before the session knows what was
    /// dragged, so the definition must state one sentence for every kind it draws, and this holds it to that.
    /// </summary>
    private static string ReparentRefusalOf()
    {
        var notation = Specification.Json.GetProperty("notation");
        var sentences = new[] { "nodes", "edges" }
            .Where(kind => notation.TryGetProperty(kind, out _))
            .SelectMany(kind => notation.GetProperty(kind).EnumerateObject())
            .Select(type => type.Value.TryGetProperty("refusals", out var refusals) && refusals.TryGetProperty("reparent", out var reparent) && reparent.ValueKind == JsonValueKind.Array && reparent.GetArrayLength() == 1 && reparent[0].ValueKind == JsonValueKind.String
                ? reparent[0].GetString()
                : null)
            .Distinct()
            .ToList();
        return sentences is [{ } sentence]
            ? sentence
            : throw new InvalidOperationException("The definition's notation does not state one reparent refusal for every kind it draws.");
    }

    /// <summary>A term's node: a variable, an anonymous variable, an IRI, a literal or a subquery, in the region of its parent.</summary>
    private static SparqlNode Node(DislElement element)
    {
        var scope = element.Parent is { } parent ? Text(parent, "path") : "where";
        var display = Text(element, "display");
        return element.Type.Name switch
        {
            "Variable" => new SparqlNode(element.Id, SparqlNodeKind.Variable, display, scope,
                Projected: Flag(element, "projected"), JoinCount: (int)(element.ValueOf("joinCount") as long? ?? 0), DefiningExpression: Text(element, "definingExpression")),
            "AnonymousVariable" => new SparqlNode(element.Id, SparqlNodeKind.Anonymous, display, scope),
            "Iri" => new SparqlNode(element.Id, SparqlNodeKind.Iri, display, scope, Full: Text(element, "full")),
            "Literal" => new SparqlNode(element.Id, SparqlNodeKind.Literal, display, scope, Full: Text(element, "full"), Annotation: Text(element, "annotation")),
            _ => new SparqlNode(element.Id, SparqlNodeKind.SubSelect, display, scope, Full: Text(element, "text")),
        };
    }

    private static string Text(DislElement element, string attribute) => element.ValueOf(attribute) as string ?? "";

    private static bool Flag(DislElement element, string attribute) => element.ValueOf(attribute) is true;

    private static IEnumerable<string> Texts(object? value) => value is IEnumerable<object?> items ? items.Select(item => item as string ?? "") : [];
}
