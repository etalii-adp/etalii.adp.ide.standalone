namespace EtAlii.Adp.Diagram;

/// <summary>
/// Where in a document a <see cref="DiagramProblem"/> sits, when its rule can say: a diagram
/// element for a rule that judged the model, a line for one that judged the text. A problem
/// with neither carries no location at all - the file itself is the subject.
/// </summary>
/// <remarks>
/// The constructor is protected rather than private: the two cases live beside this record
/// rather than inside it (tech.md's no-nested-types rule), so they cannot reach a private
/// one. The set stays closed by convention and by the two records being sealed.
/// </remarks>
public abstract record DiagramProblemLocation
{
    protected DiagramProblemLocation() { }
}
