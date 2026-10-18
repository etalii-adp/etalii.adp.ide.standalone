namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// What a C4 view offers in the Toolbox: exactly the element kinds that view permits, and
/// nothing it would then refuse. Described by the backend as data - the client renders a
/// palette it does not interpret (c4-diagrams Requirement 12).
/// </summary>
/// <remarks>
/// One provider per type, because the permitted kinds are what differ between the six that draw a
/// view. The palette is derived from the type's bundled DISL definition (<see cref="C4Definitions.Toolbox"/>),
/// its toolbox mapped onto today's wire ids by its <c>x-c4</c> block (c4-disl-fbl Requirement 3.1).
/// </remarks>
public sealed class C4ToolboxProvider : IDiagramToolboxProvider
{
    /// <summary>The palette of the C4 type <paramref name="origin"/> names, derived from its definition.</summary>
    /// <exception cref="KeyNotFoundException">The type has no definition, as <c>c4/code</c> has none.</exception>
    public C4ToolboxProvider(ToolOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        Origin = origin;
        Items = C4Definitions.Toolbox(origin);
    }

    public ToolOrigin Origin { get; }

    public IReadOnlyList<ToolboxItemDefinition> Items { get; }
}
