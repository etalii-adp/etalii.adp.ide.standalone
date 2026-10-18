namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// A watch's claim on its hierarchy model, held in the connection's scope: while it is held the model
/// is kept, and releasing it drops the model (code-structure FR-067).
/// </summary>
public sealed class HierarchyModelClaim(HierarchyModel model, Action release) : IDisposable
{
    private readonly IDisposable _release = Disposable.From(release);

    /// <summary>The model claimed.</summary>
    public HierarchyModel Model { get; } = model;

    public void Dispose() => _release.Dispose();
}
