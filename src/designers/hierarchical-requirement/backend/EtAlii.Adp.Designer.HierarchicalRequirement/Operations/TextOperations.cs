namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The operations that change an entry's text, <c>setTitle</c> and <c>setDescription</c> of
/// <c>hierarchical-requirement.des</c> (FR-026): a replace of the one value, with the numbering every edit writes
/// (spec 029 R17). What they write is what the conformance fixtures state, byte for byte.
/// </summary>
internal static class TextOperations
{
    public const string SetTitle = "setTitle";

    public const string SetDescription = "setDescription";

    /// <summary>Whether <paramref name="operation"/> is one of these.</summary>
    public static bool Handles(string operation) => operation is SetTitle or SetDescription;

    /// <summary>The attribute an operation sets, which is also the name of its one param.</summary>
    public static string AttributeOf(string operation) => operation == SetTitle ? "title" : "description";

    /// <summary>
    /// Whether <paramref name="value"/> can be written as the attribute <paramref name="operation"/> sets: the sentence
    /// saying why not, or empty. A title is the rest of its bullet's line, so it holds no line break.
    /// </summary>
    public static string Refusal(string operation, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return operation == SetTitle && value.AsSpan().IndexOfAny('\r', '\n') >= 0 ? "A title is one line; the description holds the lines below it." : "";
    }

    /// <summary>
    /// The bytes of the body <paramref name="body"/>, named <paramref name="fileName"/>, after <paramref name="operation"/>
    /// set the entry the surface names <paramref name="targetId"/> to <paramref name="value"/>; or the sentence saying why
    /// it cannot be, in which case nothing changed. A target the file no longer has is refused as a change made against
    /// an older file.
    /// </summary>
    public static (byte[]? Bytes, string Refusal) Apply(byte[] body, string fileName, string operation, string targetId, string value)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(value);
        if (Refusal(operation, value) is { Length: > 0 } refusal) return (null, refusal);

        var read = HierarchicalRequirementBody.Read(body, fileName);
        if (read.Document.ReadOnlyReason.Length > 0) return (null, read.Document.ReadOnlyReason);
        var ids = HierarchicalRequirementSurfaceMapper.NodeIds(read.Document.Entries);
        if (read.Document.Entries.FirstOrDefault(entry => ids[entry] == targetId) is not { } target) return (null, HierarchicalRequirementGestures.Drifted);

        var edit = new BodyEdit(read);
        edit.Set(edit.ElementOf(target), AttributeOf(operation), value);
        return edit.Write();
    }
}
