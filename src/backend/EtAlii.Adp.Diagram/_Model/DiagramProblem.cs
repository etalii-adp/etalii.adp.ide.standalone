namespace EtAlii.Adp.Diagram;

/// <summary>
/// One thing wrong with one diagram document, as its type's <see cref="IDiagramValidator"/>
/// reports it. Where it sits in the project - which file, how stale - is core's bookkeeping,
/// added around this record rather than inside it.
/// </summary>
/// <param name="Severity">Whether this blocks correctness or merely deserves attention.</param>
/// <param name="Message">What is wrong, in the user's terms.</param>
/// <param name="RuleId">
/// Stable identifier of the rule that fired, prefixed with its module's short name
/// (e.g. <c>mindmap.root-missing</c>) - core's own routing problems use <c>core.</c>.
/// </param>
/// <param name="Location">
/// Where in the document, when the rule can say - an element or a line. Null means the file
/// itself.
/// </param>
public sealed record DiagramProblem(
    DiagramProblemSeverity Severity,
    string Message,
    string RuleId,
    DiagramProblemLocation? Location = null);
