using EtAlii.Adp.Sessions;

namespace EtAlii.Adp.Links;

/// <summary>
/// The index of a project's links as a part of a connection's project session: the root is indexed once a connection has it
/// open and followed until the last connection to it lets go.
/// </summary>
internal sealed class LinkTracking(ILinkIndex index) : IProjectSessionPart
{
    public IDisposable Acquire(ProjectRoot root) => index.Track(root.FullPath);
}
