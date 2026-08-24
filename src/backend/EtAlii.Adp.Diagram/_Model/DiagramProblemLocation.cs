namespace EtAlii.Adp.Diagram;

/// <summary>
/// Where in a document a <see cref="DiagramProblem"/> sits, when its rule can say: a diagram
/// element for a rule that judged the model, a line for one that judged the text. A problem
/// with neither carries no location at all - the file itself is the subject.
/// </summary>
public abstract record DiagramProblemLocation
{
    private DiagramProblemLocation() { }

    /// <summary>The problem sits on one diagram element, named by the module's own stable element id.</summary>
    public sealed record ElementId(string Id) : DiagramProblemLocation;

    /// <summary>The problem sits on one line of the document text, 1-based.</summary>
    public sealed record Line(uint Number) : DiagramProblemLocation;
}
