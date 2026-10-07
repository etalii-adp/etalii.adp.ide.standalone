namespace EtAlii.Adp.Specification.Cel;

/// <summary>The result of an evaluation that failed: a missing key, a type mismatch, a division by zero.</summary>
public sealed record CelError(string Message);
