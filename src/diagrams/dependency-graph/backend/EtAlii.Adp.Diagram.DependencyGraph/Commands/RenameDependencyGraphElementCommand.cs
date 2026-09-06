using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Renames one node - one line rewritten, identity untouched, so the selection, the history
/// and every pushed element id survive it. The benefit the design attributes to owning the
/// schema: the id is in the file, and a rename has no reason to go near it.
/// </summary>
/// <param name="BodyPath">The document.</param>
/// <param name="ElementId">Which node.</param>
/// <param name="Label">The new label.</param>
public sealed record RenameDependencyGraphElementCommand(
    string BodyPath,
    string ElementId,
    string Label) : ICommand;
