using EtAlii.Adp.Links;
using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// An index of links that holds one workspace, as the real one would have read it; with <paramref name="kinds"/>, every document
/// it holds can be given an element of those kinds, which <paramref name="grow"/> writes into its bytes.
/// </summary>
internal sealed class FixedLinkIndex(LinkWorkspace workspace, IReadOnlyList<LinkElementKind>? kinds = null, Func<byte[], string, string, string, byte[]?>? grow = null) : ILinkIndex
{
    private int _made;

    public IReadOnlyList<LinkElementKind> CreatableKindsOf(string path) => kinds ?? [];

    public string? NewElementIdOf(string path) => kinds is null ? null : $"new{++_made:00000}";

    public byte[]? WithElement(string path, byte[] bytes, string kind, string id, string name) => grow?.Invoke(bytes, kind, id, name);

    public event EventHandler<LinkIndexChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public IDisposable Track(string rootPath) => throw new NotSupportedException();

    public LinkWorkspace WorkspaceOf(string path) => workspace;

    public Task<string?> RenewAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken) => throw new NotSupportedException();
}
