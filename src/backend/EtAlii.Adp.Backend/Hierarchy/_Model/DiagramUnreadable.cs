namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>A registration file that could not be read.</summary>
public sealed record DiagramUnreadable(string Path) : DiagramRouting;
