namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>Carries one <see cref="MindmapChange"/> and the document body it happened to.</summary>
public sealed class MindmapChangedEventArgs(string bodyPath, MindmapChange change) : EventArgs
{
    public string BodyPath { get; } = bodyPath;

    public MindmapChange Change { get; } = change;
}
