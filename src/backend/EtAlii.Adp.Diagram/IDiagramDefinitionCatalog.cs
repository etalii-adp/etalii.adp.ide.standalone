namespace EtAlii.Adp.Diagram;

/// <summary>
/// The discovered diagram types, as a service rather than the static
/// <see cref="DiagramDefinition.All"/>, so code that needs to look a type up - the create
/// and rename commands, the file router - can be handed a test-controlled list instead of
/// filling the process-wide cache.
/// </summary>
public interface IDiagramDefinitionCatalog
{
    IReadOnlyList<DiagramDefinition> All { get; }
}

/// <summary>The production catalog: <see cref="DiagramDefinition.All"/>, read at call time because the host fills it after the container is built.</summary>
public sealed class DiagramDefinitionCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => DiagramDefinition.All;
}
