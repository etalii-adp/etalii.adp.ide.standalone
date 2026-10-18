using System.Reflection;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Several definitions bundled into one module's assembly, each the definition of a tool type of its
/// own, found by that tool type's origin (DISL §3.2, <c>language.origin</c>): the readings of one body by
/// several tool types (FBL §9), where a registration's origin says which definition a reading is drawn by.
/// </summary>
/// <remarks>
/// <para>
/// Each definition is loaded as <see cref="BundledDefinition.Load(Assembly, string, DislPluginFunctions)"/>
/// loads one, with its own provenance beside it, so a set refuses whatever that refuses. A set also
/// refuses a definition that declares no origin, and two that declare one origin: either would leave
/// a reading that cannot say which definition it is drawn by.
/// </para>
/// <para>
/// The derivations take the specification as an argument, so a set adds no derivation of its own: a
/// host finds the definition by origin and hands its <see cref="BundledDefinition.Specification"/> to
/// <see cref="ToolboxDerivation"/>, <see cref="ContextMenuDerivation"/>, <see cref="FormDerivation"/> and
/// <see cref="ConstraintEvaluator"/>, with a model built under that same specification.
/// </para>
/// </remarks>
public sealed class BundledDefinitionSet
{
    private readonly Dictionary<string, BundledDefinition> _byOrigin;

    private BundledDefinitionSet(IReadOnlyList<BundledDefinition> definitions, Dictionary<string, BundledDefinition> byOrigin)
    {
        Definitions = definitions;
        _byOrigin = byOrigin;
    }

    /// <summary>The definitions, in the order they were given.</summary>
    public IReadOnlyList<BundledDefinition> Definitions { get; }

    /// <summary>The origins of the definitions, in the order they were given.</summary>
    public IReadOnlyList<string> Origins => [.. Definitions.Select(definition => definition.Specification.Origin!)];

    /// <summary>
    /// Loads the definitions <paramref name="assembly"/> embeds as <paramref name="logicalNames"/>, each with
    /// its provenance, with the host's implementations of their plugins' CEL functions (DISL §13.1.1);
    /// <see cref="DislPluginFunctions.None"/> where they declare none.
    /// </summary>
    /// <exception cref="InvalidOperationException">A definition does not load, declares no origin, or declares the origin of another.</exception>
    public static BundledDefinitionSet Load(Assembly assembly, DislPluginFunctions plugins, params IEnumerable<string> logicalNames)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(logicalNames);
        return Of(logicalNames.Select(name => BundledDefinition.Load(assembly, name, plugins)));
    }

    /// <summary>A set of definitions already loaded.</summary>
    /// <exception cref="InvalidOperationException">There are none, one declares no origin, or two declare one origin.</exception>
    public static BundledDefinitionSet Of(params IEnumerable<BundledDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var list = definitions.ToList();
        if (list.Count == 0) throw new InvalidOperationException("A set of bundled definitions holds at least one.");

        var byOrigin = new Dictionary<string, BundledDefinition>(StringComparer.Ordinal);
        foreach (var definition in list)
        {
            var origin = definition.Specification.Origin
                ?? throw new InvalidOperationException($"{definition.Provenance.Path} declares no language.origin, so no reading can name it.");
            if (!byOrigin.TryAdd(origin, definition))
            {
                throw new InvalidOperationException($"{definition.Provenance.Path} and {byOrigin[origin].Provenance.Path} both declare the origin {origin}.");
            }
        }
        return new BundledDefinitionSet(list, byOrigin);
    }

    /// <summary>The definition of the tool type <paramref name="origin"/> names, or null when the set has none.</summary>
    public BundledDefinition? Find(string origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return _byOrigin.GetValueOrDefault(origin);
    }

    /// <summary>The definition of the tool type <paramref name="origin"/> names.</summary>
    /// <exception cref="KeyNotFoundException">The set has no definition of that origin.</exception>
    public BundledDefinition For(string origin) =>
        Find(origin) ?? throw new KeyNotFoundException($"No bundled definition has the origin {origin}; the set has {string.Join(", ", Origins)}.");
}
