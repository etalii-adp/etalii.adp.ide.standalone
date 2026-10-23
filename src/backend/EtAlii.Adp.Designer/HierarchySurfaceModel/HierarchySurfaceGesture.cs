namespace EtAlii.Adp.Designer;

/// <summary>
/// One gesture of the person on a hierarchy surface: an operation of the designer's specification, what it is for, and
/// its params. The host passes it through and knows none of the operations; the module maps it (FR-026, R8).
/// </summary>
/// <param name="Operation">The operation's name in the specification, <c>toggleCollapsed</c> say.</param>
/// <param name="TargetId">The id of what it is for, from the model it was made in; empty for one that is for the document.</param>
/// <param name="Params">Its params by their names in the specification, in their written form.</param>
public sealed record HierarchySurfaceGesture(string Operation, string TargetId, IReadOnlyDictionary<string, string> Params);
