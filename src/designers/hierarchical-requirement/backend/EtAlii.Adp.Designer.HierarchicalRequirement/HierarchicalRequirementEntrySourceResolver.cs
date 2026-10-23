using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Fbl.Routing;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// An entry of a requirement document as something that can be selected and acted on (FR-012): the page names it by the
/// id its box carries, under the file it is in, and this finds it there and follows it while the file changes - so that a
/// click on a box is the application's selection, and a key on it reaches the entry's actions.
/// </summary>
/// <remarks>
/// <b>Found missing by the browser check of spec 029 T081.</b> The actions answered for an entry, but nothing turned the
/// page's id into one, so every key and every selection of an entry was refused as an unrecognised source.
/// </remarks>
internal sealed class HierarchicalRequirementEntrySourceResolver(IHierarchicalRequirementDocumentStore documents) : IContextSourceResolver
{
    /// <summary>The origin a selected entry is marked with, so that only this module's providers answer for it.</summary>
    public static readonly ToolOrigin EntryOrigin = ToolOrigin.TryParse(Designer.Origin)!;

    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.ElementId;

    public ValueTask<ContextLevelResolution> ResolveAsync(ShortGuid watchId, ProjectRoot root, ContextSource id, IReadOnlyList<string> clientPath, ContextResolvedLevel? parent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        cancellationToken.ThrowIfCancellationRequested();

        if (parent is null || parent.Scope != ContextScope.Hierarchy)
        {
            return Rejected("The selected file is not a requirement document.");
        }

        var bodyPath = parent.Target.ResolvedFullPath;
        var elementId = id.ElementId.Value;
        if (HierarchicalRequirementEntries.Find(bodyPath, elementId) is not { } found)
        {
            return Rejected("That entry is no longer in this file.");
        }

        var text = Describe(found.Entry);
        var detail = new ContextLevelDetail { Element = new ElementDetail { Text = text } };
        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(new ContextResolvedLevel(
            id,
            [text],
            ContextScope.DiagramElement,
            ContextTarget.ForElement(bodyPath, elementId, root, watchId, EntryOrigin),
            detail,
            this)));
    }

    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    public IDisposable Track(ShortGuid watchId, ProjectRoot root, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);

        var bodyPath = level.Target.ResolvedFullPath;
        var elementId = level.Target.ElementId;
        documents.Reloaded += OnReloaded;
        return new Unsubscriber(() => documents.Reloaded -= OnReloaded);

        void OnReloaded(string path)
        {
            if (string.Equals(path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                // Read again: a retitle keeps the id while changing what is shown, and a deleted entry clears the selection.
                onChange(HierarchicalRequirementEntries.Find(bodyPath, elementId) is { } found ? [Describe(found.Entry)] : null);
            }
        }
    }

    private static string Describe(RequirementEntry entry) => entry.Title.Length > 0 ? $"{entry.Id} {entry.Title}" : entry.Id;

    private static ValueTask<ContextLevelResolution> Rejected(string reason) => ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

/// <summary>Finds an entry of a requirement document by the id the page gives its box.</summary>
internal static class HierarchicalRequirementEntries
{
    /// <summary>The entry <paramref name="elementId"/> names in the file at <paramref name="bodyPath"/>, with the bytes and the document it was read from; null when the file is not a requirement document or holds no such entry.</summary>
    public static Found? Find(string bodyPath, string elementId)
    {
        if (elementId.Length == 0 || !bodyPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = SharedDocumentReader.ReadAllBytes(bodyPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var name = System.IO.Path.GetFileName(bodyPath);
        if (!BodyClaim.Claims(HierarchicalRequirementDefinition.Binding, name, bytes))
        {
            return null;
        }

        var document = HierarchicalRequirementBody.Read(bytes, name).Document;
        var ids = HierarchicalRequirementSurfaceMapper.NodeIds(document.Entries);
        return document.Entries.FirstOrDefault(entry => ids[entry] == elementId) is { } entry ? new Found(entry, bytes, document) : null;
    }

    internal sealed record Found(RequirementEntry Entry, byte[] Bytes, HierarchicalRequirementDocument Document);
}
