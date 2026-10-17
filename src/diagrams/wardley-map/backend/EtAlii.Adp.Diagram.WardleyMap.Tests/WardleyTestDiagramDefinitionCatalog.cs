namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The diagram definitions a test wants the router to see, rather than whatever the running
/// process happened to discover.
/// </summary>
/// <remarks>
/// A copy of the shared stub in <c>EtAlii.Adp.Backend.Tests</c> rather than a reference to it:
/// test projects do not reference one another, and one small internal class is a better price
/// than a project reference that exists only to share it.
/// </remarks>
internal sealed class WardleyTestDiagramDefinitionCatalog(params DiagramDefinition[] definitions) : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All { get; } = definitions;
}
