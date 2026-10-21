using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;

/// <summary>
/// The palette, the context menus and the deletion confirmation derived from the DISL definition equal
/// the hand-written ones (functional-decomposition-graph-disl-fbl task 6, Requirement 3.1), record for
/// record: for every document of the parity corpus and every target its transcript records, readable
/// and read-only.
/// </summary>
public class FdgDerivedMenusTests
{
    public static TheoryData<string> Documents() => FdgCorpus.Documents();

    [Fact]
    public void ThePalette_IsTheHandWrittenOne() => Assert.Equal(HandWrittenFdg.Toolbox, FdgDefinition.Definition.Toolbox);

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryMenu_IsTheHandWrittenOne(string path)
    {
        // Arrange.
        (DocumentEntry<LineDocument, FdgModel> readable, DocumentEntry<LineDocument, FdgModel> readOnly) = FdgCorpus.Entries(path);

        // Act and assert.
        foreach (var target in FdgCorpus.Targets(readable.Model))
        {
            foreach (var entry in new[] { readable, readOnly })
            {
                var expected = HandWrittenFdg.Menus(entry, target);
                var actual = FdgDefinition.Menus(entry, target);
                Assert.True(
                    expected.Select(Text).SequenceEqual(actual.Select(Text)),
                    $"{path} {target} (read-only: {!entry.IsUsable}):\nexpected {string.Join(" / ", expected.Select(Text))}\nactual   {string.Join(" / ", actual.Select(Text))}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryConfirmation_IsTheHandWrittenOne(string path)
    {
        // Arrange.
        (DocumentEntry<LineDocument, FdgModel> readable, DocumentEntry<LineDocument, FdgModel> readOnly) = FdgCorpus.Entries(path);

        // Act and assert.
        foreach (var target in FdgCorpus.Targets(readable.Model))
        {
            foreach (var entry in new[] { readable, readOnly })
            {
                Assert.True(
                    Equals(HandWrittenFdg.Confirmation(entry, target), FdgDefinition.Confirmation(entry, target)),
                    $"{path} {target} (read-only: {!entry.IsUsable}):\nexpected {HandWrittenFdg.Confirmation(entry, target)}\nactual   {FdgDefinition.Confirmation(entry, target)}");
            }
        }
    }

    private static string Text(ContextActionGroupDefinition group) => string.Join(", ", group.Actions);
}
