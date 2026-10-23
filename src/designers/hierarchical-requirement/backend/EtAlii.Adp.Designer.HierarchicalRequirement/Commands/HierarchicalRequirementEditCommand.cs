using EtAlii.Adp.History;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// One edit of a requirement document's body: an operation of the surface, with what it names and its params, planned
/// against the bytes the person's page was made from. Plain data, so the history can run it again to redo it.
/// </summary>
/// <param name="BodyPath">The Markdown file.</param>
/// <param name="Operation">The operation's name in the specification, <c>setTitle</c> say.</param>
/// <param name="TargetId">The id the surface gives the entry it is for.</param>
/// <param name="Params">Its params, in their written form: the value a text operation sets, the entries a move names by the surface's ids.</param>
/// <param name="Expected">
/// The body's bytes as the page was read from them. A file that holds other bytes now was changed since, and the edit is
/// refused rather than made to a file the person did not see (FBL 0.7 section 7.2, spec 029 FR-024).
/// </param>
internal sealed record HierarchicalRequirementEditCommand(string BodyPath, string Operation, string TargetId, IReadOnlyDictionary<string, string> Params, byte[] Expected) : ICommand;
