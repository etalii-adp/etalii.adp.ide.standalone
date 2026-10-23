namespace EtAlii.Adp.Designer;

/// <summary>The role a node of a hierarchy surface plays (DESL section 7.7.1): a level holds further nodes, an entry holds none.</summary>
public static class HierarchySurfaceRoles
{
    /// <summary>A type the surface's <c>levels</c> names, drawn as a box with what it holds to its right.</summary>
    public const string Level = "level";

    /// <summary>A type the surface's <c>entries</c> names, drawn inside the box of its level.</summary>
    public const string Entry = "entry";
}
