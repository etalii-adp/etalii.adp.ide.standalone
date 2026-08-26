using EtAlii.Adp.Backend.Context;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Claims every element id, as every diagram module's resolver does, and then decides whether it
/// owns this one - which is what a real resolver can only tell by looking at the enclosing file.
/// </summary>
/// <remarks>
/// Two of these stand in for two diagram types registered side by side. <c>CanResolve</c> answers
/// a question about the shape of an id, not about ownership, so both say yes to the same element
/// and only one of them accepts it.
/// </remarks>
internal sealed class ContextSelectionResolverRefusingStubResolver : IContextSourceResolver
{
    private readonly string _ownedElementId;
    private readonly string _refusal;

    /// <summary>Creates a resolver that accepts only <paramref name="ownedElementId"/>.</summary>
    public ContextSelectionResolverRefusingStubResolver(string ownedElementId, string refusal)
    {
        _ownedElementId = ownedElementId;
        _refusal = refusal;
    }

    /// <summary>How many times this resolver was actually asked.</summary>
    public int Asked { get; private set; }

    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.ElementId;

    public ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId,
        string rootPath,
        ContextSelectionSource source,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        Asked++;
        if (id.ElementId.Value != _ownedElementId)
        {
            return ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(_refusal));
        }

        var level = new ContextResolvedLevel(
            source,
            id,
            clientPath,
            ContextScope.DiagramElement,
            new ContextTarget(
                ContextScope.DiagramElement,
                System.IO.Path.Combine(rootPath, "diagram.adp"),
                IsContainer: false,
                SourceId: default,
                rootPath,
                watchId,
                id.ElementId.Value),
            new ContextLevelDetail(),
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange) =>
        new StubResolverNoopDisposable();
}
