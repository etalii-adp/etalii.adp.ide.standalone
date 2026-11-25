using EtAlii.Adp.Specification.Fbl.Links;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Links;

/// <summary>The link's grammar (FBL 17.1, spec 089): its parts split at their separators first and unescaped after.</summary>
public class FblLinkTests
{
    [Fact]
    public void ALinkToAnElement_HasEveryPart()
    {
        var link = FblLink.Parse("$k3f9x2ma@provinces.yaml#nh.Name=Noord-Holland");

        Assert.Equal(new FblLink("k3f9x2ma", "provinces.yaml", "nh", "Name", "Noord-Holland"), link);
    }

    [Fact]
    public void ALinkToADocument_HasNoElement()
    {
        Assert.Equal(new FblLink("d7n2k4qp", "services.dgr", null, null, null), FblLink.Parse("$d7n2k4qp@services.dgr"));
    }

    [Fact]
    public void ANameKeepsItsDotsAndEqualsSigns_AndEscapesAreReadInEitherCase()
    {
        var link = FblLink.Parse("$a%2eb@my%23file.tml#e%3D1=v1.2 = final 100%25");

        Assert.Equal(new FblLink("a.b", "my#file.tml", "e=1", null, "v1.2 = final 100%"), link);
    }

    [Fact]
    public void AnEscapedMultiByteCharacter_IsReadAsUtf8()
    {
        Assert.Equal("Zürich", FblLink.Parse("$a@b.yaml#c=Z%C3%BCrich")!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("k3f9x2ma@provinces.yaml")]
    [InlineData("$k3f9x2ma")]
    [InlineData("$@provinces.yaml")]
    [InlineData("$k3f9x2ma@")]
    [InlineData("$k3f9x2ma@provinces.yaml#")]
    [InlineData("$k3f9x2ma@provinces.yaml#nh.=x")]
    [InlineData("$k3f9x2ma@provinces.yaml#nh=bad%2")]
    [InlineData("$k3f9x2ma@provinces.yaml#nh=%FF")]
    [InlineData("$k3$f9@provinces.yaml")]
    [InlineData("$k3f9x2ma@provinces.yaml#n\u0001h")]
    public void ATextThatDoesNotParse_IsNoLink(string text)
    {
        Assert.Null(FblLink.Parse(text));
    }

    [Theory]
    [InlineData("$k3f9x2ma@provinces.yaml#nh.Name=Noord-Holland")]
    [InlineData("$d7n2k4qp@services.dgr")]
    [InlineData("$a%2Eb@my%23file.tml#e%3D1=v1.2 = final 100%25")]
    [InlineData("$m8c1u4wa@ideas.mm#ID_2.text=Z\u00fcrich \U0001F680")]
    public void ALink_IsWrittenAsItWasRead_EachPartEscapedOnlyWhereACharacterWouldEndIt(string text)
    {
        Assert.Equal(text, FblLink.Parse(text)!.Text);
    }

    [Fact]
    public void AnEscapeWrittenInLowerCase_IsWrittenInUpperCase()
    {
        Assert.Equal("$a%2Eb@x.yaml", FblLink.Parse("$a%2eb@x.yaml")!.Text);
    }
}
