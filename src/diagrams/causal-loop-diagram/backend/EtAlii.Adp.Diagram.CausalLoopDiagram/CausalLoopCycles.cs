using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The CEL function of the definition's <c>net.etalii.adp.systems.cldCycles</c> plugin (DISL §13.1.1):
/// <c>causalCycles(variables, links, max)</c>, the module's <see cref="CycleFinder"/> over the model's
/// variables and causal links, so the unlabelled-loop findings and Claim a loop see the cycles in the
/// order the tool has always found them.
/// </summary>
/// <remarks>
/// <para>
/// <b>It stands in for an order DISL does not give.</b> DISL 12.4's <c>diagram.cycles</c> finds the
/// same cycles and sorts them; Johnson's algorithm reports them as it finds them, and the two differ
/// over the corpus (<c>weight-forms.cld</c>). The definition's companion records the difference under
/// "Other differences: the order of cycles"; without this plugin the definition falls back to DISL's.
/// </para>
/// <para>
/// A variable is named by its <c>name</c>, the first of a name standing for every one of it, and a
/// link's ends are its <c>causeName</c> and <c>effectName</c>, as the tool's parser read them.
/// </para>
/// </remarks>
internal static class CausalLoopCycles
{
    /// <summary>The implementations, for <see cref="BundledDefinition.Load(System.Reflection.Assembly, string, DislPluginFunctions)"/>.</summary>
    public static DislPluginFunctions Plugins() => new DislPluginFunctions()
        .Add("causalCycles", arguments => Find(Elements(arguments[0]), Elements(arguments[1]), Bound(arguments[2])));

    private static List<object?> Find(IReadOnlyList<DislElement> variables, IReadOnlyList<DislElement> links, int bound)
    {
        var byName = new Dictionary<string, DislElement>(StringComparer.Ordinal);
        foreach (var variable in variables) byName.TryAdd(Text(variable, "name"), variable);

        var model = new CausalLoopModel(
            [.. byName.Keys.Select(name => new CausalLoopVariable(name, name, default))],
            [.. links.Select(link => new CausalLoopLink(Text(link, "causeName"), Text(link, "effectName"), CausalLoopPolarity.Unstated, false, false, null, "", default))],
            []);
        return [.. CycleFinder.Find(model, bound).Cycles.Select(cycle => (object?)cycle.Select(name => (object?)byName[name]).ToList())];
    }

    private static List<DislElement> Elements(object? argument) => argument is IEnumerable<object?> items
        ? [.. items.Select(item => item as DislElement ?? throw new CelException("causalCycles takes lists of elements."))]
        : throw new CelException("causalCycles takes lists of elements.");

    private static int Bound(object? argument) => argument is long bound and >= 1 and <= int.MaxValue
        ? (int)bound
        : throw new CelException("causalCycles takes a bound of at least 1.");

    private static string Text(DislElement element, string attribute) =>
        element.Attributes.GetValueOrDefault(attribute) as string ?? "";
}
