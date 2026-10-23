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
/// </para>
/// </remarks>
internal static class SparqlDefinition
{
    /// <summary>The definition's budget of drawn elements (<c>language.limits.budgets</c>).</summary>
    private const string DrawnBudget = "drawn";

    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(SparqlDefinition).Assembly, "sparql-query.dis"));

    /// <summary>What <c>env</c> reads: nothing in this diagram can be edited.</summary>
    private static readonly DislEnv Env = new(ReadOnly: true);

    /// <summary>The definition.</summary>
    internal static DislSpecification Specification => Loaded.Value.Specification;

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
