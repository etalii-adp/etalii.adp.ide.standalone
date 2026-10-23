namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>An item: a part of the subject, which holds requirements and further items (hierarchical-requirement.des, Item).</summary>
public sealed record Item(string Id, string? ParentId, string Title, string Description, IReadOnlyList<string> Tags, bool Collapsed, int Line, bool Placeholder)
    : RequirementEntry(Id, ParentId, Title, Description, Tags, Collapsed, Line, Placeholder);
