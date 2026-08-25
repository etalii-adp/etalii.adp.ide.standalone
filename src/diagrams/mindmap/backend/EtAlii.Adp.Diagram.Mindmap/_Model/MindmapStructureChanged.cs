namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>Nodes appeared, moved or disappeared; positions of others may have shifted with them.</summary>
public sealed record MindmapStructureChanged(IReadOnlyList<string> RemovedNodeIds) : MindmapChange
{
    /// <summary>A structural change that removed nothing - the common case for an add or a move.</summary>
    public static MindmapStructureChanged Nothing { get; } = new([]);
}
