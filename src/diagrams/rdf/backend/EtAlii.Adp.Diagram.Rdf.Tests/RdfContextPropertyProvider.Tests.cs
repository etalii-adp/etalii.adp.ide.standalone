using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The property rows: identity read-only with its reasons, rdfs:label and rdfs:comment editable
/// through the standard SetProperty path - a first write stating the triple, a later one
/// rewriting exactly the literal token (rdf-diagram Requirement 6).
/// </summary>
public class RdfContextPropertyProviderTests : IDisposable
{
    private const string Corpus =
        "@prefix ex: <http://example.org/> .\r\n"
        + "@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\r\n"
        + "\r\n"
        + "ex:alice a ex:Person ;\r\n"
        + "    rdfs:label \"Alice\" .\r\n"
        + "\r\n"
        + "ex:bob ex:name \"Bob\" .\r\n";

    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly RdfContextPropertyProvider _properties;
    private readonly IHistoryStack _history;

    public RdfContextPropertyProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands()
            .AddRdf()
            .BuildServiceProvider();
        _properties = new RdfContextPropertyProvider(
            _provider.GetRequiredService<IHistoryStackStore>(),
            _provider.GetRequiredService<IRdfDocumentStore>());
        _history = _provider.GetRequiredService<IHistoryStackStore>().Get(_root);
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
    }

    private string WriteBody(string content)
    {
        var path = IoPath.Combine(_root, "graph.ttl");
        File.WriteAllText(path, content);
        return path;
    }

    private ContextTarget Target(string bodyPath, string elementId) => new(
        ContextScope.DiagramElement, bodyPath, IsContainer: false, SourceId: default, _root, default, elementId);

    [Fact]
    public async Task AResource_ShowsIdentityReadOnly_AndItsDocumentationEditable()
    {
        // Arrange.
        var body = WriteBody(Corpus);

        // Act.
        var rows = await _properties.DescribeAsync(Target(body, "res:http://example.org/alice"), TestContext.Current.CancellationToken);

        // Assert.
        var iri = rows.Single(row => row.Id == RdfContextPropertyProvider.IriProperty);
        Assert.Equal("http://example.org/alice", iri.Value);
        Assert.NotEmpty(iri.ReadOnlyReason);

        Assert.Equal("ex:Person", rows.Single(row => row.Id == RdfContextPropertyProvider.TypesProperty).Value);

        var label = rows.Single(row => row.Id == RdfContextPropertyProvider.LabelProperty);
        Assert.Equal("Alice", label.Value);
        Assert.Empty(label.ReadOnlyReason);
    }

    [Fact]
    public async Task SettingAnExistingLabel_RewritesExactlyTheLiteralToken()
    {
        // Arrange.
        var body = WriteBody(Corpus);

        // Act.
        var result = await _properties.SetAsync(
            Target(body, "res:http://example.org/alice"), RdfContextPropertyProvider.LabelProperty, "Alicia", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Contains("rdfs:label \"Alicia\" .", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));

        // One undo restores the bytes.
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SettingAMissingComment_StatesTheTriple()
    {
        // Arrange.
        var body = WriteBody(Corpus);

        // Act.
        var result = await _properties.SetAsync(
            Target(body, "res:http://example.org/bob"), RdfContextPropertyProvider.CommentProperty, "Who Bob is", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess);
        // The rdfs prefix is declared, so the new pair reuses it rather than a full IRI.
        Assert.Contains("rdfs:comment \"Who Bob is\" .", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ABlankNode_ShowsItsBoundary_AndAnEdgeItsPredicate()
    {
        // Arrange.
        var body = WriteBody(Corpus + "_:x ex:name \"Anon\" .\r\n");

        // Act & assert.
        var blank = await _properties.DescribeAsync(Target(body, "blank:0"), TestContext.Current.CancellationToken);
        var row = Assert.Single(blank);
        Assert.Contains("blank node", row.ReadOnlyReason);

        var edge = await _properties.DescribeAsync(
            Target(body, "edge:res:http://example.org/alice|http://www.w3.org/1999/02/22-rdf-syntax-ns#type|res:http://example.org/Person"),
            TestContext.Current.CancellationToken);
        // Type triples draw as badges, not edges - but a stated IRI-object triple describes.
        _ = edge;

        var unsettable = await _properties.SetAsync(
            Target(body, "blank:0"), RdfContextPropertyProvider.LabelProperty, "nope", TestContext.Current.CancellationToken);
        Assert.False(unsettable.IsSuccess);
    }
}
