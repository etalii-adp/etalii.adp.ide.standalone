using EtAlii.Adp.Context;
using Xunit;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;

/// <summary>
/// The palette and the context menus derived from the DISL definition equal the hand-written ones
/// (azure-devops-pipeline-disl-fbl task 6), record for record: for every document of the parity corpus,
/// every stage, job and step, every element the mapper draws, the empty id and an unknown id, with
/// everything closed and everything open, readable and read-only.
/// </summary>
public class PipelineDerivedMenusTests
{
    public static TheoryData<string> Documents() => [.. PipelineReadings.Paths()];

    [Fact]
    public void ThePalette_IsTheHandWrittenOne() => Assert.Equal(HandWrittenPipeline.Toolbox, PipelineDefinition.Toolbox);

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryMenu_IsTheHandWrittenOne(string path)
    {
        // Arrange.
        using var reading = await PipelineReadings.OpenAsync(path, TestContext.Current.CancellationToken);
        var closed = new PipelineConnectionView();
        var open = new PipelineConnectionView();
        foreach (var id in reading.Targets)
        {
            open.Toggle(id);
        }

        // Act and assert.
        foreach (var target in reading.Targets)
        {
            foreach (var entry in reading.Entries)
            {
                foreach (var view in new[] { closed, open })
                {
                    var expected = HandWrittenPipeline.Menus(entry, view, target);
                    var actual = PipelineDefinition.Menus(entry, view, target);
                    Assert.True(
                        expected.Select(group => group.Actions).SequenceEqual(actual.Select(group => group.Actions), GroupComparer.Instance),
                        $"{path} {target} (read-only: {!entry.IsUsable}, open: {ReferenceEquals(view, open)}):\nexpected {Text(expected)}\nactual   {Text(actual)}");
                }
            }
        }
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
