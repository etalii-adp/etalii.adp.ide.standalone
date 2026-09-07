using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;

namespace EtAlii.Adp.Context.Tests;

/// <summary>A resolver whose tracks the test can fire by hand and count disposals of.</summary>
internal sealed class ContextSelectionStoreStubResolver : IContextSourceResolver
{
    private Action<IReadOnlyList<string>?>? _onChange;

    public int Disposed { get; internal set; }

    public void Fire(IReadOnlyList<string>? path) => _onChange?.Invoke(path);

    public bool CanResolve(ContextSource source) => true;

    public ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId, string rootPath, ContextSelectionSource source, ContextSource id,
        IReadOnlyList<string> clientPath, ContextResolvedLevel? parent, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        _onChange = onChange;
        return new StubResolverSubscription(this);
    }

}
