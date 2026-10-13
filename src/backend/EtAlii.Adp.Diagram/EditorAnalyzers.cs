using EtAlii.Adp.Editor;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The registered editor analysers, keyed by the editor id they answer for - shaped as
/// <see cref="EditorSessionFactories"/>. A module registers its analyser through its
/// definition's <c>Build</c> delegate; an editor without one has no analysis, which is an
/// answer rather than a fault.
/// </summary>
public sealed class EditorAnalyzers
{
    private readonly IReadOnlyDictionary<string, IEditorAnalyzer> _byId;

    public EditorAnalyzers(IEnumerable<IEditorAnalyzer> analyzers)
    {
        ArgumentNullException.ThrowIfNull(analyzers);
        _byId = analyzers.ToDictionary(analyzer => analyzer.EditorId, StringComparer.Ordinal);
    }

    public IEditorAnalyzer? Find(string editorId) =>
        _byId.GetValueOrDefault(editorId);
}
