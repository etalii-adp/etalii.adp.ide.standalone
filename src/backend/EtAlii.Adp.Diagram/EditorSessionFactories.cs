using EtAlii.Adp.Editor;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The registered editor session factories, keyed by the editor id they serve - the editor
/// family's mirror of <see cref="DiagramSessionFactories"/>. A module registers its factory
/// through its definition's <c>Build</c> delegate; core resolves and never names a module.
/// </summary>
public sealed class EditorSessionFactories
{
    private readonly IReadOnlyDictionary<string, IEditorSessionFactory> _byId;

    public EditorSessionFactories(IEnumerable<IEditorSessionFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        _byId = factories.ToDictionary(factory => factory.EditorId, StringComparer.Ordinal);
    }

    public IEditorSessionFactory? Find(string editorId) =>
        _byId.GetValueOrDefault(editorId);
}
