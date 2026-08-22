namespace EtAlii.Adp.Diagram;

/// <summary>
/// A diagram type's origin, expressed the same MIME-type-style way as docs/diagrams.md's own
/// Origin column: <c>vendor/type</c>, with an optional finer <c>subtype</c> for a diagram type
/// that needs one - identifying which architecture/vendor notation it belongs to.
/// </summary>
public sealed record DiagramOrigin(string Vendor, string Type, string Subtype = "");
