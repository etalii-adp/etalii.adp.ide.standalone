using System.Text;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>A text read lately is found by what it holds, and only the last few are kept.</summary>
public class RecentBodiesTests
{
    private static readonly FblBinding Binding = LoadBinding();

    [Fact]
    public void ARememberedBody_IsFoundByItsText_InOtherBytes()
    {
        // Arrange.
        var recent = new RecentBodies();
        var body = Open("items: []\n");
        recent.Remember(body);

        // Act.
        var found = recent.Find("items: []\n"u8.ToArray());

        // Assert.
        Assert.Same(body, found);
        Assert.Null(recent.Find("items: [] \n"u8.ToArray()));
    }

    [Fact]
    public void TheOldest_IsForgotten_BeyondTheLastEight()
    {
        // Arrange.
        var recent = new RecentBodies();
        var texts = Enumerable.Range(0, 9).Select(index => $"# {index}\nitems: []\n").ToList();

        // Act.
        foreach (var text in texts)
        {
            recent.Remember(Open(text));
        }

        // Assert.
        Assert.Null(recent.Find(Encoding.UTF8.GetBytes(texts[0])));
        Assert.All(texts.Skip(1), text => Assert.NotNull(recent.Find(Encoding.UTF8.GetBytes(text))));
    }

    private static OpenBody Open(string text) => OpenBody.Open(Encoding.UTF8.GetBytes(text), Binding, new FblOptions { FileName = "body.t" });

    private static FblBinding LoadBinding()
    {
        const string document = """
            { "fbl": "0.1", "bindings": { "t": {
                "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared",
                "elements": [{ "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } }, "attributes": { "label": { "key": "label" } } }] } } }
            """;
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(document), out var loaded);
        return loaded?.Bindings["t"] ?? throw new InvalidOperationException(string.Join("; ", problems));
    }
}
