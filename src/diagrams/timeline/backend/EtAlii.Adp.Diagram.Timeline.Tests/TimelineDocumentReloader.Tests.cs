using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>The shared reload seam over the timeline store, held to the contract every module's is.</summary>
public sealed class TimelineDocumentReloaderTests : DocumentReloaderContract<ITimelineDocumentStore>
{
    protected override DiagramOrigin Origin => Diagram.Timeline.Origin;

    protected override string Example => TimelineDocumentStoreTests.Simple;

    protected override ITimelineDocumentStore CreateStore() => new TimelineDocumentStore();

    protected override bool IsEmpty(ITimelineDocumentStore store, string path) => store.GetOrLoad(path).Model.Elements.Count == 0;

    protected override void OnChanged(ITimelineDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
