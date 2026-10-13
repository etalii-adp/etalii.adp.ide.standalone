using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests;

/// <summary>
/// Where the caret is, decided from the text around it (xml-editor task 8): the kind of place,
/// what a completion there replaces, and the places where nothing is to be offered.
/// </summary>
public class CaretContextTests
{
    [Theory]
    [InlineData("<library>\n  |", nameof(CaretKind.Content))]
    [InlineData("<library>\n  <|", nameof(CaretKind.ElementName))]
    [InlineData("<library>\n  <bo|", nameof(CaretKind.ElementName))]
    [InlineData("<library>\n  <book |", nameof(CaretKind.AttributeName))]
    [InlineData("<library>\n  <book format=\"e|\"", nameof(CaretKind.AttributeValue))]
    [InlineData("<library>\n  <book title=\"a>b\" |", nameof(CaretKind.AttributeName))]
    [InlineData("<library>\n  <lent>tr|", nameof(CaretKind.Content))]
    public void TheKindOfPlace_IsToldFromTheText(string marked, string expected)
    {
        // Arrange.
        (string text, int offset) = Caret(marked);

        // Act.
        var caret = CaretContext.At(text, offset);

        // Assert.
        Assert.Equal(expected, caret.Kind.ToString());
    }

    [Theory]
    [InlineData("<library>\n  <!-- a <book |")]
    [InlineData("<library>\n  <![CDATA[ <book |")]
    [InlineData("<?xml-model |")]
    [InlineData("<library>\n  </|")]
    [InlineData("<!DOCTYPE |")]
    [InlineData("<library>\n  <book format=|")]
    public void APlaceWhoseOwnSyntaxHidesTheDocument_IsUnknown(string marked)
    {
        // Arrange.
        (string text, int offset) = Caret(marked);

        // Act.
        var caret = CaretContext.At(text, offset);

        // Assert: Requirement 7.7 - nothing is guessed there.
        Assert.Equal(CaretKind.Unknown, caret.Kind);
    }

    [Fact]
    public void AName_IsReplacedWhole_AndAWrittenTagIsSaidToBeWritten()
    {
        // Arrange: the caret in the middle of a name whose tag is already closed.
        (string text, int offset) = Caret("<library>\n  <bo|ok isbn=\"1\">\n");

        // Act.
        var caret = CaretContext.At(text, offset);

        // Assert.
        Assert.Equal("book", text[caret.From..caret.To]);
        Assert.Equal("<library>\n  ".Length, caret.Cut);
        Assert.True(caret.IsWrittenTag);
    }

    [Fact]
    public void ANameBeingTyped_IsNotAWrittenTag()
    {
        // Arrange.
        (string text, int offset) = Caret("<library>\n  <bo|\n</library>\n");

        // Act.
        var caret = CaretContext.At(text, offset);

        // Assert.
        Assert.False(caret.IsWrittenTag);
        Assert.Equal("bo", text[caret.From..caret.To]);
    }

    [Fact]
    public void AnAttributeValue_IsReplacedWhole_AndTheAttributesOnBothSidesAreRead()
    {
        // Arrange.
        (string text, int offset) = Caret("<book isbn=\"1\" format=\"pa|per\" lent=\"x\">");

        // Act.
        var caret = CaretContext.At(text, offset);

        // Assert.
        Assert.Equal("book", caret.ElementName);
        Assert.Equal("format", caret.AttributeName);
        Assert.Equal("paper", text[caret.From..caret.To]);
        Assert.Equal(["isbn", "format", "lent"], caret.Attributes.Select(attribute => attribute.Name));
    }

    [Fact]
    public void AValueLeftOpen_EndsAtItsLine_AndDoesNotSwallowTheRestOfTheDocument()
    {
        // Arrange.
        (string text, int offset) = Caret("<book format=\"pa|\n  <title>t</title>\n</book>\n");

        // Act.
        var caret = CaretContext.At(text, offset);

        // Assert.
        Assert.Equal(nameof(CaretKind.AttributeValue), caret.Kind.ToString());
        Assert.Equal("pa", text[caret.From..caret.To]);
    }

    private static (string Text, int Offset) Caret(string marked)
    {
        var offset = marked.IndexOf('|', StringComparison.Ordinal);
        return (marked.Remove(offset, 1), offset);
    }
}
