namespace EtAlii.Adp.Specification.Fbl.Expressions;

/// <summary>Where a CEL expression appears, which decides the variables it may use (FBL §2.4).</summary>
public enum CelContext
{
    /// <summary>A rule of a yaml, json or xml binding: entry, parent, path, line, registration.</summary>
    Tree,

    /// <summary>A rule of a lines or blocks binding: entry, parent, groups, line, registration.</summary>
    Lines,

    /// <summary><c>insert.when</c>: attributes.</summary>
    Insert,
}
