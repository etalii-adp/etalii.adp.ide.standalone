namespace EtAlii.Adp.Documents;

/// <summary>Where a terminal <see cref="TerminalScanner"/> read ends, or why it could not be closed.</summary>
/// <param name="End">The offset just past the terminal, when <paramref name="Problem"/> is <see cref="TerminalProblem.None"/>.</param>
/// <param name="Problem">Why the terminal did not close, or <see cref="TerminalProblem.None"/>.</param>
public readonly record struct TerminalScan(int End, TerminalProblem Problem);
