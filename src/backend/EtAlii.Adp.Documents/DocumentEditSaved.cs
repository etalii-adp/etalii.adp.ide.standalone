namespace EtAlii.Adp.Documents;

/// <summary>What an edit saved against the file as it is now did: its result, and the text before and after it, for the undo that reverses it.</summary>
/// <param name="Result">Whether the edit was written, and the failure or the edit's refusal when it was not.</param>
/// <param name="Before">The text the edit was applied to: the file as it was when the edit ran, which may be newer than what the caller read.</param>
/// <param name="After">The text the edit left; <paramref name="Before"/> when it was refused.</param>
public sealed record DocumentEditSaved(DocumentSaveResult Result, string Before, string After);
