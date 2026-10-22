using EtAlii.Adp.History;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Creates a designer's document as its body alone, <paramref name="FileName"/> in <paramref name="Folder"/> holding
/// <paramref name="Content"/>: for a type that creates its registration with the first setting the user changes (FBL
/// section 8.4, spec 029 FR-005).
/// </summary>
/// <param name="Folder">Absolute path of the folder the file belongs in.</param>
/// <param name="FileName">The body's name, extension included.</param>
/// <param name="Content">The body's complete initial content.</param>
public sealed record CreateDocumentBodyCommand(string Folder, string FileName, string Content) : ICommand;
