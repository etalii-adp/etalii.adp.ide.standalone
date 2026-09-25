using EtAlii.Adp.Documents.Wire;
namespace EtAlii.Adp.Context;

/// <summary>
/// Resolves ids of one kind (one <see cref="ContextSource"/> member) into locations the
/// rest of the context mechanism can work with, and decides how things of that kind
/// nest. This is the selection-side twin of <see cref="IContextActionProvider"/>: a new
/// kind of selectable thing is one more implementation, and nothing in the context
/// service learns that it exists.
/// </summary>
public interface IContextSourceResolver
{
    /// <summary>Whether this resolver answers for the member <paramref name="source"/> uses.</summary>
    bool CanResolve(ContextSource source);

    /// <summary>
    /// Resolves one level. <paramref name="clientPath"/> is verified against what the id
    /// resolves to, or filled in when empty; it is never trusted on its own.
    /// <paramref name="parent"/> is the already-resolved enclosing level, whose nesting
    /// rule this level must satisfy, or null for the outermost level.
    /// </summary>
    ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId,
        string rootPath,
        ContextSelectionSource source,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken);

    /// <summary>How a selection nested under <paramref name="level"/> relates to it.</summary>
    ContextNesting NestingOf(ContextResolvedLevel level);

    /// <summary>
    /// Starts observing a resolved level. <paramref name="onChange"/> receives the new
    /// relative path when the thing moves or is renamed, or null when it is gone; it
    /// must tolerate being invoked after disposal. Disposing stops the observation.
    /// </summary>
    IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange);
}
