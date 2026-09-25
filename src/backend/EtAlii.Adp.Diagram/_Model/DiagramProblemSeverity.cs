namespace EtAlii.Adp.Diagram;

/// <summary>
/// How bad a <see cref="DiagramProblem"/> is: informational, worth attention, or wrong.
/// </summary>
/// <remarks>
/// <para>
/// <b>The values are pinned deliberately, and Info is negative for a reason.</b>
/// <c>ProblemStore</c> caches problems to disk as JSON with no string-enum converter, so a
/// severity persists as its NUMBER. Adding Info as the first member would have renumbered
/// Warning 0-&gt;1 and Error 1-&gt;2, and every cache file written before that change would have
/// read back one level too low - every stored Error silently becoming a Warning on somebody's
/// machine. Pinning Info at -1 keeps the two existing values exactly where the caches already
/// have them while still ordering the three correctly.
/// </para>
/// <para>
/// The ordering is load-bearing rather than cosmetic: several specs measure their examples as
/// "zero findings above info level", which is the comparison <c>severity &gt; Info</c>. Adding a
/// level below Warning is additive for every existing call site - code that reports a Warning
/// or an Error means exactly what it meant before.
/// </para>
/// </remarks>
public enum DiagramProblemSeverity
{
    /// <summary>
    /// Worth saying, but nothing is wrong: a term this file does not describe and another
    /// probably does, a truncated view, a deliberate omission. Not counted as a problem.
    /// </summary>
    Info = -1,

    /// <summary>Worth attention, but the document still means something.</summary>
    Warning = 0,

    /// <summary>The document is wrong: it cannot be trusted until this is fixed.</summary>
    Error = 1,
}
