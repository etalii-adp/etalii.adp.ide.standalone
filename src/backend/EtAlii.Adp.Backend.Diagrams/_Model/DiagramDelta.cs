namespace EtAlii.Adp.Backend.Diagrams;

/// <summary>
/// One change to a diagram, in the core add/remove/group/ungroup vocabulary but as backend
/// records rather than proto - so a module builds them without depending on the generated
/// contract, and the core service maps them to <c>Delta</c> messages in one place. Elements
/// are carried opaquely: the service knows an id, a position, a mime type and a payload it
/// never reads.
/// </summary>
/// <remarks>A closed set: the four <c>Diagram*Delta</c> records declared beside this one.</remarks>
public abstract record DiagramDelta;
