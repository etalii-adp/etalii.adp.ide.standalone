namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>The stable identifiers of this module's rules, prefixed with its origin's type name.</summary>
/// <remarks>
/// Four rules where the timeline has six, and the two that are gone are the two that were about
/// time: an element ending before it begins, and a value that is not a time this module can read.
/// Nothing replaces them. A dependency graph will want a cycle rule one day; it does not have one
/// now, and inventing it here would be inventing a requirement.
/// </remarks>
public static class DependencyGraphRules
{
    /// <summary>A relation naming an element that does not exist.</summary>
    public const string DanglingRelation = "dependencies.dangling-relation";

    /// <summary>Two declarations sharing one id, which makes every reference to it ambiguous.</summary>
    public const string DuplicateId = "dependencies.duplicate-id";

    /// <summary>An element with no id at all, which nothing can select, connect or edit.</summary>
    public const string MissingId = "dependencies.missing-id";

    /// <summary>A node declared to depend on itself, which says nothing and draws as a stub.</summary>
    public const string SelfDependency = "dependencies.self-dependency";
}
