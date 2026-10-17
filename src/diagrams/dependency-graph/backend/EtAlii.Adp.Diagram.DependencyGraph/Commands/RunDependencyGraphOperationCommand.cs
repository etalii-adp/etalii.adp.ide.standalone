using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Writes what one of the definition's operations made - <c>addNodeHere</c>, <c>addRight</c>, <c>addBelow</c>,
/// or the <c>dependsOn</c> tool released on empty canvas - as the operation made it.
/// </summary>
/// <remarks>
/// The transaction is computed once, where the gesture happens, under ids minted there, so a redo writes the
/// same nodes and dependencies under the same ids.
/// </remarks>
/// <param name="BodyPath">The document to write to.</param>
/// <param name="Transaction">The operation's changes.</param>
public sealed record RunDependencyGraphOperationCommand(
    string BodyPath,
    DislTransaction Transaction) : IDocumentEditCommand;
