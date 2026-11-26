using EtAlii.Adp.Links;
using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>An index of links that holds one workspace, as the real one would have read it.</summary>
internal sealed class FixedLinkIndex(LinkWorkspace workspace) : ILinkIndex
{
    public event EventHandler<LinkIndexChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public IDisposable Track(string rootPath) => throw new NotSupportedException();

    public LinkWorkspace? WorkspaceOf(string path) => workspace;

    public Task<string?> RenewAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken) => throw new NotSupportedException();
}
