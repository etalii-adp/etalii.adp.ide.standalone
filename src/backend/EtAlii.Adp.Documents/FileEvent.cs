namespace EtAlii.Adp.Documents;

/// <summary>What a <see cref="WatchedFolder"/> heard.</summary>
public enum FileEventKind
{
    /// <summary>A file or folder appeared.</summary>
    Created,

    /// <summary>A file's content or size changed.</summary>
    Changed,

    /// <summary>A file or folder went away.</summary>
    Deleted,

    /// <summary>A file or folder took another name; <see cref="FileEvent.OldPath"/> is the name it had.</summary>
    Renamed,

    /// <summary>
    /// The watcher overflowed and dropped events it cannot name. Which files changed is unknowable,
    /// so an owner that must not miss a change reads its subject again.
    /// </summary>
    Lost,
}

/// <summary>One event of a <see cref="WatchedFolder"/>.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Path">The full path it happened to; for <see cref="FileEventKind.Lost"/>, the watched folder.</param>
/// <param name="OldPath">The name a renamed file had; null for every other kind.</param>
public readonly record struct FileEvent(FileEventKind Kind, string Path, string? OldPath = null);
