using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests.Parity;

/// <summary>
/// The bundled behavior model definition, and the parity corpus as its CEL sees it. The module has no
/// FBL reader yet, so this is the small adapter that stands in for one: each <see cref="AbmNode"/>
/// put into a <see cref="DislDiagram"/> as the type whose <c>x-abm.kind</c> is its kind, field for
/// field, parents before children.
/// </summary>
internal static class AbmDisl
{
    public static DislSpecification Specification { get; } =
        BundledDefinition.Load(typeof(AbmParser).Assembly, "agent-behavior-modelling.dis").Specification;

    /// <summary>The DISL type of each of the eleven kinds, read from the definition's <c>x-abm.kind</c>.</summary>
    public static IReadOnlyDictionary<string, string> TypeOfKind { get; } = Specification.Metamodel.Types.Values
        .Where(type => type.Extensions.ContainsKey("x-abm"))
        .ToDictionary(type => type.Extensions["x-abm"].GetProperty("kind").GetString()!, type => type.Name, StringComparer.Ordinal);

    /// <summary>The four examples and the inline documents, by name.</summary>
    public static IEnumerable<(string Name, string Text)> Corpus() =>
        AbmExamples.Names.Select(name => ($"examples/{name}", File.ReadAllText(AbmExamples.BodyOf(name)))).Concat(AbmCorpus.Inline);

    public static AbmModel Parse(string text) => AbmParser.Parse(LineDocument.Parse(text));

    /// <summary>
    /// A diagram holding <paramref name="model"/>'s nodes in document order, beneath their parents.
    /// With <paramref name="ids"/> false the ids are left empty, for the definition's derived-id rule
    /// to compute.
    /// </summary>
    public static DislDiagram DiagramOf(AbmModel model, bool ids = true)
    {
        var diagram = new DislDiagram(Specification);
        var byId = new Dictionary<string, DislElement>(StringComparer.Ordinal);
        foreach (var node in model.Nodes)
        {
            var parent = node.ParentId is { } parentId ? byId[parentId] : null;
            byId[node.Id] = NodeOf(diagram, node, ids ? node.Id : "", parent);
        }
        return diagram;
    }

    /// <summary>Adds <paramref name="node"/> with the attributes its type has: a Retry's attempts, a Do's <c>implicit</c>.</summary>
    public static DislElement NodeOf(DislDiagram diagram, AbmNode node, string id, DislElement? parent = null)
    {
        var type = TypeOfKind[node.Kind];
        var attributes = new Dictionary<string, object?> { ["label"] = node.Label, ["notes"] = node.Notes };
        if (type == "Retry") attributes["attempts"] = (long)node.RetryCount;
        if (type == "Do") attributes["implicit"] = !node.HasKeyword;
        return diagram.AddNode(type, id, attributes, parent);
    }

    /// <summary>Evaluates <paramref name="expression"/> in the element context, <paramref name="variables"/> bound beside <c>self</c>, <c>diagram</c> and <c>env</c>.</summary>
    public static object? Evaluate(string expression, IReadOnlyDictionary<string, object?> variables) =>
        Checked(expression, Specification.Environment(DislContexts.Element, variables.Keys).Compile(expression).Evaluate(variables));

    /// <summary>The compiled expression at <paramref name="pointer"/> evaluated with <paramref name="variables"/>.</summary>
    public static object? EvaluateAt(string pointer, IReadOnlyDictionary<string, object?> variables) =>
        Checked(pointer, (Specification.ExpressionAt(pointer) ?? throw new InvalidOperationException($"No expression at {pointer}.")).Program.Evaluate(variables));

    private static object? Checked(string what, object? value) =>
        value is CelError error ? throw new InvalidOperationException($"{what}: {error.Message}") : value;
}
