using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Adds an empty note where the toolbox's Note was dropped (Requirement 7.1).</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="X">The drop's x. The note's left edge is the start of the step it falls in.</param>
/// <param name="Y">The drop's y. The note's top is the top of the row it falls in, so the drop is inside it.</param>
/// <param name="NoteId">Empty to have one minted, once, as for <see cref="AddGhgTrendCommand"/>.</param>
public sealed record AddGhgNoteCommand(string BodyPath, double X, double Y, string NoteId = "") : ICommand;
