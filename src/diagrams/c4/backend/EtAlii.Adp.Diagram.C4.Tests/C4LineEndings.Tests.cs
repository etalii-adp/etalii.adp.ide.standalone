using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// How c4's edits end their lines now that it reads its documents through the core
/// <see cref="LineDocument"/> (code-structure T128). Its own line document chose differently in
/// two of these, and the core's behaviour won by the ruling on the private line documents
/// (behaviour changes B-05 and B-16).
/// </summary>
public class C4LineEndingsTests
{
    [Fact]
    public void AFileWithEqualCountsOfLfAndCrlf_RoundTrips_AndANewLineTakesCrlf()
    {
        // Arrange.
        const string text = "workspace {\n    model {\r\n        user = person \"User\"\n    }\r\n";
        var document = LineDocument.Parse(text);

        // Act.
        var unchanged = document.Text;
        document.InsertLine(4, "        system = softwareSystem \"System\"");

        // Assert.
        Assert.Equal(text, unchanged);
        Assert.Equal(
            "workspace {\n    model {\r\n        user = person \"User\"\n        system = softwareSystem \"System\"\r\n    }\r\n",
            document.Text);
    }

    [Fact]
    public void RemovingAnUnterminatedLastLine_LeavesTheFileUnterminated()
    {
        // Arrange.
        // An undo of an append to a file that ended without a newline removes the line the
        // append added, and the file must come back exactly as it was.
        var document = LineDocument.Parse("workspace {\r\n}\r\nviews");

        // Act.
        document.RemoveLines(3, 3);

        // Assert.
        Assert.Equal("workspace {\r\n}", document.Text);
    }

    [Fact]
    public void AFileWithNoLineBreaks_TakesCrlf_WhateverThePlatform()
    {
        // Arrange.
        // Nothing in the file says which ending to use, so the repository's house style decides
        // rather than the machine the edit happens to be made on.
        var document = LineDocument.Parse("workspace {");

        // Act.
        document.InsertLine(2, "}");

        // Assert.
        Assert.Equal("workspace {\r\n}", document.Text);
    }
}
