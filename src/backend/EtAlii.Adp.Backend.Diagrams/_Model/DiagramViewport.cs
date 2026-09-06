namespace EtAlii.Adp.Backend.Diagrams;

/// <summary>The visible rectangle a connection reported, in the diagram's own units.</summary>
public readonly record struct DiagramViewport(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>The "everything is in view" viewport a connection has before it reports one.</summary>
    public static DiagramViewport Unbounded { get; } = new(
        double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity);
}
