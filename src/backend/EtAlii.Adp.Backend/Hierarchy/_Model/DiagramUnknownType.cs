namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>A registration file naming a MIME type no discovered definition matches (Requirement 2.6).</summary>
public sealed record DiagramUnknownType(string Path, string MimeType) : DiagramRouting;
