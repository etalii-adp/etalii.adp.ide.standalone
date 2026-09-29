namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>Where the layout put one node, and how big it measured it.</summary>
/// <remarks>
/// The mapper's input shape: computed by <c>HelmLayout</c>, overlaid with authored positions
/// by the session before mapping, so the mapper never knows which positions were computed and
/// which were dragged - it draws what it is handed.
/// </remarks>
public readonly record struct HelmBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}
