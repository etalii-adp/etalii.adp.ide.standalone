using System.Text.Json;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// One view of the model (DISL §12.2, <c>View</c>): its id, its viewpoint, its name and the elements
/// it shows, as <c>diagram.views</c> reads them and a constraint over the model asks about them (§8.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Its members are computed from its viewpoint</b> (§3.5, membership), unless the host gave them:
/// every element of the model, nodes then relations in model order, whose type passes the viewpoint's
/// <c>include</c> and <c>exclude</c> and for which its <c>members</c> holds. That expression sees the
/// view: <c>env.viewpoint</c> is the view's viewpoint, and <c>diagram</c> reads the view's own diagram
/// attributes where it has them, the model's otherwise. A <c>members</c> that fails holds for nothing.
/// Membership is step 1 of what is drawn (§6.1); whether a relation's ends are drawn is not asked here.
/// </para>
/// <para>
/// <b>A viewpoint the specification does not declare</b> has no membership to compute: such a view's
/// members are what the host gave, and reading them without is an evaluation error. A specification
/// that declares no viewpoint has the implicit <c>main</c>, which shows everything.
/// </para>
/// <para>Computed when first read and again after the model changes; never stored.</para>
/// </remarks>
public sealed class DislView : ICelObject
{
    internal DislView(DislDiagram diagram, string id, string viewpoint, string name, IReadOnlyDictionary<string, object?> attributes, IReadOnlyList<DislElement>? members)
    {
        Diagram = diagram;
        Id = id;
        Viewpoint = viewpoint;
        Name = name;
        Attributes = attributes;
        Given = members;
    }

    public DislDiagram Diagram { get; }

    public string Id { get; }

    /// <summary>The viewpoint the view is of (§3.5).</summary>
    public string Viewpoint { get; }

    public string Name { get; }

    /// <summary>The diagram attributes this view has of its own, as CEL values: a reference by the id it names.</summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; }

    /// <summary>The elements of the model the view shows, in model order.</summary>
    public IReadOnlyList<DislElement> Members => Given ?? (Cached ??= Computed());

    /// <summary>The members the host gave, when it gave them.</summary>
    private IReadOnlyList<DislElement>? Given { get; }

    /// <summary>The members computed when last read, until the model changes.</summary>
    private IReadOnlyList<DislElement>? Cached { get; set; }

    /// <summary>The model changed: the members are computed again when next read.</summary>
    internal void Changed() => Cached = null;

    private List<DislElement> Computed()
    {
        var specification = Diagram.Specification;
        var declared = specification.Root.TryGetProperty("viewpoints", out var viewpoints) && viewpoints.ValueKind == JsonValueKind.Object;
        if (!declared) return [.. Diagram.Elements];
        if (!viewpoints.TryGetProperty(Viewpoint, out var viewpoint) || viewpoint.ValueKind != JsonValueKind.Object)
        {
            throw new CelException($"The view '{Id}' is of the viewpoint '{Viewpoint}', which this specification does not declare, and was given no members.");
        }

        var include = DislJson.Strings(viewpoint, "include");
        var exclude = DislJson.Strings(viewpoint, "exclude");
        var members = specification.ExpressionAt(DislJson.Pointer(DislJson.Pointer("/viewpoints", Viewpoint), "members"));
        var variables = new Dictionary<string, object?>
        {
            ["diagram"] = new DislViewDiagram(this),
            ["env"] = (Diagram.Env with { Viewpoint = Viewpoint }).ToCel(),
        };
        return
        [
            .. Diagram.Elements.Where(element =>
                (include.Count == 0 || include.Any(element.IsA))
                && !exclude.Any(element.IsA)
                && (members is null || members.Program.Evaluate(new Dictionary<string, object?>(variables) { ["self"] = element }) is true)),
        ];
    }

    /// <summary>The value CEL reads for diagram attribute <paramref name="name"/> in this view: its own, resolved as the model's are, else the model's.</summary>
    internal object? ValueOf(string name)
    {
        if (!Attributes.TryGetValue(name, out var value)) return Diagram.ValueOf(name);
        var attribute = Diagram.Specification.Metamodel.DiagramAttributes[name];
        if (Diagram.Specification.Metamodel.TypeOf(attribute.Type) is null) return value;
        return value switch
        {
            string id => Diagram.ElementById(id),
            IReadOnlyList<object?> ids => ids.Select(id => id is string text ? Diagram.ElementById(text) : null).ToList(),
            _ => value,
        };
    }

    /// <inheritdoc />
    public bool TryGetMember(string name, out object? value)
    {
        value = name switch
        {
            "id" => Id,
            "viewpoint" => Viewpoint,
            "name" => Name,
            "members" => Members.Cast<object?>().ToList(),
            _ => null,
        };
        return name is "id" or "viewpoint" or "name" or "members";
    }

    /// <inheritdoc />
    public bool HasMember(string name) => name is "id" or "viewpoint" or "name" or "members";

    /// <inheritdoc />
    public bool TryInvoke(string name, IReadOnlyList<object?> arguments, out object? value)
    {
        value = null;
        return false;
    }

    public override string ToString() => $"view {Id}";
}

/// <summary>
/// The model as a view's membership sees it: the diagram, with the view's own diagram attributes in
/// place of the model's (<see cref="DislView"/>).
/// </summary>
internal sealed class DislViewDiagram(DislView view) : ICelObject
{
    public bool TryGetMember(string name, out object? value)
    {
        if (view.Diagram.Specification.Metamodel.DiagramAttributes.ContainsKey(name))
        {
            value = view.ValueOf(name);
            return true;
        }
        return view.Diagram.TryGetMember(name, out value);
    }

    public bool HasMember(string name) => view.Attributes.ContainsKey(name) || view.Diagram.HasMember(name);

    public bool TryInvoke(string name, IReadOnlyList<object?> arguments, out object? value) => view.Diagram.TryInvoke(name, arguments, out value);

    public override string ToString() => view.Diagram.ToString();
}
