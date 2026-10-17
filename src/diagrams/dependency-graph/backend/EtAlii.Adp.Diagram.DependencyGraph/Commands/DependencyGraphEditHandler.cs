using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// A handler of a command that edits a dependency graph: the shared run, which edits a copy, saves
/// it and carries a warning of the save to the result (code-structure FR-033), with the DISL model
/// the body's binding reads.
/// </summary>
/// <remarks>
/// Every edit is a change the binding plans as splices (<see cref="DependencyGraphBody"/>), and its undo
/// is the host's shared restore command, which puts the text back as it was before the edit.
/// </remarks>
/// <typeparam name="TCommand">The command handled.</typeparam>
public abstract class DependencyGraphEditHandler<TCommand>(IDependencyGraphDocumentStore documents)
    : DocumentEditHandler<TCommand, IDependencyGraphDocumentStore, DependencyGraphBody, DislModel>(documents)
    where TCommand : IDocumentEditCommand
{
    /// <inheritdoc />
    protected sealed override DislModel Parse(DependencyGraphBody body) => body.Disl;
}
