using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.Tests;

internal sealed class DiagramDocumentFactoriesStubFactory(ToolOrigin origin) : IDiagramDocumentFactory
{
    public ToolOrigin Origin { get; } = origin;

    public string CreateEmptyDocument(string baseName) => "";
}
