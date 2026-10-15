namespace EtAlii.Adp.Documents;

/// <summary>How a double-quoted string in a line-based language writes a quote of its own.</summary>
public enum EscapePolicy
{
    /// <summary>It cannot: a quote always ends the string, and a backslash is text.</summary>
    None,

    /// <summary><c>\"</c> is a quote inside the string; every other backslash is text.</summary>
    BackslashQuote,
}
