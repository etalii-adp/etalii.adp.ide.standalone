namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// A <c>.mm</c> file that cannot be read as a Freeplane map. Carries a message fit to show
/// for that one diagram; it never takes the workspace down (Requirement 3.8).
/// </summary>
public sealed class MindmapFormatException : Exception
{
    public MindmapFormatException(string message)
        : base(message)
    {
    }

    public MindmapFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
