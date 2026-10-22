using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;

/// <summary>
/// The <c>.fdg</c> case of the module cross-check (FBL implementation Requirement 11.6;
/// functional-decomposition-graph-disl-fbl Amendment 2): the ids per type the module's binding reads from every
/// <c>.fdg</c> file under <c>src/</c> are the ones <see cref="FdgParser"/> reads, every difference listed in
/// <c>RealFiles/divergences.json</c> of <c>EtAlii.Adp.Specification.Fbl.Tests</c> and never tuned away.
/// </summary>
/// <remarks>
/// It lives in the module's test project, as every cross-check does since code-structure T287, so that no core
/// project references a tool module. The binding is the module's own, embedded one, because etalii.adp vendors no
/// <c>.fdg</c> binding, and the parser stays in the product code until task 10 moves it here.
/// </remarks>
public class FdgCrossCheckTests
{
    /// <summary>The binding name a divergence of this check is listed under.</summary>
    private const string Key = "functional-decomposition-graph";

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var file in RealFileCorpus.All.Where(path => path.EndsWith(".fdg", StringComparison.Ordinal))) data.Add(file);
        return data;
    }

    [Fact]
    public void TheCorpusHoldsTheModulesDocuments() => Assert.True(Files().Count >= 20, $"Only {Files().Count} .fdg files were found under src/.");

    [Theory]
    [MemberData(nameof(Files))]
    public async Task TheBindingReadsTheIdsTheParserReads(string file)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(RealFileCorpus.FullPath(file), TestContext.Current.CancellationToken);

        // Act.
        var difference = ModuleCrossCheck.Difference(Read(text), Parser(text));

        // Assert.
        Divergences.Check("cross-check", Key, file, difference);
    }

    /// <summary>The ids each type's entries are written with, as the binding reads them; nothing for a body it cannot read.</summary>
    private static Dictionary<string, SortedSet<string>> Read(string text)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var body = FdgBody.Parse(text);
        if (body.IsUnreadable) return ids;
        foreach (var element in body.Model.Elements.Where(element => FdgReading.IsElement(element) || FdgReading.IsConnection(element)))
        {
            var type = element.Type is FdgReading.UnknownElement or FdgReading.UnknownConnection
                ? FdgReading.TextOf(element, "documentType") ?? ""
                : FdgReading.DocumentTypeOf(element.Type);
            if (FdgReading.Written(element) is { Length: > 0 } id) ModuleCrossCheck.Add(ids, type, id);
        }

        return ids;
    }

    /// <summary>The ids each type's entries are written with, as the parser reads them.</summary>
    private static Dictionary<string, SortedSet<string>> Parser(string text)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var model = FdgParser.Parse(LineDocument.Parse(text));
        foreach (var element in model.Elements.Where(element => element.Id.Length > 0)) ModuleCrossCheck.Add(ids, element.Type, element.Id);
        foreach (var connection in model.Connections.Where(connection => connection.Id.Length > 0)) ModuleCrossCheck.Add(ids, connection.Type, connection.Id);
        return ids;
    }
}
