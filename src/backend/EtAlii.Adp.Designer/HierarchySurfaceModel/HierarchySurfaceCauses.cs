namespace EtAlii.Adp.Designer;

/// <summary>What a <see cref="HierarchySurfaceModel"/> follows, so the client knows whether the file changed under an open field (spec 029 R10, R11).</summary>
public static class HierarchySurfaceCauses
{
    /// <summary>The document was opened.</summary>
    public const string Open = "open";

    /// <summary>A gesture of this connection's person was written.</summary>
    public const string Edit = "edit";

    /// <summary>The file was changed by another program, or by another connection.</summary>
    public const string Outside = "outside";
}
