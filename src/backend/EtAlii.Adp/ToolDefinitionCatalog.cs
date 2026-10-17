namespace EtAlii.Adp;

/// <summary>
/// The discovered definitions of one tool kind, as a service rather than a static cache, so code
/// that looks a tool type up - routing a file, offering a new document, opening with an editor - can
/// be handed a test-controlled list (code-structure FR-057). Each kind names its own catalog by
/// deriving from this one, so a subject asks for the kind it means.
/// </summary>
public interface IToolDefinitionCatalog<out T> where T : IToolDefinition
{
    IReadOnlyList<T> All { get; }
}

/// <summary>The production catalog of one tool kind: the definitions the host discovered at startup.</summary>
public class ToolDefinitionCatalog<T> : IToolDefinitionCatalog<T> where T : IToolDefinition
{
    public required IReadOnlyList<T> All { get; init; }
}
