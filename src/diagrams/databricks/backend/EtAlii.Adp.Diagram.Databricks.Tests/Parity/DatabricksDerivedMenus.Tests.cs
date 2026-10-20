using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests.Parity;

/// <summary>
/// The palettes, the context menus, the removal's confirmation and a dropped task's key derived from the three DISL
/// definitions equal the hand-written ones (databricks-disl-fbl task 6, Requirements 3.1, 3.2 and 3.5), record for
/// record: for every document of the parity corpus, every reading the transcript records, and every element id that
/// reading draws, the empty id, a placement, two connect gestures and an unknown id, readable and read-only.
/// </summary>
public class DatabricksDerivedMenusTests
{
    public static TheoryData<string> Documents() => DatabricksParityCorpus.Documents();

    public static TheoryData<string> Origins() => [.. DatabricksParityCorpus.Origins.Select(origin => origin.Key)];

    [Theory]
    [MemberData(nameof(Origins))]
    public void ThePalette_IsTheHandWrittenOne(string origin)
    {
        // Arrange.
        var tool = DatabricksParityCorpus.Origins.Single(candidate => candidate.Key == origin);

        // Act.
        var derived = new DatabricksToolboxProvider(tool).Items;

        // Assert.
        Assert.Equal(HandWrittenDatabricks.Toolbox(tool), derived);
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryMenu_IsTheHandWrittenOne(string document)
    {
        // Arrange.
        using var corpus = new DatabricksParityCorpus();
        var readable = await DatabricksParityCorpus.EntryAsync(document, TestContext.Current.CancellationToken);
        var readOnly = readable with { Unreadable = "parity: read-only" };
        var compared = 0;

        // Act and assert.
        foreach ((string label, ToolOrigin origin, string? registration) in await corpus.OpeningsAsync(readable.Model, TestContext.Current.CancellationToken))
        {
            foreach (var elementId in DatabricksParityCorpus.Targets(readable.Model, origin, registration))
            {
                var target = DatabricksParityCorpus.Target(elementId, origin, registration);
                foreach (var entry in new[] { readable, readOnly })
                {
                    var expected = HandWrittenDatabricks.Menus(target, entry);
                    var actual = DatabricksDefinition.Menus(target, entry);
                    Assert.True(
                        expected.Select(group => group.Actions).SequenceEqual(actual.Select(group => group.Actions), GroupComparer.Instance),
                        $"{document} | {label} | {elementId} (read-only: {!entry.IsUsable}):\nexpected {Text(expected)}\nactual   {Text(actual)}");
                    compared += expected.Count;
                }
            }
        }

        // A file that does not parse offers nothing under either; every other offers something to compare.
        Assert.True(compared > 0 || !readable.IsUsable, $"{document}: no menu had anything to compare.");
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryRemovalConfirmation_IsTheHandWrittenOne(string document)
    {
        // Arrange.
        using var corpus = new DatabricksParityCorpus();
        var entry = await DatabricksParityCorpus.EntryAsync(document, TestContext.Current.CancellationToken);

        // Act and assert.
        foreach ((string label, ToolOrigin origin, string? registration) in await corpus.OpeningsAsync(entry.Model, TestContext.Current.CancellationToken))
        {
            foreach (var elementId in DatabricksParityCorpus.Targets(entry.Model, origin, registration))
            {
                var target = DatabricksParityCorpus.Target(elementId, origin, registration);
                Assert.True(
                    Equals(HandWrittenDatabricks.RemoveConfirmation(target, entry.Model), DatabricksDefinition.RemoveConfirmation(target, entry.Model)),
                    $"{document} | {label} | {elementId}:\nexpected {HandWrittenDatabricks.RemoveConfirmation(target, entry.Model)}\nactual   {DatabricksDefinition.RemoveConfirmation(target, entry.Model)}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryDroppedTaskKey_IsTheHandWrittenFreshKey(string document)
    {
        // Arrange.
        var entry = await DatabricksParityCorpus.EntryAsync(document, TestContext.Current.CancellationToken);

        // Act and assert: the three the palette drops, and one only a command reaches.
        foreach (var job in entry.Model.Jobs)
        {
            foreach (var type in (string[])["notebook", "python", "condition", "jar"])
            {
                Assert.Equal(
                    HandWrittenDatabricks.FreshKey(job, type),
                    DatabricksDefinition.DroppedTaskKey(job, $"{DatabricksContextActionProvider.AddTaskActionPrefix}{type}"));
            }
        }
    }

    /// <summary>
    /// The entry finding A12 is struck on (Requirement 10.4): a dependency's disconnect is mapped to an id built from
    /// the key it names, <c>databricks.disconnect:ingest</c>, with no library change, once per upstream, in the file's
    /// order, labelled with the key as Requirement 3.2 reads it.
    /// </summary>
    [Fact]
    public async Task ADependencysDisconnect_IsTheIdBuiltFromTheKeyItNames()
    {
        // Arrange.
        var entry = await DatabricksParityCorpus.EntryAsync("job.yml", TestContext.Current.CancellationToken);
        var dependent = entry.Model.Jobs.Single().Tasks.First(task => task.DependsOn.Any(dependency => dependency.TaskKey == "ingest"));
        var target = DatabricksParityCorpus.Target($"task:{dependent.Key}", ServiceCollectionAddDatabricksExtension.JobOrigin, null);

        // Act.
        var disconnects = DatabricksDefinition.Menus(target, entry)
            .SelectMany(group => group.Actions)
            .Where(action => action.Id.StartsWith(DatabricksContextActionProvider.DisconnectActionPrefix, StringComparison.Ordinal))
            .ToList();

        // Assert.
        Assert.Equal(dependent.DependsOn.Select(dependency => $"databricks.disconnect:{dependency.TaskKey}"), disconnects.Select(action => action.Id));
        var ingest = Assert.Single(disconnects, action => action.Id == "databricks.disconnect:ingest");
        Assert.Equal("Disconnect from 'ingest'", ingest.Label);
        Assert.Equal("mdi-vector-polyline-remove", ingest.Icon);
    }

    private static string Text(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        string.Join(" / ", groups.Select(group => string.Join(", ", group.Actions)));

    private sealed class GroupComparer : IEqualityComparer<IReadOnlyList<ContextActionDefinition>>
    {
        public static GroupComparer Instance { get; } = new();

        public bool Equals(IReadOnlyList<ContextActionDefinition>? x, IReadOnlyList<ContextActionDefinition>? y) => x!.SequenceEqual(y!);

        public int GetHashCode(IReadOnlyList<ContextActionDefinition> obj) => obj.Count;
    }
}
