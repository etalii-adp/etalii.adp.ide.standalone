namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// One of the four evolution stages, with the maturity range it covers.
/// </summary>
/// <param name="Label">What the axis shows, including the parenthetical the notation uses.</param>
/// <param name="Start">Inclusive lower bound on the maturity axis.</param>
/// <param name="End">Exclusive upper bound, except for Commodity where 1.0 is included.</param>
public sealed record WardleyEvolutionStage(string Label, double Start, double End);
