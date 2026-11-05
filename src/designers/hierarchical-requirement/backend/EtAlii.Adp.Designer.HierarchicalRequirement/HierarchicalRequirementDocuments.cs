namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>The module's one <see cref="IHierarchicalRequirementDocumentStore"/>: it holds nothing, and passes each written path on.</summary>
internal sealed class HierarchicalRequirementDocuments : IHierarchicalRequirementDocumentStore
{
    public event Action<string, byte[]>? Writing;

    public event Action<string>? Reloaded;

    public void WillWrite(string path, byte[] bytes) => Writing?.Invoke(path, bytes);

    public Task ReloadAsync(string path, CancellationToken cancellationToken)
    {
        Reloaded?.Invoke(path);
        return Task.CompletedTask;
    }
}
