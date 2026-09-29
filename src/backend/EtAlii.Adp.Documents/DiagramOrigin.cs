namespace EtAlii.Adp.Documents;

/// <summary>
/// A diagram type's origin, expressed the same MIME-type-style way as docs/tools.md's own
/// Origin column: <c>vendor/type</c>, with an optional finer <c>subtype</c> for a diagram type
/// that needs one - identifying which architecture/vendor notation it belongs to.
/// </summary>
public sealed record DiagramOrigin(string Vendor, string Type, string Subtype = "")
{
    /// <summary>
    /// The origin as docs/tools.md writes it: <c>vendor/type</c>, or
    /// <c>vendor/type/subtype</c> when there is a subtype. Unique per discovered diagram
    /// type, so it doubles as the identifier a dialog hands back to name a choice.
    /// </summary>
    public string Key => Subtype.Length == 0 ? $"{Vendor}/{Type}" : $"{Vendor}/{Type}/{Subtype}";

    /// <summary>
    /// This diagram type as a MIME type - what a created <c>.adp</c> file states on its first
    /// line. Distinct from <see cref="Key"/>, which identifies a choice inside ADP and may take
    /// a shape MIME does not allow: a MIME type has exactly one slash, so a subtype is appended
    /// with <c>+</c>, the structured-suffix convention MIME already uses (<c>svg+xml</c>,
    /// <c>ld+json</c>). Without a subtype the two are identical, and both match the Origin
    /// column docs/tools.md carries.
    /// </summary>
    public string MimeType => Subtype.Length == 0 ? $"{Vendor}/{Type}" : $"{Vendor}/{Type}+{Subtype}";

    public override string ToString() => Key;
}
