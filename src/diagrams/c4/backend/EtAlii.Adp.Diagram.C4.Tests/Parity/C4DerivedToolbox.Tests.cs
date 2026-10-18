using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// The palette each of the six types derives from its DISL definition equals the one the module
/// wrote by hand, item for item (c4-disl-fbl Requirement 3.1), and is the one its provider hands out.
/// </summary>
public class C4DerivedToolboxTests
{
    public static TheoryData<string> Origins() => [.. C4Definitions.Types.Select(type => type.Origin.Key)];

    [Theory]
    [MemberData(nameof(Origins))]
    public void ThePalette_IsTheHandWrittenOne(string origin)
    {
        // Arrange.
        (ToolOrigin type, C4ViewKind kind, string _) = C4Definitions.Types.Single(each => each.Origin.Key == origin);

        // Act.
        var derived = C4Definitions.Toolbox(type);

        // Assert.
        Assert.Equal(HandWrittenC4.Toolbox(kind), derived);
        Assert.Equal(derived, new C4ToolboxProvider(type).Items);
    }
}
