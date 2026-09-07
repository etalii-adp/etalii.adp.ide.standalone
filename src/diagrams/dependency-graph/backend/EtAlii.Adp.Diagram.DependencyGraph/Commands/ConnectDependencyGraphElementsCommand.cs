

using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Declares that one node depends on another. A second relation between the same pair is
/// permitted - the notation carries a label per relation, and two kinds of dependency between
/// one pair are meaningful.
/// </summary>
/// <param name="BodyPath">The document.</param>
/// <param name="Id">The new relation's id, the caller's to supply so a redo reuses it.</param>
/// <param name="From">The dependent node's id.</param>
/// <param name="To">The dependency's id.</param>
/// <param name="Label">What the author calls it, or empty.</param>
public sealed record ConnectDependencyGraphElementsCommand(
    string BodyPath,
    string Id,
    string From,
    string To,
    string Label) : ICommand;
