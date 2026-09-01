namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// One line of a <see cref="C4Document"/> as it was read: its text, and the terminator that
/// followed it. Empty for the last line of a file that ends without one.
/// </summary>
/// <remarks>
/// The terminator is kept per line rather than per document because a document edited on two
/// platforms carries both, and rewriting every line to the prevailing one makes the first
/// save a whole-file diff - exactly the diff Requirement 3.1 forbids.
/// </remarks>
internal readonly record struct C4DocumentLine(string Text, string Terminator);
