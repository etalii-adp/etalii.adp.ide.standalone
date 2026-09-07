using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Editor;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The read-only facts of a text file: encoding, line-ending style, size and line count -
/// one provider for the whole editor family, registered once rather than per module, because
/// the four properties are identical in shape for any open text file (modular-text-editors
/// Requirements 9.1-9.3).
/// </summary>
/// <remarks>
/// Placed in core rather than the task's suggested <c>EtAlii.Adp.Editor</c> project, and
/// recorded as a deviation: <see cref="IContextPropertyProvider"/> and the context model live
/// in <c>EtAlii.Adp.Backend</c>, which references <c>EtAlii.Adp.Editor</c> - the provider in
/// the Editor project would make that reference circular. The requirement it satisfies is
/// "one <c>IContextPropertyProvider</c>" registered once, which holds either way.
/// <para>
/// Every property is read-only with a reason, per Requirement 9.2's own framing: these are
/// facts about the file, not settings of the editor - a user who wants CRLF changes it in
/// their editor of choice or their <c>.gitattributes</c>.
/// </para>
/// </remarks>
public sealed class EditorFilePropertyProvider : IContextPropertyProvider
{
    private const string Group = "File";
    private const string FactReason = "A fact about the file, not a setting: a user who wants it different changes the file itself - in their editor of choice, or their .gitattributes.";

    private readonly DiagramFileRouter _router;

    public EditorFilePropertyProvider(DiagramFileRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        _router = router;
    }

    public ContextScope Scope => ContextScope.Hierarchy;

    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        // Only the editor family's files: a folder has no text, and a diagram-routed file's
        // grid belongs to its diagram module. A file the buffer refuses (binary, oversize,
        // not UTF-8) contributes nothing - the refusal reaches the user when they open it.
        if (target.IsContainer || !HierarchyTargets.Exists(target)
            || _router.Route(target.ResolvedFullPath, target.RootPath) is DiagramRouted)
        {
            return ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);
        }

        var opened = TextFileBuffer.Open(target.ResolvedFullPath);
        if (opened.Buffer is null)
        {
            return ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);
        }

        var sizeInBytes = new FileInfo(target.ResolvedFullPath).Length;
        return ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>(
        [
            new ContextPropertyDefinition("editor.encoding", "Encoding", opened.Buffer.EncodingName, ReadOnlyReason: FactReason, Group: Group),
            new ContextPropertyDefinition("editor.line-endings", "Line endings", opened.Buffer.LineEndingStyle, ReadOnlyReason: FactReason, Group: Group),
            new ContextPropertyDefinition("editor.size", "Size", $"{sizeInBytes} bytes", ReadOnlyReason: FactReason, Group: Group),
            new ContextPropertyDefinition("editor.line-count", "Lines", $"{opened.Buffer.LineCount}", ReadOnlyReason: FactReason, Group: Group),
        ]);
    }

    /// <summary>Refused for every id, with the same sentence the grid already shows beside the value.</summary>
    public ValueTask<ContextPropertyResult> SetAsync(ContextTarget target, string propertyId, string value, CancellationToken cancellationToken)
        => ValueTask.FromResult(new ContextPropertyResult(FactReason));
}
