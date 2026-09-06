using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>The C4 module's own definitions, without a host to run discovery.</summary>
internal sealed class C4ExamplesStubCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
