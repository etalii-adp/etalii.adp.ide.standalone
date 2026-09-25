using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.Tests;

internal sealed class DiagramDocumentFactoriesStubFactory(DiagramOrigin origin) : IDiagramDocumentFactory
{
    public DiagramOrigin Origin { get; } = origin;

    public string CreateEmptyDocument(string baseName) => "";
}
