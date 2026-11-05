namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>A stored filter: its name and its query (hierarchical-requirement.des, Filter; DESL 0.2 section 7.7.3).</summary>
public sealed record Filter(string Name, string Query);
