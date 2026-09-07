namespace EtAlii.Adp.Problems;

/// <summary>
/// What one validation run produced, and how much ground it actually covered - so a caller
/// can tell "no problems in 40 files" from "no problems because nothing was reachable".
/// </summary>
/// <param name="Problems">Everything found wrong within the scope.</param>
/// <param name="FilesConsidered">How many files the walk routed - diagrams and near-diagrams alike.</param>
/// <param name="Skipped">
/// How many entries the walk could not judge - unreadable folders, reparse points leaving
/// the root. Each skip also produced its own core problem where one applies.
/// </param>
public sealed record ValidationOutcome(
    IReadOnlyList<StoredProblem> Problems,
    int FilesConsidered,
    int Skipped);
