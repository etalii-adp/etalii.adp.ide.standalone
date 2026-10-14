namespace EtAlii.Adp.Layout;

/// <summary>
/// Where an element sits and how big it is, in a canvas's own units: the one rectangle every layout
/// places with and every mapper tests against a viewport (code-structure FR-045).
/// </summary>
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>Whether this rectangle and <paramref name="other"/> share any area; touching edges share none.</summary>
    public bool Overlaps(Rect other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}
