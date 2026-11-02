using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// What ADP did not change keeps every byte, and what it changed comes back byte for byte when undone, for every
/// fixture, the pair of line-ending fixtures and the one without a final newline among them (c4-disl-fbl task 15,
/// Requirements 5.6 and 5.7).
/// </summary>
public sealed class C4RoundTripTests : IDisposable
{
    private const string Value = "Round \"trip\"";

    private readonly TestFolder _testFolder = TestFolder.Create("c4-round-trip");
    private readonly ServiceProvider _services;
    private readonly IHistoryStack _history;
    private readonly IC4DocumentStore _documents;

    public C4RoundTripTests()
    {
        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_testFolder.Path));
        _documents = _services.GetRequiredService<IC4DocumentStore>();
    }

    public void Dispose()
    {
        _services.Dispose();
        _testFolder.Dispose();
    }

    /// <summary>Every <c>.dsl</c> fixture, by its path under <c>Fixtures</c>.</summary>
    public static TheoryData<string> AllFixtures() =>
        [.. Directory.GetFiles("Fixtures", "*.dsl", SearchOption.AllDirectories).Select(path => IoPath.GetRelativePath("Fixtures", path)).Order(StringComparer.Ordinal)];

    [Fact]
    public void TheCorpus_HoldsTheLineEndingPairAndTheDocumentWithoutAFinalNewline()
    {
        // Act.
        var fixtures = AllFixtures().Select(row => row.Data).ToList();

        // Assert.
        Assert.Contains("crlf-line-endings.dsl", fixtures);
        Assert.Contains("lf-line-endings.dsl", fixtures);
        Assert.Contains("no-trailing-newline.dsl", fixtures);
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task AnUnchangedDocument_IsWrittenBackByteIdentical(string fixture)
    {
        // Arrange.
        (string path, byte[] original) = await PlaceAsync(fixture);
        var entry = await _documents.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Act.
        await _documents.SaveAsync(path, entry.Document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task ASetToTheValueAlreadyThere_ChangesNoByte(string fixture)
    {
        // Arrange.
        (string path, byte[] original) = await PlaceAsync(fixture);
        var workspace = (await _documents.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Model;
        if (workspace.Elements.FirstOrDefault(candidate => candidate.Description.Length > 0) is not { } described)
        {
            return;
        }

        // Act.
        var result = await _history.ExecuteAsync(new SetElementDescriptionCommand(path, described.Id, described.Description), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task EveryEditUndone_RestoresEveryByte(string fixture)
    {
        // Arrange.
        (string path, byte[] original) = await PlaceAsync(fixture);
        var workspace = (await _documents.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Model;
        List<ICommand> edits = [new AddC4ElementCommand(path, C4ElementKind.Person, "Round trip")];
        if (workspace.Elements.FirstOrDefault(element => element.Kind == C4ElementKind.SoftwareSystem) is { } system)
        {
            edits.Add(new AddC4ElementCommand(path, C4ElementKind.Container, "Round trip", system.Id));
            edits.Add(new SetElementDescriptionCommand(path, system.Id, Value));
        }

        if (workspace.Relationships.FirstOrDefault() is { } relationship)
        {
            edits.Add(new SetRelationshipTechnologyCommand(path, relationship.Id, Value));
        }

        foreach (var edit in edits)
        {
            // Act.
            var done = await _history.ExecuteAsync(edit, TestContext.Current.CancellationToken);
            var changed = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            var undone = await _history.UndoAsync(TestContext.Current.CancellationToken);

            // Assert.
            if (!done.IsSuccess)
            {
                // A refused edit wrote nothing, and there was nothing to undo.
                Assert.Equal(original, changed);
                continue;
            }

            Assert.NotEqual(original, changed);
            Assert.True(undone.IsSuccess, $"{edit}: {undone.Error}");
            Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        }
    }

    private async Task<(string Path, byte[] Bytes)> PlaceAsync(string fixture)
    {
        var bytes = await File.ReadAllBytesAsync(IoPath.Combine("Fixtures", fixture), TestContext.Current.CancellationToken);
        var path = IoPath.Combine(_testFolder.Path, IoPath.GetFileName(fixture));
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        return (path, bytes);
    }
}
