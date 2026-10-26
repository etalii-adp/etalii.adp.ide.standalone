using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>
/// Every document of the parity corpus read through the binding (<see cref="SupplyChainReading"/>) against the module's
/// reader before it (<see cref="SupplyChainParser"/>), supply-chain-disl-fbl Requirement 4.2 to 4.6: the same groups,
/// nodes and flows on the same lines, every text as written and every number as .NET reads it, the same problems in the
/// same order on the same lines, the same version, and the same refusal of a body that is not YAML.
/// </summary>
public class SupplyChainReadingParityTests
{
    public static TheoryData<string> Documents() => SupplyChainCorpus.Names();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryDocument_ReadsThroughTheBinding_AsTheParserReadIt(string name)
    {
        // Arrange.
        var text = await SupplyChainCorpus.TextAsync(name, TestContext.Current.CancellationToken);
        var expected = SupplyChainParser.Outcome(LineDocument.Parse(text));

        // Act.
        var actual = SupplyChainReading.Outcome(text);

        // Assert.
        Assert.Equal(expected.Model.Groups, actual.Model.Groups);
        Assert.Equal(expected.Model.Nodes, actual.Model.Nodes);
        Assert.Equal(expected.Model.Flows, actual.Model.Flows);
        Assert.Equal(expected.Model.Problems, actual.Model.Problems);
        Assert.Equal(expected.Model.Version, actual.Model.Version);
        Assert.Equal(expected.Problems, actual.Problems);
        Assert.Equal(expected.Unreadable, actual.Unreadable);
    }
}
