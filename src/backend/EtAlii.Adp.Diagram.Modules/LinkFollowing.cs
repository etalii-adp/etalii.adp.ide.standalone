using EtAlii.Adp.Links;
using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// An open diagram following the index of links (DISL 4.12, etalii.adp spec 092, T018): a change to any other document of the
/// workspace, a target renamed, moved, copied or deleted, calls <c>changed</c>, so that the diagram renders again and its linked
/// elements show their targets as they now are. A change to the diagram's own document is left to its store.
/// </summary>
public sealed class LinkFollowing : IDisposable
{
    private readonly ILinkIndex? _links;
    private readonly string _path;
    private readonly Action _changed;

    /// <summary>Follows <paramref name="links"/> for the diagram at <paramref name="bodyPath"/>; follows nothing where there is no index.</summary>
    public LinkFollowing(ILinkIndex? links, string bodyPath, Action changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(changed);
        _links = links;
        _path = Path.GetFullPath(bodyPath).Replace('\\', '/');
        _changed = changed;
        _links?.Changed += OnChanged;
    }

    /// <summary>The workspace the diagram's links resolve against now; null where there is no index or none holds it.</summary>
    public LinkWorkspace? Workspace => _links?.WorkspaceOf(_path);

    /// <summary>Stops following the index.</summary>
    public void Dispose()
    {
        _links?.Changed -= OnChanged;
    }

    private void OnChanged(object? sender, LinkIndexChangedEventArgs args)
    {
        if (!string.Equals(args.Path, _path, StringComparison.Ordinal)) _changed();
    }
}
