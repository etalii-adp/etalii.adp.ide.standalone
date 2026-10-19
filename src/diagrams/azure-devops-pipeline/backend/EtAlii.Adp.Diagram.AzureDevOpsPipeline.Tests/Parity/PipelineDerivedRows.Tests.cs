using EtAlii.Adp.Context;
using Xunit;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;

/// <summary>
/// The property rows derived from the DISL definition equal the hand-written ones
/// (azure-devops-pipeline-disl-fbl task 6), row for row and candidate for candidate: for every document of
/// the parity corpus, every stage, job and step, every element the mapper draws, the empty id and an
/// unknown id, readable and read-only.
/// </summary>
public class PipelineDerivedRowsTests
{
    public static TheoryData<string> Documents() => [.. PipelineReadings.Paths()];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryRow_IsTheHandWrittenOne(string path)
    {
        // Arrange.
        using var reading = await PipelineReadings.OpenAsync(path, TestContext.Current.CancellationToken);

        // Act and assert.
        foreach (var target in reading.Targets)
        {
            foreach (var entry in reading.Entries)
            {
                var expected = HandWrittenPipeline.Rows(entry, target).Select(Text).ToList();
                var actual = PipelineDefinition.Rows(entry, target).Select(Text).ToList();
                Assert.True(
                    expected.SequenceEqual(actual),
                    $"{path} {target} (read-only: {!entry.IsUsable}):\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
            }
        }
    }

    private static string Text(ContextPropertyDefinition row) =>
        $"{row.Id} | {row.Label} | {row.Value.Replace("\n", "\\n", StringComparison.Ordinal)} | {row.Editor} | {row.ReadOnlyReason} | {row.Group} | {(row.Candidates is null ? "null" : "[" + string.Join("; ", row.Candidates) + "]")}";
}
