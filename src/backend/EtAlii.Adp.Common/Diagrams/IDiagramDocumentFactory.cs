namespace EtAlii.Adp.Common;

/// <summary>
/// Produces the empty document body for one diagram type - the one thing core cannot derive
/// from a <see cref="DiagramDefinition"/> on its own. A type that declares an
/// <see cref="DiagramDefinition.Extension"/> registers exactly one of these; the shared
/// create-file command calls it and writes what it returns beside the <c>.adp</c> file,
/// without ever learning what the type is.
/// </summary>
/// <remarks>
/// Registered like <c>IContextActionProvider</c> and resolved by <see cref="Origin"/>. A
/// definition with an extension and no factory is a deployment error, and
/// <see cref="DiagramDocumentFactories.Verify"/> reports it at startup rather than at the
/// first Add.
/// </remarks>
public interface IDiagramDocumentFactory
{
    /// <summary>The diagram type whose empty body this produces.</summary>
    DiagramOrigin Origin { get; }

    /// <summary>
    /// The complete text of a new, empty document for a diagram whose file base name is
    /// <paramref name="baseName"/> - a mindmap's single root node is named after it.
    /// </summary>
    string CreateEmptyDocument(string baseName);
}
