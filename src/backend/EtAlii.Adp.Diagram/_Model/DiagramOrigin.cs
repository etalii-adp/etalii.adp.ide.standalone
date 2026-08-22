namespace EtAlii.Adp.Diagram;

/// <summary>
/// A diagram type's origin, expressed the same MIME-type-style way as docs/diagrams.md's own
/// Origin column: <c>vendor/type</c>, with an optional finer <c>subtype</c> for a diagram type
/// that needs one - identifying which architecture/vendor notation it belongs to.
/// </summary>
public sealed record DiagramOrigin(string Vendor, string Type, string Subtype = "")
{
    /// <summary>
    /// The origin as docs/diagrams.md writes it: <c>vendor/type</c>, or
    /// <c>vendor/type/subtype</c> when there is a subtype. Unique per discovered diagram
    /// type, so it doubles as the identifier a dialog hands back to name a choice.
    /// </summary>
    public string Key => Subtype.Length == 0 ? $"{Vendor}/{Type}" : $"{Vendor}/{Type}/{Subtype}";

    public override string ToString() => Key;
}
