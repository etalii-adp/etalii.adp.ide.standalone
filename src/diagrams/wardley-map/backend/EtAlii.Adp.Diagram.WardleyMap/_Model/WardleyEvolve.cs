namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// An `evolve` statement: where a component is heading on the evolution axis (Requirement 6.1).
/// </summary>
/// <param name="Name">The component being evolved, by the name it currently has.</param>
/// <param name="Maturity">The target position on the evolution axis.</param>
/// <param name="Line">The 1-based line that declares it.</param>
/// <param name="Override">
/// The name the component takes when it arrives, from the `evolve Name-&gt;NewName x` form.
/// Empty when the component keeps its name. The reference parser calls this field `override`,
/// and the word is kept here so the two are recognisably the same thing.
/// </param>
/// <remarks>
/// The component is shown at <b>both</b> positions joined by a movement indicator, because the
/// pair is the point of the statement - a component's current maturity and where its owner
/// believes it is going. Rendering only the target would throw away half of what was said.
/// </remarks>
public sealed record WardleyEvolve(string Name, double Maturity, uint Line, string Override = "");
