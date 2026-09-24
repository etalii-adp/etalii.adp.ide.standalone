using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// The store owns pipeline documents, and the two things worth holding here are sharing and
/// honesty: two diagrams on one file must see one document, and a file that does not parse must
/// open as a diagram that says so rather than either throwing or being quietly overwritten.
/// </summary>
public class PipelineDocumentStoreTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-pipeline-store-" + Guid.NewGuid().ToString("N"));

    public PipelineDocumentStoreTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    private string Write(string relativePath, string content)
    {
        var full = IoPath.Combine(_workspace, relativePath.Replace('/', IoPath.DirectorySeparatorChar));
        Directory.CreateDirectory(IoPath.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private const string Simple = "stages:\n  - stage: Build\n    jobs:\n      - job: Compile\n        steps:\n          - script: x\n";

    [Fact]
    public void TwoOpensOfTheSameFile_ShareOneDocument()
    {
        // Arrange: an edit through one diagram has to be visible in another on the same file,
        // which only holds if they are looking at the same instance.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore();

        // Act.
        var first = store.GetOrLoad(_workspace, path);
        var second = store.GetOrLoad(_workspace, path);

        // Assert.
        Assert.Same(first.Document, second.Document);
    }

    [Fact]
    public void AFileThatIsNotThereYet_OpensAsEmptyRatherThanThrowing()
    {
        // Arrange: the registration may have been written a moment ago. A diagram that cannot
        // open at all is a worse answer than an empty one.
        var store = new PipelineDocumentStore();

        // Act.
        var entry = store.GetOrLoad(_workspace, IoPath.Combine(_workspace, "not-written-yet.yml"));

        // Assert.
        Assert.True(entry.IsUsable);
        Assert.Empty(entry.Model.Stages);
        Assert.Equal("", entry.Document.Text);
    }

    [Fact]
    public void AFileThatDoesNotParse_OpensCarryingTheReasonAndTheLine()
    {
        // Arrange: Requirement 3.6 - the diagram opens as unavailable naming file and line, which
        // is what tells the user where to go and look.
        var path = Write("broken.yml", "stages:\n  - stage: Build\n   jobs: [\n");
        var store = new PipelineDocumentStore();

        // Act.
        var entry = store.GetOrLoad(_workspace, path);

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.NotEqual(0, entry.ErrorLine);
        Assert.Contains("Line ", entry.Error);
        Assert.Empty(entry.Model.Stages);
    }

    [Fact]
    public void AFileThatDoesNotParse_IsNotWrittenBack()
    {
        // Arrange: the model is empty because the file never parsed, so writing it out would
        // replace a file somebody can still fix with one this module invented.
        const string broken = "stages:\n  - stage: Build\n   jobs: [\n";
        var path = Write("broken.yml", broken);
        var store = new PipelineDocumentStore();
        var entry = store.GetOrLoad(_workspace, path);

        // Act.
        var refusal = store.Save(_workspace, path, entry);

        // Assert.
        Assert.NotEqual("", refusal);
        Assert.Equal(broken, File.ReadAllText(path));
    }

    [Fact]
    public void SavingAnEditedDocument_WritesItByteForByte()
    {
        // Arrange.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore();
        var entry = store.GetOrLoad(_workspace, path);
        var stage = entry.Model.Stages.Single();

        // Act.
        new PipelineWriter(entry.Document).SetDisplayName(PipelineEditTarget.For(stage), "Build it");
        var refusal = store.Save(_workspace, path, entry);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(entry.Document.Text, File.ReadAllText(path));
        Assert.Contains("displayName: Build it", File.ReadAllText(path));
    }

    [Fact]
    public void SavingRebuildsTheModel_SoTheNextReaderSeesTheEdit()
    {
        // Arrange.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore();
        var entry = store.GetOrLoad(_workspace, path);
        new PipelineWriter(entry.Document).SetDisplayName(PipelineEditTarget.For(entry.Model.Stages.Single()), "Build it");

        // Act.
        store.Save(_workspace, path, entry);

        // Assert.
        Assert.Equal("Build it", store.GetOrLoad(_workspace, path).Model.Stages.Single().DisplayName);
    }

    [Fact]
    public void SavingTellsTheSessionsOnThatDocument()
    {
        // Arrange.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore();
        var entry = store.GetOrLoad(_workspace, path);
        new PipelineWriter(entry.Document).SetDisplayName(PipelineEditTarget.For(entry.Model.Stages.Single()), "Build it");
        var announced = new List<PipelineDocumentChangedEventArgs>();
        store.Changed += (_, args) => announced.Add(args);

        // Act.
        store.Save(_workspace, path, entry);

        // Assert.
        var change = Assert.Single(announced);
        Assert.Equal(path, change.Path);
        Assert.Equal("Build it", change.Model.Stages.Single().DisplayName);
    }

    [Fact]
    public void ATouch_TellsTheSessionsWithoutChangingAnything()
    {
        // Arrange: a change to how a pipeline is drawn rather than to what it says.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore();
        store.GetOrLoad(_workspace, path);
        var announced = 0;
        store.Changed += (_, _) => announced++;

        // Act.
        store.Touch(_workspace, path);

        // Assert.
        Assert.Equal(1, announced);
        Assert.Equal(Simple, File.ReadAllText(path));
    }

    [Fact]
    public void AReload_PicksUpAnExternalEdit()
    {
        // Arrange: somebody edited the file in their editor while the diagram was open.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore();
        store.GetOrLoad(_workspace, path);
        File.WriteAllText(path, Simple.Replace("stage: Build", "stage: Rebuilt", StringComparison.Ordinal));
        var announced = new List<PipelineDocumentChangedEventArgs>();
        store.Changed += (_, args) => announced.Add(args);

        // Act.
        store.Reload(_workspace, path);

        // Assert.
        Assert.Equal("Rebuilt", Assert.Single(announced).Model.Stages.Single().Name);
        Assert.Equal("Rebuilt", store.GetOrLoad(_workspace, path).Model.Stages.Single().Name);
    }

    [Fact]
    public void AReload_PicksUpAnEditToATemplateToo()
    {
        // Arrange: what changed on disk may not have been this file at all. A resolver still
        // serving what it read before would show the pipeline as it used to be.
        var path = Write("azure-pipelines.yml", "jobs:\n  - template: templates/shared.yml\n");
        var template = Write("templates/shared.yml", "jobs:\n  - job: Before\n    steps:\n      - script: x\n");
        var store = new PipelineDocumentStore();
        Assert.Equal("Before", store.GetOrLoad(_workspace, path).Model.Jobs.Single().Name);

        // Act.
        File.WriteAllText(template, "jobs:\n  - job: After\n    steps:\n      - script: x\n");
        store.Reload(_workspace, path);

        // Assert.
        Assert.Equal("After", store.GetOrLoad(_workspace, path).Model.Jobs.Single().Name);
    }

    [Fact]
    public void ForgettingADocument_MakesTheNextOpenReadItAfresh()
    {
        // Arrange.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore();
        var first = store.GetOrLoad(_workspace, path);

        // Act.
        store.Forget(path);
        var second = store.GetOrLoad(_workspace, path);

        // Assert.
        Assert.NotSame(first.Document, second.Document);
    }

    [Fact]
    public void TwoPipelinesSharingATemplate_ShareOneResolver()
    {
        // Arrange: the cache lives in the resolver, so one resolver per workspace is what stops a
        // template shared by twenty pipelines being read twenty times. Proven the same way the
        // resolver's own test proves it - by rewriting the file between the two reads.
        var one = Write("one.yml", "jobs:\n  - template: templates/shared.yml\n");
        var two = Write("two.yml", "jobs:\n  - template: templates/shared.yml\n");
        var template = Write("templates/shared.yml", "jobs:\n  - job: Before\n    steps:\n      - script: x\n");
        var store = new PipelineDocumentStore();

        // Act.
        store.GetOrLoad(_workspace, one);
        File.WriteAllText(template, "jobs:\n  - job: After\n    steps:\n      - script: x\n");

        // Assert.
        Assert.Equal("Before", store.GetOrLoad(_workspace, two).Model.Jobs.Single().Name);
    }

    [Fact]
    public void ATemplateThatCannotBeFollowed_IsCarriedOnTheModelRatherThanFailingTheLoad()
    {
        // Arrange: a gap in the diagram, not the end of it.
        var path = Write("azure-pipelines.yml", "stages:\n  - template: elsewhere.yml@other\n");
        var store = new PipelineDocumentStore();

        // Act.
        var entry = store.GetOrLoad(_workspace, path);

        // Assert.
        Assert.True(entry.IsUsable);
        Assert.Equal(PipelineTemplateUnresolvedReason.OtherRepository, Assert.Single(entry.Model.Unresolved).Reason);
    }
}
