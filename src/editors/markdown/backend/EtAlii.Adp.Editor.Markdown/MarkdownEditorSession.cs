using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Editor.Markdown;

/// <summary>
/// One open markdown file: a <see cref="TextEditorSession"/>, everything the plain editor does
/// (Requirement 10.2). What makes markdown worth its own module - preview, heading navigation - is
/// client-side; this session deliberately adds nothing to the family's backend contract, which is
/// Requirement 10.3's whole point. A type of its own so its records name markdown.
/// </summary>
public sealed class MarkdownEditorSession(string path, ProjectFileEvents? events = null) : TextEditorSession(path, events);
