using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// A selection in a query, by the contract every element resolver keeps. The element followed is the
/// header, because it is the one whose id survives an edit while what it shows changes: a variable's
/// id is its name, so renaming one is a removal and an addition.
/// </summary>
public sealed class SparqlContextSourceResolverTests : DiagramElementSourceResolverContract
{
    private readonly SparqlDocumentStore _store = new();

    protected override string Example => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "lf-line-endings.rq");

    protected override IContextSourceResolver CreateResolver() =>
        new SparqlContextSourceResolver(new DiagramFileRouter(new SparqlOnlyCatalog()), _store, new SparqlElementMapper());

    protected override void Reload(string body) => _store.Reload(body);

    protected override string ElementId => SparqlElementMapper.HeaderId;

    protected override IReadOnlyList<string> ElementPath => ["SELECT query"];

    protected override string ElementType => SparqlElementMapper.HeaderType;

    protected override string Gesture => "rel:var:s->var:o";

    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a SPARQL query.",
        "That element is no longer in this query.",
        "New element",
        "New relation");

    /// <summary>Changes the query's form, which is what its header shows; the name is not a word a query can hold.</summary>
    protected override async Task<IReadOnlyList<string>> RenameAsync(string body, string name, CancellationToken cancellationToken)
    {
        _ = name;
        var asked = (await File.ReadAllTextAsync(body, cancellationToken)).Contains("ASK", StringComparison.Ordinal);
        await File.WriteAllTextAsync(
            body,
            asked ? "PREFIX ex: <http://example.org/>\nSELECT DISTINCT ?s\nWHERE { ?s ex:p ?o }\n" : "PREFIX ex: <http://example.org/>\nASK { ?s ex:p ?o }\n",
            cancellationToken);
        return [asked ? "SELECT DISTINCT query" : "ASK query"];
    }

}
