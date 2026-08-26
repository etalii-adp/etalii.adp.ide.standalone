namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// A position as the document writes it: `[visibility, maturity]` (Requirement 5.2).
/// </summary>
/// <param name="Visibility">
/// The value-chain axis, 1 at the user need and 0 at the invisible end. Rendered
/// <b>vertically</b>.
/// </param>
/// <param name="Maturity">
/// The evolution axis, 0 at genesis and 1 at commodity. Rendered <b>horizontally</b>.
/// </param>
/// <remarks>
/// Deliberately not a <c>Point2D</c>. The document's order is the reverse of the canvas's, and
/// the axes are transposed as well as inverted, so keeping the document's own vocabulary here
/// means the conversion has exactly one home - <c>WardleyElementMapper</c> - rather than being
/// a thing every reader of this record has to remember (task 11).
/// </remarks>
public sealed record WardleyCoordinate(double Visibility, double Maturity);
