namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>A node's own content changed - text, notes, link - and nothing moved.</summary>
public sealed record MindmapNodeUpdated(string NodeId) : MindmapChange;
