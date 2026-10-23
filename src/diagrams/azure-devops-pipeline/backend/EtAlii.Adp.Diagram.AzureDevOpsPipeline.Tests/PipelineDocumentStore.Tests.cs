using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// The store owns pipeline documents, and the two things worth holding here are sharing and
/// honesty: two diagrams on one file must see one document, and a file that does not parse must
/// open as a diagram that says so rather than either throwing or being quietly overwritten.
/// </summary>
public class PipelineDocumentStoreTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("adp-pipeline-store");
    private readonly string _workspace;

    private ProjectRoot Root => ProjectRoot.From(_workspace);

    public PipelineDocumentStoreTests() => _workspace = _testFolder.Path;

    public void Dispose()
    {
        _testFolder.Dispose();

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
    public async Task TwoOpensOfTheSameFile_ShareOneDocument()
    {
        // Arrange: an edit through one diagram has to be visible in another on the same file,
        // which only holds if they are looking at the same instance.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore(new FileWriter());

        // Act.
        var first = await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken);
        var second = await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Same(first.Document, second.Document);
    }

    [Fact]
    public async Task AFileThatIsNotThereYet_OpensAsEmptyRatherThanThrowing()
    {
        // Arrange: the registration may have been written a moment ago. A diagram that cannot
        // open at all is a worse answer than an empty one.
        var store = new PipelineDocumentStore(new FileWriter());

        // Act.
        var entry = await store.GetOrLoadAsync(Root, IoPath.Combine(_workspace, "not-written-yet.yml"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(entry.IsUsable);
        Assert.Empty(entry.Model.Stages);
        Assert.Equal("", entry.Document.Text);
    }

    [Fact]
    public async Task AFileThatDoesNotParse_OpensCarryingTheReasonAndTheLine()
    {
        // Arrange: Requirement 3.6 - the diagram opens as unavailable naming file and line, which
        // is what tells the user where to go and look.
        var path = Write("broken.yml", "stages:\n  - stage: Build\n   jobs: [\n");
        var store = new PipelineDocumentStore(new FileWriter());

        // Act.
        var entry = await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.StartsWith("Line 3: ", entry.Unreadable, StringComparison.Ordinal);
        Assert.Empty(entry.Model.Stages);
        var problem = Assert.Single(entry.Problems);
        Assert.Equal((2, entry.Unreadable), (problem.Line, $"Line 3: {problem.Message}"));
    }

    [Fact]
    public async Task AFileThatDoesNotParse_IsNotWrittenBack()
    {
        // Arrange: the model is empty because the file never parsed, so writing it out would
        // replace a file somebody can still fix with one this module invented.
        const string broken = "stages:\n  - stage: Build\n   jobs: [\n";
        var path = Write("broken.yml", broken);
        var store = new PipelineDocumentStore(new FileWriter());
        var entry = await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken);

        // Act.
        var refusal = await store.SaveAsync(Root, path, entry, TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotEqual("", refusal.Error);
        Assert.Equal(broken, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheStoreSeenFromARoot_ResolvesTemplatesThere_AndSavesAsTheStoreDoes()
    {
        // Arrange: a body below the root names its template from the root.
        Write("templates/shared.yml", "jobs:\n  - job: Shared\n    steps:\n      - script: x\n");
        var path = Write("pipelines/azure-pipelines.yml", "jobs:\n  - template: /templates/shared.yml\n");
        const string broken = "stages:\n  - stage: Build\n   jobs: [\n";
        var brokenPath = Write("broken.yml", broken);
        var rooted = new PipelineDocumentStore(new FileWriter()).In(Root);

        // Act.
        var entry = await rooted.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        var edited = new PipelineDocumentStore(new FileWriter()).Open(Root, path, [.. entry.Document.Bytes, .. "# edited\n"u8]);
        var saved = await rooted.SaveAsync(path, edited, TestContext.Current.CancellationToken);
        var refused = await rooted.SaveAsync(brokenPath, (await rooted.GetOrLoadAsync(brokenPath, TestContext.Current.CancellationToken)).Document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Shared", entry.Model.Jobs.Single().Name);
        Assert.Equal(("", edited.Text), (saved.Error, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)));
        Assert.NotEqual("", refused.Error);
        Assert.Equal(broken, await File.ReadAllTextAsync(brokenPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SavingAnEditedDocument_WritesItByteForByte()
    {
        // Arrange.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore(new FileWriter());
        var entry = await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken);
        var stage = entry.Model.Stages.Single();

        // Act.
        PipelineBodyEdits.Rename(entry.Document, stage.Id, "Build it");
        var refusal = await store.SaveAsync(Root, path, entry, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", refusal.Error);
        Assert.Equal(entry.Document.Text, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Contains("displayName: Build it", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SavingRebuildsTheModel_SoTheNextReaderSeesTheEdit()
    {
        // Arrange.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore(new FileWriter());
        var entry = await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken);
        PipelineBodyEdits.Rename(entry.Document, entry.Model.Stages.Single().Id, "Build it");

        // Act.
        await store.SaveAsync(Root, path, entry, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Build it", (await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken)).Model.Stages.Single().DisplayName);
    }

    [Fact]
    public async Task SavingTellsTheSessionsOnThatDocument()
    {
        // Arrange.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore(new FileWriter());
        var entry = await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken);
        PipelineBodyEdits.Rename(entry.Document, entry.Model.Stages.Single().Id, "Build it");
        var announced = new List<string>();
        store.Changed += (_, args) => announced.Add(args.Path);

        // Act.
        await store.SaveAsync(Root, path, entry, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal([path], announced);
        Assert.Equal("Build it", (await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken)).Model.Stages.Single().DisplayName);
    }

    [Fact]
    public async Task ATouch_TellsTheSessionsWithoutChangingAnything()
    {
        // Arrange: a change to how a pipeline is drawn rather than to what it says.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore(new FileWriter());
        await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken);
        var announced = 0;
        store.Changed += (_, _) => announced++;

        // Act.
        await store.TouchAsync(Root, path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(1, announced);
        Assert.Equal(Simple, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AReload_PicksUpAnExternalEdit()
    {
        // Arrange: somebody edited the file in their editor while the diagram was open.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore(new FileWriter());
        await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, Simple.Replace("stage: Build", "stage: Rebuilt", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        var announced = new List<string>();
        store.Changed += (_, args) => announced.Add(args.Path);

        // Act.
        await store.ReloadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal([path], announced);
        Assert.Equal("Rebuilt", (await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken)).Model.Stages.Single().Name);
    }

    [Fact]
    public async Task AReload_PicksUpAnEditToATemplateToo()
    {
        // Arrange: what changed on disk may not have been this file at all. A resolver still
        // serving what it read before would show the pipeline as it used to be.
        var path = Write("azure-pipelines.yml", "jobs:\n  - template: templates/shared.yml\n");
        var template = Write("templates/shared.yml", "jobs:\n  - job: Before\n    steps:\n      - script: x\n");
        var store = new PipelineDocumentStore(new FileWriter());
        Assert.Equal("Before", (await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken)).Model.Jobs.Single().Name);

        // Act.
        await File.WriteAllTextAsync(template, "jobs:\n  - job: After\n    steps:\n      - script: x\n", TestContext.Current.CancellationToken);
        await store.ReloadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("After", (await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken)).Model.Jobs.Single().Name);
    }

    [Fact]
    public async Task ForgettingADocument_MakesTheNextOpenReadItAfresh()
    {
        // Arrange.
        var path = Write("azure-pipelines.yml", Simple);
        var store = new PipelineDocumentStore(new FileWriter());
        var first = await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken);

        // Act.
        store.Forget(path);
        var second = await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotSame(first.Document, second.Document);
    }

    [Fact]
    public async Task TwoPipelinesSharingATemplate_ShareOneResolver()
    {
        // Arrange: the cache lives in the resolver, so one resolver per workspace is what stops a
        // template shared by twenty pipelines being read twenty times. Proven the same way the
        // resolver's own test proves it - by rewriting the file between the two reads.
        var one = Write("one.yml", "jobs:\n  - template: templates/shared.yml\n");
        var two = Write("two.yml", "jobs:\n  - template: templates/shared.yml\n");
        var template = Write("templates/shared.yml", "jobs:\n  - job: Before\n    steps:\n      - script: x\n");
        var store = new PipelineDocumentStore(new FileWriter());

        // Act.
        await store.GetOrLoadAsync(Root, one, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(template, "jobs:\n  - job: After\n    steps:\n      - script: x\n", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Before", (await store.GetOrLoadAsync(Root, two, TestContext.Current.CancellationToken)).Model.Jobs.Single().Name);
    }

    [Fact]
    public async Task ATemplateThatCannotBeFollowed_IsCarriedOnTheModelRatherThanFailingTheLoad()
    {
        // Arrange: a gap in the diagram, not the end of it.
        var path = Write("azure-pipelines.yml", "stages:\n  - template: elsewhere.yml@other\n");
        var store = new PipelineDocumentStore(new FileWriter());

        // Act.
        var entry = await store.GetOrLoadAsync(Root, path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(entry.IsUsable);
        Assert.Equal(PipelineTemplateUnresolvedReason.OtherRepository, Assert.Single(entry.Model.Unresolved).Reason);
    }
}
