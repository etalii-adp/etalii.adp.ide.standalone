using System.Text;
using Xunit;

namespace EtAlii.Adp.Designer.Tests;

/// <summary>
/// The envelope of a designer's Markdown document, line 1 (DED 0.2 sections 2 and 4; hierarchical-requirement FR-031):
/// read with any run of spaces and tabs, after a byte-order mark only, and a version this application does not read
/// gives the reason the document is read-only.
/// </summary>
public class DedEnvelopeTests
{
    [Fact]
    public void Read_TheLineAsWritten_GivesTheVersionAndTheOrigin()
    {
        // Act.
        var envelope = DedEnvelope.Read("<!-- ded: 0.2 designer: etalii/hierarchical-requirement -->\r\n# Title\r\n"u8.ToArray());

        // Assert.
        Assert.NotNull(envelope);
        Assert.Equal("0.2", envelope.Version);
        Assert.Equal("etalii/hierarchical-requirement", envelope.Designer);
        Assert.Equal("", envelope.ReadOnlyReason);
    }

    [Fact]
    public void Read_TabsSpacesAndAByteOrderMark_AreRead()
    {
        // Act.
        var envelope = DedEnvelope.Read([0xEF, 0xBB, 0xBF, .. "<!--\tded:  0.1\tdesigner: etalii/hierarchical-requirement   -->  \n"u8]);

        // Assert.
        Assert.Equal(new DedEnvelope("0.1", "etalii/hierarchical-requirement"), envelope);
    }

    [Theory]
    [InlineData("# Title\n<!-- ded: 0.2 designer: etalii/hierarchical-requirement -->\n")]
    [InlineData(" <!-- ded: 0.2 designer: etalii/hierarchical-requirement -->\n")]
    [InlineData("<!-- ded: 0.2 designer: etalii/hierarchical-requirement --> text\n")]
    [InlineData("<!-- designer: etalii/hierarchical-requirement ded: 0.2 -->\n")]
    [InlineData("")]
    public void Read_ALineThatIsNoEnvelope_GivesNone(string text)
    {
        // Act and assert.
        Assert.Null(DedEnvelope.Read(Encoding.UTF8.GetBytes(text)));
    }

    [Theory]
    [InlineData("0.3")]
    [InlineData("1.0")]
    public void ALaterVersion_IsReadOnly_WithTheVersionInTheReason(string version)
    {
        // Act.
        var reason = new DedEnvelope(version, "etalii/hierarchical-requirement").ReadOnlyReason;

        // Assert: the reason names the version and the one this application reads.
        Assert.Contains(version, reason, StringComparison.Ordinal);
        Assert.Contains("0.2", reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.1")]
    [InlineData("0.2")]
    public void AKnownVersion_IsWritable(string version)
    {
        // Act and assert.
        Assert.Equal("", new DedEnvelope(version, "etalii/hierarchical-requirement").ReadOnlyReason);
    }
}
