namespace EtAlii.Adp.Diagram;

/// <summary>
/// Where a <see cref="DiagramProblem"/> sits, when its rule can say: a diagram element for a
/// rule that judged the model, a line for one that judged the text, or another file entirely
/// for a rule whose diagram is a folder of them. A problem with none of the three carries no
/// location at all - the diagram's own file is the subject.
/// </summary>
/// <remarks>
/// The constructor is protected rather than private: the three cases live beside this record
/// rather than inside it (tech.md's no-nested-types rule), so they cannot reach a private
/// one. The set stays closed by convention and by the three records being sealed.
/// </remarks>
public abstract record DiagramProblemLocation;
