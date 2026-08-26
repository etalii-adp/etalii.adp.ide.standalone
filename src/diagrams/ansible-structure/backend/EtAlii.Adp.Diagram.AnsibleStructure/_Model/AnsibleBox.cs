namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Where a node sits and how big it is, in canvas units. The client draws the box the backend
/// computed rather than re-measuring: a client-side guess at the size is what made wide nodes
/// overlap their neighbours in the mindmap module, and the lesson transfers.
/// </summary>
public readonly record struct AnsibleBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>Whether two boxes share any area at all - what "no overlap" is tested with.</summary>
    public bool Intersects(AnsibleBox other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}
