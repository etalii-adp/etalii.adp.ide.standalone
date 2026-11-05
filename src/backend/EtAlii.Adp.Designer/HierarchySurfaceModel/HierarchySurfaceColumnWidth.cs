namespace EtAlii.Adp.Designer;

/// <summary>The width the user set of one column of levels (DESL section 7.7.1); a column without one has the host's.</summary>
/// <param name="Id">What a gesture names it by.</param>
/// <param name="Level">The depth of the levels the column holds, 1 for the top ones.</param>
/// <param name="Width">Its width in pixels.</param>
public sealed record HierarchySurfaceColumnWidth(string Id, int Level, int Width);
