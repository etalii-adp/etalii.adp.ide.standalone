namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>Where a variable sits and how big it is, in canvas units.</summary>
public readonly record struct CausalLoopBox(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>Whether this box and <paramref name="other"/> share any area.</summary>
    public bool Overlaps(CausalLoopBox other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}
