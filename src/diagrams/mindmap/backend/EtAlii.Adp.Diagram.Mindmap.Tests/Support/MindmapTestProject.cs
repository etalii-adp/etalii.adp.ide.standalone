using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Common;
using Microsoft.Extensions.DependencyInjection;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// A temporary project holding the corpus as a registered mindmap, and the real module
/// services over it - the same wiring the host uses, so what the resolver and provider tests
/// see is what the user would get.
/// </summary>
internal sealed class MindmapTestProject : IDisposable
{
    private readonly ServiceProvider _services;

    public MindmapTestProject()
    {
        Root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(IoPath.Combine(Root, "docs"));
        AdpPath = IoPath.Combine(Root, "docs", "architecture.adp");
        BodyPath = IoPath.Combine(Root, "docs", "architecture.mm");
        File.WriteAllText(AdpPath, Diagram.Mindmap.Origin.MimeType + "\n");
        File.Copy("Fixtures/architecture.mm", BodyPath);

        _services = new ServiceCollection()
            .AddSingleton<IDiagramDefinitionCatalog>(new MindmapTestProjectCatalog([Diagram.Mindmap]))
            .AddSingleton<DiagramFileRouter>()
            .AddSingleton<MindmapViewState>()
            .AddSingleton<MindmapContextSourceResolver>()
            .AddSingleton<MindmapContextActionProvider>()
            .AddSingleton<MindmapContextPropertyProvider>()
            .AddCommands()
            .AddMindmapCommands()
            .BuildServiceProvider();
    }

    public string Root { get; }

    public string AdpPath { get; }

    public string BodyPath { get; }

    public ShortGuid WatchId { get; } = ShortGuid.NewShortGuid();

    public IHistoryStack History => _services.GetRequiredService<IHistoryStackStore>().Get(Root);

    public IMindmapDocumentStore Documents => _services.GetRequiredService<IMindmapDocumentStore>();

    public MindmapViewState Views => _services.GetRequiredService<MindmapViewState>();

    public MindmapContextSourceResolver Resolver => _services.GetRequiredService<MindmapContextSourceResolver>();

    public MindmapContextActionProvider Provider => _services.GetRequiredService<MindmapContextActionProvider>();

    public MindmapContextPropertyProvider Properties => _services.GetRequiredService<MindmapContextPropertyProvider>();

    public MindmapDocument Document => Documents.GetOrLoad(BodyPath);

    /// <summary>The resolved hierarchy level for the registration file - what a node selection nests under.</summary>
    public ContextResolvedLevel FileLevel() => new(
        ContextSelectionSource.Explorer,
        new ContextSource { EntryId = ShortGuid.NewShortGuid() },
        ["docs", "architecture.adp"],
        ContextScope.Hierarchy,
        new ContextTarget(ContextScope.Hierarchy, AdpPath, IsContainer: false, ShortGuid.NewShortGuid(), Root, WatchId),
        new ContextLevelDetail { Entry = new EntryDetail { Kind = EntryKind.File, Available = true } },
        new MindmapTestProjectNullResolver());

    /// <summary>A provider target for one node, as the resolver would build it.</summary>
    public ContextTarget NodeTarget(string nodeId) =>
        new(ContextScope.DiagramElement, BodyPath, Document.Find(nodeId)?.HasChildren ?? false, SourceId: default, Root, WatchId, nodeId);

    public static ContextSource Element(string nodeId) => new() { ElementId = new ElementId { Value = nodeId } };

    public void Dispose()
    {
        _services.Dispose();
        TestFolder.TryDelete(Root);
    }

}
