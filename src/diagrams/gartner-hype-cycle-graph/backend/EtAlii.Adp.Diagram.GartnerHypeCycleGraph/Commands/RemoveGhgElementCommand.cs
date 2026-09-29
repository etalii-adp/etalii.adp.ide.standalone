using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Removes a trend, a trigger or a note. A trend or trigger takes every influence touching it, in one
/// edit, so one undo restores them all (Requirement 3.5); a note takes nothing with it.
/// </summary>
public sealed record RemoveGhgElementCommand(string BodyPath, string ElementId) : ICommand;
