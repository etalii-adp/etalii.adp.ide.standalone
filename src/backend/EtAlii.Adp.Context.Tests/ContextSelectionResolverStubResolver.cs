using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;

namespace EtAlii.Adp.Context.Tests;

/// <summary>
/// Answers for entry ids, echoing the client path (or a fixed fill-in), and records
/// which parent it was handed at each level so the chain walk can be asserted.
/// </summary>
internal sealed class ContextSelectionResolverStubResolver : IContextSourceResolver
{
    private readonly bool _canResolve;
    private readonly ContextNesting _nesting;
    private readonly bool _rejectWhenParentPresent;
    private readonly IReadOnlyList<string>? _fillPath;

    public ContextSelectionResolverStubResolver(
        bool canResolve = true,
        ContextNesting nesting = ContextNesting.Contained,
        bool rejectWhenParentPresent = false,
        IReadOnlyList<string>? fillPath = null)
    {
        _canResolve = canResolve;
        _nesting = nesting;
        _rejectWhenParentPresent = rejectWhenParentPresent;
        _fillPath = fillPath;
    }

    public List<ContextResolvedLevel?> ParentsSeen { get; } = new();

    public bool CanResolve(ContextSource source) => _canResolve && source.SourceCase == ContextSource.SourceOneofCase.EntryId;

    public ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId, string rootPath, ContextSelectionSource source, ContextSource id,
        IReadOnlyList<string> clientPath, ContextResolvedLevel? parent, CancellationToken cancellationToken)
    {
        ParentsSeen.Add(parent);
        if (_rejectWhenParentPresent && parent is not null)
        {
            return ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel("not inside its parent"));
        }

        var path = clientPath.Count == 0 && _fillPath is not null ? _fillPath : clientPath;
        var level = new ContextResolvedLevel(
            source, id, path, ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, System.IO.Path.Combine([rootPath, .. path]), false, (ShortGuid)id.EntryId),
            new ContextLevelDetail(), this);
        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    public ContextNesting NestingOf(ContextResolvedLevel level) => _nesting;

    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange) => new StubResolverNoopDisposable();

}
