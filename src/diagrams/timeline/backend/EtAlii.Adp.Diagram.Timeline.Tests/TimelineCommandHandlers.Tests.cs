using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>What every handler does with a save, through the shared contract (code-structure FR-022).</summary>
public sealed class TimelineCommandHandlersTests : EditHandlerContract<ITimelineDocumentStore, LineDocument, TimelineModel>
{
    protected override string Example => TimelineDocumentStoreTests.Simple;

    protected override ITimelineDocumentStore CreateStore() => new TimelineDocumentStore(new FileWriter());

    protected override ITimelineDocumentStore Warning(ITimelineDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(ITimelineDocumentStore store) => new TimelineTestDispatcher(store);

    protected override ICommand EditOf(string bodyPath) => new RenameTimelineElementCommand(bodyPath, "k7Qv2mXa", "Exploration");

    protected override ICommand EditOfMissing(string bodyPath) => new RenameTimelineElementCommand(bodyPath, "gone0000", "Exploration");

    private sealed class WarningStore(ITimelineDocumentStore inner, string warning)
        : WarningDocumentStore<LineDocument, TimelineModel>(inner, warning), ITimelineDocumentStore;
}
