namespace EtAlii.Adp.Designer;

/// <summary>A stored filter.</summary>
/// <param name="Id">What a gesture names it by.</param>
/// <param name="Query">Its words, in DESL section 7.7.3's notation.</param>
public sealed record HierarchySurfaceFilter(string Id, string Name, string Query);
