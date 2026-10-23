namespace EtAlii.Adp.Specification.Fbl.Tests.Support;

/// <summary>An element of a requirement document as the Hierarchical Requirement Designer holds it (<see cref="HierarchicalRequirementDocument"/>).</summary>
internal sealed record DesignerElement(string Id, string Type, string? ParentId, IReadOnlyDictionary<string, object?> Attributes);
