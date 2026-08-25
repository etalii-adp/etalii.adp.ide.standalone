using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

internal sealed class MindmapTestProjectNullResolver : IContextSourceResolver
{
    public bool CanResolve(ContextSource source) => false;

    public ValueTask<ContextLevelResolution> ResolveAsync(ShortGuid watchId, string rootPath, ContextSelectionSource source, ContextSource id, IReadOnlyList<string> clientPath, ContextResolvedLevel? parent, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.Contained;

    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange) =>
        throw new NotSupportedException();
}
