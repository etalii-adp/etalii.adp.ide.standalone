namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>A requirement: one statement of what the subject must do or be, standing in an item and holding nothing (hierarchical-requirement.des, Requirement).</summary>
/// <param name="Category">The category's name as written, or null when it has none.</param>
/// <param name="State">The state's name as written, or null when it has none.</param>
public sealed record Requirement(string Id, string? ParentId, string Title, string Description, IReadOnlyList<string> Tags, string? Category, string? State, bool Collapsed, int Line, bool Placeholder)
    : RequirementEntry(Id, ParentId, Title, Description, Tags, Collapsed, Line, Placeholder);
