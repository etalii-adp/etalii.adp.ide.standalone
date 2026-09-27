using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Renames a trend or a trigger, or rewrites a note's text, from the inline editor or the grid - one
/// command for both (Requirement 2.6, 4.2).
/// </summary>
/// <param name="BodyPath">The document.</param>
/// <param name="ElementId">The trend, trigger or note.</param>
/// <param name="Name">The new name, or a note's new text, line breaks and all.</param>
public sealed record RenameGhgElementCommand(string BodyPath, string ElementId, string Name) : ICommand;
