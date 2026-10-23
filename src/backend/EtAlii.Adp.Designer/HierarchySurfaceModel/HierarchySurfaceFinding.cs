namespace EtAlii.Adp.Designer;

/// <summary>What reading reported at one place.</summary>
/// <param name="Code">Its code: <c>std.duplicateId</c>, <c>std.mentions</c>, <c>fbl.unknown-block</c> and so on.</param>
/// <param name="Severity"><c>error</c>, <c>warning</c> or <c>info</c>.</param>
/// <param name="NodeId">The node it stands at, or empty when it stands at none.</param>
/// <param name="Line">The 1-based line of the body, or of the registration for a finding of the registration.</param>
public sealed record HierarchySurfaceFinding(string Code, string Severity, string Message, string NodeId, int Line);
