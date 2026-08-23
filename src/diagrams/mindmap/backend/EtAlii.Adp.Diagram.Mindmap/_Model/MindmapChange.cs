namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// What a command did to a document, in the terms the delta mapper needs: which nodes to
/// re-send, which are gone. A structural change relays out the whole map, so it names
/// nothing and the mapper re-derives everything from the layout.
/// </summary>
public abstract record MindmapChange
{
    /// <summary>A node's own content changed - text, notes, link - and nothing moved.</summary>
    public sealed record NodeUpdated(string NodeId) : MindmapChange;

    /// <summary>Nodes appeared, moved or disappeared; positions of others may have shifted with them.</summary>
    public sealed record StructureChanged(IReadOnlyList<string> RemovedNodeIds) : MindmapChange
    {
        public static StructureChanged Nothing { get; } = new([]);
    }

    /// <summary>The file was reloaded from disk after an external edit; everything may differ.</summary>
    public sealed record Reloaded : MindmapChange;
}

public sealed class MindmapChangedEventArgs(string bodyPath, MindmapChange change) : EventArgs
{
    public string BodyPath { get; } = bodyPath;

    public MindmapChange Change { get; } = change;
}
