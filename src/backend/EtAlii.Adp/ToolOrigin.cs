namespace EtAlii.Adp;

/// <summary>
/// A tool type's origin, for all three kinds (code-structure FR-060), expressed the same
/// MIME-type-style way as docs/tools.md's own Origin column: <c>vendor/type</c>, with an optional
/// finer <c>subtype</c> for a type that needs one - identifying which architecture/vendor notation it
/// belongs to. A diagram's origin is declared as one; a designer's <c>vendor/type</c> string is read
/// into one once, by its definition; an editor's is <c>editor/&lt;id&gt;</c>, as the catalog tags it.
/// </summary>
public sealed record ToolOrigin(string Vendor, string Type, string Subtype = "")
{
    /// <summary>
    /// The origin as docs/tools.md writes it: <c>vendor/type</c>, or
    /// <c>vendor/type/subtype</c> when there is a subtype. Unique per discovered tool type, so it
    /// doubles as the identifier a dialog hands back to name a choice.
    /// </summary>
    public string Key => Subtype.Length == 0 ? $"{Vendor}/{Type}" : $"{Vendor}/{Type}/{Subtype}";

    /// <summary>
    /// This tool type as a MIME type - what a created <c>.adp</c> file states on its first
    /// line. Distinct from <see cref="Key"/>, which identifies a choice inside ADP and may take
    /// a shape MIME does not allow: a MIME type has exactly one slash, so a subtype is appended
    /// with <c>+</c>, the structured-suffix convention MIME already uses (<c>svg+xml</c>,
    /// <c>ld+json</c>). Without a subtype the two are identical, and both match the Origin
    /// column docs/tools.md carries.
    /// </summary>
    public string MimeType => Subtype.Length == 0 ? $"{Vendor}/{Type}" : $"{Vendor}/{Type}+{Subtype}";

    /// <summary>
    /// The origin a <see cref="Key"/> names - <c>vendor/type</c> or <c>vendor/type/subtype</c>, every
    /// part non-empty - or null for a text that names none.
    /// </summary>
    public static ToolOrigin? TryParse(string? key) => key?.Split('/') switch
    {
        [{ Length: > 0 } vendor, { Length: > 0 } type] => new ToolOrigin(vendor, type),
        [{ Length: > 0 } vendor, { Length: > 0 } type, { Length: > 0 } subtype] => new ToolOrigin(vendor, type, subtype),
        _ => null,
    };

    public override string ToString() => Key;
}
