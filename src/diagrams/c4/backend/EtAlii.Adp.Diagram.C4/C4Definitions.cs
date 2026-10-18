using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// The six C4 diagram types, each paired with the view kind it binds and with its DISL definition,
/// bundled from etalii-adp/etalii.adp into <c>definition/</c> (<c>c4-context.dis</c> and the five beside
/// it), loaded once as one <see cref="BundledDefinitionSet"/> and found by origin or by view kind.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing derives from the definitions yet</b> (c4-disl-fbl task 5): the toolbox, the menus, the
/// rows and the findings are still the module's own, and the providers that will hand out what is
/// derived from these arrive in the tasks after it. The table of types is used already, by
/// <see cref="ServiceCollectionAddC4Extension.AddC4"/>, which reads it without loading anything.
/// </para>
/// <para>
/// <b><c>c4/code</c> has no definition</b> (c4-disl-fbl Requirement 2.7): it keeps its registration and
/// its refusing empty document, and <see cref="Find(ToolOrigin)"/> answers null for it.
/// </para>
/// <para>
/// The definitions declare one plugin function, <c>writtenEnd</c>, which is not required and has a
/// fallback. It is loaded without the module's implementation, because the reading it would answer
/// from (<c>C4Disl</c>) does not exist yet; the fallback serves until it does.
/// </para>
/// </remarks>
internal static class C4Definitions
{
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
        DislPluginFunctions.None,
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
}
