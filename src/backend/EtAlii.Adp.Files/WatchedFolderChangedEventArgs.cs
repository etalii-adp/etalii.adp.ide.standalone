namespace EtAlii.Adp.Files;

/// <summary>One watched folder has been re-read because something in it changed.</summary>
/// <remarks>
/// Carries the new snapshot rather than a description of what changed. A folder diagram's model
/// is immutable and derived wholesale, so "here is what it is now" is both simpler and less able
/// to drift than "here is what moved" - and a session turns it into deltas by comparing, which it
/// would have to do either way.
/// </remarks>
/// <param name="folderPath">The folder that was re-read, as a full path.</param>
/// <param name="snapshot">What it now holds.</param>
public sealed class WatchedFolderChangedEventArgs<TSnapshot>(string folderPath, TSnapshot snapshot) : EventArgs
{
    public string FolderPath { get; } = folderPath;

    public TSnapshot Snapshot { get; } = snapshot;
}
