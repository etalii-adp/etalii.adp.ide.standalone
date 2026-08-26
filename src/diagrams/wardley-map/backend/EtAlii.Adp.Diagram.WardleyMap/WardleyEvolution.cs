namespace EtAlii.Adp.Diagram.WardleyMap;

// ReSharper disable once InvalidXmlDocComment
/// <summary>
/// The evolution axis: four stages, three boundaries, and the reading of a maturity value
/// against them (Requirement 8.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Where these numbers come from, because nobody should re-derive them from a search
/// result.</b> They are not published in the OnlineWardleyMaps DSL documentation. They are the
/// reference renderer's own <c>EvoOffsets</c> - <c>custom: 3.5</c>, <c>product: 8</c>,
/// <c>commodity: 14</c> - each divided by the axis width of 20. That gives 0.175, 0.400 and
/// 0.700. <see cref="WardleyEvolutionTests"/> asserts the division rather than the literals, so
/// the derivation is what is pinned rather than three magic numbers.
/// </para>
/// <para>
/// This is the only place in the module - or the client - that knows them. The client is sent
/// <see cref="Stages"/> as a map-level element and draws the bands it is told about, so there
/// is no second copy to drift (design, "The evolution constants are sent as data").
/// </para>
/// </remarks>
public static class WardleyEvolution
{
    /// <summary>The reference renderer's axis width, which its offsets are expressed against.</summary>
    private const double AxisWidth = 20d;

    /// <summary>Where Genesis ends and Custom Built begins: <c>EvoOffsets.custom / 20</c>.</summary>
    public const double CustomBuilt = 3.5d / AxisWidth;

    /// <summary>Where Custom Built ends and Product begins: <c>EvoOffsets.product / 20</c>.</summary>
    public const double Product = 8d / AxisWidth;

    /// <summary>Where Product ends and Commodity begins: <c>EvoOffsets.commodity / 20</c>.</summary>
    public const double Commodity = 14d / AxisWidth;

    /// <summary>
    /// The four stages in axis order, genesis at the left. The labels carry the parentheticals
    /// the notation itself uses, because "Product" without "(+rental)" is a different claim.
    /// </summary>
    public static IReadOnlyList<WardleyEvolutionStage> Stages { get; } =
    [
        new("Genesis", 0d, CustomBuilt),
        new("Custom Built", CustomBuilt, Product),
        new("Product (+rental)", Product, Commodity),
        new("Commodity (+utility)", Commodity, 1d),
    ];

    /// <summary>
    /// The stage <paramref name="maturity"/> falls in. A value exactly on a boundary belongs to
    /// the stage it starts, so 0.4 is Product rather than Custom Built.
    /// </summary>
    /// <remarks>
    /// A maturity outside <c>0..1</c> is a document error rather than this function's problem -
    /// Requirement 14.3 reports it - so this clamps rather than throwing, and a map with a bad
    /// coordinate still renders with the rest of it intact (Requirement 3.5).
    /// </remarks>
    public static WardleyEvolutionStage StageOf(double maturity)
    {
        if (maturity < CustomBuilt)
        {
            return Stages[0];
        }

        if (maturity < Product)
        {
            return Stages[1];
        }

        return maturity < Commodity ? Stages[2] : Stages[3];
    }
}
