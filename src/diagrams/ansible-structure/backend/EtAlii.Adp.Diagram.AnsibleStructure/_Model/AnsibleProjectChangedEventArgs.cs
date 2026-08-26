namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One registered folder has been re-read because something in it changed.
/// </summary>
/// <remarks>
/// Carries the new project rather than a description of what changed. A folder diagram's model
/// is immutable and derived wholesale, so "here is what it is now" is both simpler and less
/// able to drift than "here is what moved" - and the session turns it into deltas by comparing,
/// which it would have to do either way.
/// </remarks>
/// <param name="FolderPath">The folder that was re-read.</param>
/// <param name="Project">What it now holds.</param>
public sealed class AnsibleProjectChangedEventArgs(string folderPath, AnsibleProject project) : EventArgs
{
    public string FolderPath { get; } = folderPath;

    public AnsibleProject Project { get; } = project;
}
