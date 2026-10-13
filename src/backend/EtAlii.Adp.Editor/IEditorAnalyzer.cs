namespace EtAlii.Adp.Editor;

/// <summary>
/// What a module answers when its editor is asked about the text as typed: findings and a
/// status line, and what it offers at a position (ruling A1 of the text editors' design). Keyed
/// by the editor's id, as <see cref="IEditorSessionFactory"/> is. The text, the offsets and the
/// sentences cross this seam; what the text is in, the module alone knows.
/// </summary>
public interface IEditorAnalyzer
{
    /// <summary>The <see cref="EditorDefinition.Id"/> this analyser answers for.</summary>
    string EditorId { get; }

    /// <summary>
    /// The findings for <paramref name="content"/>, the file at <paramref name="filePath"/> as
    /// the user has typed it, inside the project at <paramref name="rootPath"/>.
    /// </summary>
    Task<TextAnalysis> AnalyzeAsync(string rootPath, string filePath, string content, CancellationToken cancellationToken);

    /// <summary>
    /// What is offered at <paramref name="offset"/>, a UTF-16 offset into
    /// <paramref name="content"/>. <paramref name="isExplicit"/> says the user asked rather
    /// than typed.
    /// </summary>
    Task<IReadOnlyList<TextCompletion>> CompleteAsync(string rootPath, string filePath, string content, int offset, bool isExplicit, CancellationToken cancellationToken);
}
