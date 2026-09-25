using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Sets what an element says: the Name of the four named types, or a Comment's text. Both arrive from
/// one inline editor, so one command answers both.
/// </summary>
public sealed record RenameFdgElementCommand(string BodyPath, string ElementId, string Value) : ICommand;
