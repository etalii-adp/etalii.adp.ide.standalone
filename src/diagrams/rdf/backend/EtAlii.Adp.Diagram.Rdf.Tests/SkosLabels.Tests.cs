using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The label rule as a table (skos-diagram Requirement 3): the approved preference order, the
/// within-tag smallest-literal tie-break, the fallback chain and the chip data - exhaustive,
/// because a wrong label rule makes this diagram useless rather than imperfect.
/// </summary>
public class SkosLabelsTests
{
    private static SkosLabel Label(SkosLabelSource source, string text, string? language) =>
        new(source, text, language, null!);

    public static TheoryData<string, SkosLabel[], string, string, SkosLabelKind> Table => new()
    {
        // The display language wins over everything.
        { "nl", new[] { Label(SkosLabelSource.Preferred, "Tea", "en"), Label(SkosLabelSource.Preferred, "Thee", "nl") }, "Thee", "nl", SkosLabelKind.Preferred },
        // Then en.
        { "nl", new[] { Label(SkosLabelSource.Preferred, "Tee", "de"), Label(SkosLabelSource.Preferred, "Tea", "en") }, "Tea", "en", SkosLabelKind.Preferred },
        // Then the untagged literal.
        { "nl", new[] { Label(SkosLabelSource.Preferred, "Tee", "de"), Label(SkosLabelSource.Preferred, "Tea", null) }, "Tea", "", SkosLabelKind.Preferred },
        // Then the lexicographically smallest remaining tag.
        { "nl", new[] { Label(SkosLabelSource.Preferred, "Te", "fi"), Label(SkosLabelSource.Preferred, "Tee", "de") }, "Tee", "de", SkosLabelKind.Preferred },
        // Within one tag, the lexicographically smallest literal - the S14 tie-break,
        // deliberately arbitrary and deliberately stable.
        { "en", new[] { Label(SkosLabelSource.Preferred, "Zebra", "en"), Label(SkosLabelSource.Preferred, "Aardvark", "en") }, "Aardvark", "en", SkosLabelKind.Preferred },
        // Tags compare case-insensitively: the projection lowercases per BCP 47.
        { "EN", new[] { Label(SkosLabelSource.Preferred, "Tea", "en") }, "Tea", "en", SkosLabelKind.Preferred },
        // No preferred label in any language: an alternate stands in, by the same order, marked.
        { "nl", new[] { Label(SkosLabelSource.Alternate, "Cuppa", "en"), Label(SkosLabelSource.Alternate, "Bakkie", "nl") }, "Bakkie", "nl", SkosLabelKind.Alternate },
        // A preferred label in ANY language beats every alternate.
        { "nl", new[] { Label(SkosLabelSource.Alternate, "Bakkie", "nl"), Label(SkosLabelSource.Preferred, "Chai", "hi") }, "Chai", "hi", SkosLabelKind.Preferred },
        // Hidden labels never display, even when they are all there is.
        { "en", new[] { Label(SkosLabelSource.Hidden, "misspellling", "en") }, "ex:tea", "", SkosLabelKind.IriFallback },
        // No label at all: the IRI's display form, dimmed - and validation reports it.
        { "en", Array.Empty<SkosLabel>(), "ex:tea", "", SkosLabelKind.IriFallback },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Choose_FollowsTheApprovedPreferenceOrder(
        string displayLanguage, SkosLabel[] labels, string text, string tag, SkosLabelKind kind)
    {
        // Act.
        var chosen = SkosLabels.Choose(labels, displayLanguage, "ex:tea");

        // Assert.
        Assert.Equal(new SkosChosenLabel(text, tag, kind), chosen);
    }

    [Fact]
    public void Choose_IsDeterministic_WhateverTheInputOrder()
    {
        // Arrange: the same labels in two orders.
        var forward = new[]
        {
            Label(SkosLabelSource.Preferred, "Zebra", "en"),
            Label(SkosLabelSource.Preferred, "Aardvark", "en"),
            Label(SkosLabelSource.Preferred, "Thee", "nl"),
        };
        var backward = forward.Reverse().ToArray();

        // Act & assert.
        Assert.Equal(SkosLabels.Choose(forward, "en", "x"), SkosLabels.Choose(backward, "en", "x"));
    }

    [Fact]
    public void TheChipRule_IsData_NotClientLogic()
    {
        // Arrange & act: the chosen tag rides the result; the canvas compares it to the display
        // language and shows a chip when a non-empty tag differs - an untagged label is
        // language-neutral and wears none (Requirement 3.3).
        var gap = SkosLabels.Choose([Label(SkosLabelSource.Preferred, "Tea", "en")], "nl", "x");
        var untagged = SkosLabels.Choose([Label(SkosLabelSource.Preferred, "Tea", null)], "nl", "x");

        // Assert.
        Assert.Equal("en", gap.LanguageTag);       // differs from nl: chip.
        Assert.Equal("", untagged.LanguageTag);    // neutral: no chip.
    }
}
