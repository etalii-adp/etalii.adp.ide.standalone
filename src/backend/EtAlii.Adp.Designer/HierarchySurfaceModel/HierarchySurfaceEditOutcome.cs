namespace EtAlii.Adp.Designer;

/// <summary>What became of an accepted gesture.</summary>
/// <param name="EditId">The client's id of the gesture.</param>
/// <param name="Written">Whether it was written.</param>
/// <param name="Error">Why not, when it was not.</param>
public sealed record HierarchySurfaceEditOutcome(ShortGuid EditId, bool Written, string Error = "");
