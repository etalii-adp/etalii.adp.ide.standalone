namespace EtAlii.Adp.Documents;

/// <summary>
/// A body that is read from its text and written back as its text: what an edit copies before it
/// changes anything (code-structure FR-033).
/// </summary>
/// <remarks>
/// <b>The copy is the point.</b> An edit is made on a copy parsed from the cached body's text, so a
/// refusal halfway through leaves nothing behind in memory for the next save to write, and the text
/// before the edit is what its undo restores.
/// </remarks>
/// <typeparam name="TSelf">The body type itself.</typeparam>
public interface ITextBody<TSelf>
    where TSelf : ITextBody<TSelf>
{
    /// <summary>The body as it is written to its file.</summary>
    string Text { get; }

    /// <summary>A body read from <paramref name="text"/>.</summary>
    static abstract TSelf Parse(string text);
}
