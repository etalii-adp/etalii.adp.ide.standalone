namespace EtAlii.Adp.Documents;

/// <summary>Why <see cref="TerminalScanner"/> could not close a terminal it started reading.</summary>
public enum TerminalProblem
{
    /// <summary>The terminal closed.</summary>
    None,

    /// <summary>A terminal that may not span lines reached a line break first.</summary>
    LineEnded,

    /// <summary>The text ended first.</summary>
    FileEnded,
}
