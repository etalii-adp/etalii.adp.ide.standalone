namespace EtAlii.Adp.Documents;

/// <summary>Something a parser passed over or could not read, and where.</summary>
/// <param name="Line">The zero-based line it was found on.</param>
/// <param name="Message">What could not be read, in a sentence meant for the problems panel.</param>
public sealed record ParseProblem(int Line, string Message);
