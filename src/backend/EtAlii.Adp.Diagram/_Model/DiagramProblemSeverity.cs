namespace EtAlii.Adp.Diagram;

/// <summary>
/// How bad a <see cref="DiagramProblem"/> is. Two levels only, matching what the panel
/// counts and filters - anything informational is simply not a problem.
/// </summary>
public enum DiagramProblemSeverity
{
    /// <summary>Worth attention, but the document still means something.</summary>
    Warning,

    /// <summary>The document is wrong: it cannot be trusted until this is fixed.</summary>
    Error,
}
