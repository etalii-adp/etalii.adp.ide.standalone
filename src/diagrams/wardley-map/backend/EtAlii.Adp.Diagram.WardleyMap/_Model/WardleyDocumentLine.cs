namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// One line as it was read: its text, and the terminator that followed it.
/// </summary>
/// <param name="Text">The line without its terminator.</param>
/// <param name="Terminator">
/// <c>"\r\n"</c>, <c>"\n"</c>, or empty for a final line the file did not terminate. Carried
/// per line rather than once per document because Requirement 3.1's byte-identical promise has
/// no "unless the file mixes them" clause - and a file edited on two platforms, or merged from
/// two branches, mixes them routinely.
/// </param>
public sealed record WardleyDocumentLine(string Text, string Terminator);
