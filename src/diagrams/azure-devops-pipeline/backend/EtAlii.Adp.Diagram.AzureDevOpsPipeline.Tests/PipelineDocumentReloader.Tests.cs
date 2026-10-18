using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>The shared reload seam over the pipeline store, held to the contract every module's is, and what is its own.</summary>
/// <remarks>
/// The store is asked with the folder the body sits in as its root, which is the contract's
/// workspace: a pipeline's templates are resolved within it.
/// </remarks>
public sealed class PipelineDocumentReloaderTests : DocumentReloaderContract<IPipelineDocumentStore>
{
    protected override ToolOrigin Origin => Diagram.Pipeline.Origin;

    protected override string Example => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "jobs-only.yml");

    protected override IPipelineDocumentStore CreateStore() => new PipelineDocumentStore(new FileWriter());

    protected override bool IsEmpty(IPipelineDocumentStore store, string path) =>
        store.GetOrLoad(IoPath.GetDirectoryName(path)!, path).Model.Jobs.Any() is false;

    protected override void OnChanged(IPipelineDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);

    [Fact]
    public void AReloadOfAPipelineNeverOpened_ReadsNothing_AndTellsNobody()
    {
        // Arrange: the store knows a pipeline's root only from an open, and its parse needs it.
        using var folder = TestFolder.Create("adp-pipeline-reloader");
        var body = folder.Combine("azure-pipelines.yml");
        File.Copy(Example, body);
        var store = CreateStore();
        var told = 0;
        store.Changed += (_, _) => told++;
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<IPipelineDocumentStore>(Origin, store);

        // Act.
        reloader.Reload(folder.Path, body);
        reloader.BodyDeleted(folder.Path, body);

        // Assert: nothing was open on it, and its first open reads it.
        Assert.Equal(0, told);
        Assert.NotEmpty(store.GetOrLoad(folder.Path, body).Model.Jobs);
    }
}
