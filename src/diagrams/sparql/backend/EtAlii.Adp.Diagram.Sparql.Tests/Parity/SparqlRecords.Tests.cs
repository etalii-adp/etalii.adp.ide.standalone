using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Diagram.Sparql.Tests.Parity;

/// <summary>
/// The stored records a query is read as (<see cref="SparqlRecords.From"/>): one per entry, in document order, each with
/// its span and line; the attributes the definition's stored types declare; and the three things decided here because
/// DISL cannot decide them - whether a prefix is used, a term's key, and a pattern's <c>repeat</c>.
/// </summary>
public class SparqlRecordsTests
{
    private const string Query = "PREFIX ex: <http://example.org/>\r\n"
        + "PREFIX unused: <http://example.org/unused/>\r\n"
        + "SELECT ?s (STR(?o) AS ?label) WHERE {\r\n"
        + "  ?s ex:p ?o .\r\n"
        + "  OPTIONAL { ?o ex:q \"a b\"@en }\r\n"
        + "  FILTER(?o != ex:none)\r\n"
        + "  { SELECT ?s WHERE { ?s ex:r ?t } }\r\n"
        + "  BIND(1 AS ?n)\r\n"
        + "}\r\n";

    private static FblModel Read(string text) => SparqlRecords.From(SparqlParser.Parse(text), text);

    [Fact]
    public void AQuery_IsOneRecordPerEntry_InDocumentOrder_EachWithItsSpanAndLine()
    {
        // Act.
        var records = Read(Query).Elements;

        // Assert.
        Assert.Equal(
            [
                ("Query", 1), ("Prefix", 1), ("Prefix", 2), ("Pattern", 4), ("Scope", 5), ("Pattern", 5),
                ("Constraint", 6), ("SubQuery", 7), ("Constraint", 8),
            ],
            records.Select(record => (record.Type, record.Line)));
        Assert.Equal(
            [
                "PREFIX ex: <http://example.org/>", "PREFIX unused: <http://example.org/unused/>", "?s ex:p ?o",
                "OPTIONAL { ?o ex:q \"a b\"@en }", "?o ex:q \"a b\"@en", "FILTER(?o != ex:none)", "{ SELECT ?s WHERE { ?s ex:r ?t } }",
                "BIND(1 AS ?n)",
            ],
            records.Skip(1).Select(Of));
    }

    [Fact]
    public void TheRecords_CarryTheAttributesOfTheDefinitionsStoredTypes()
    {
        // Act.
        var records = Read(Query).Elements;

        // Assert.
        var query = records[0];
        Assert.Equal("select", query.Attributes["form"]);
        Assert.Equal(["s", "label"], (IEnumerable<string>)query.Attributes["projection"]!);
        Assert.Equal(["", "STR(?o)"], (IEnumerable<string>)query.Attributes["projectionExpressions"]!);
        var scope = records.Single(record => record.Type == "Scope");
        Assert.Equal(("where/optional.0", "optional", null), ((string)scope.Attributes["path"]!, (string)scope.Attributes["scopeKind"]!, scope.ParentId));
        var pattern = records.Single(record => record.Type == "Pattern" && record.Line == 5);
        Assert.Equal(("where/optional.0", "var:o", "ex:q", "lit"), ((string)pattern.Attributes["scope"]!, (string)pattern.Attributes["sKey"]!, (string)pattern.Attributes["predicate"]!, (string)pattern.Attributes["oKind"]!));
        var bind = records.Last();
        Assert.Equal(("bind", "n", 0L), ((string)bind.Attributes["constraintKind"]!, (string)bind.Attributes["definedVariable"]!, (long)bind.Attributes["ordinal"]!));
        var subQuery = records.Single(record => record.Type == "SubQuery");
        Assert.Equal(("where", "SELECT ?s", null), ((string)subQuery.Attributes["scope"]!, (string)subQuery.Attributes["display"]!, subQuery.ParentId));
    }

    [Fact]
    public void APrefix_IsUsed_WhereTheTextUsesItOutsideItsDeclaration()
    {
        // Act.
        var prefixes = Read(Query).Elements.Where(record => record.Type == "Prefix").ToList();

        // Assert: ex: is used in a pattern and in an expression, unused: only in its declaration.
        Assert.Equal([("ex", true), ("unused", false)], prefixes.Select(prefix => ((string)prefix.Attributes["prefix"]!, (bool)prefix.Attributes["used"]!)));
    }

    [Fact]
    public void ALiteralsKey_IsItsLexicalFormPercentEncoded_WithItsLanguageOrDatatype()
    {
        // Arrange.
        const string text = "PREFIX xsd: <http://www.w3.org/2001/XMLSchema#> SELECT * WHERE { ?s ?p \"a b\"@en . ?s ?p \"1\"^^xsd:integer }";

        // Act.
        var objects = Read(text).Elements.Where(record => record.Type == "Pattern").Select(record => (string)record.Attributes["oKey"]!);

        // Assert.
        Assert.Equal(["lit:a%20b@en", "lit:1^integer"], objects);
    }

    [Fact]
    public void APatternsRepeat_CountsTheEarlierPatternsWithItsParts_InTheOrderTheToolWalksThem()
    {
        // Arrange: the optional's pattern is walked after the where clause's own two, though written between them.
        const string text = "PREFIX ex: <http://example.org/> SELECT * WHERE { ?s ex:p ?o . OPTIONAL { ?s ex:p ?o } ?s ex:p ?o }";

        // Act.
        var repeats = Read(text).Elements.Where(record => record.Type == "Pattern").Select(record => ((string)record.Attributes["scope"]!, (long)record.Attributes["repeat"]!));

        // Assert.
        Assert.Equal([("where", 0L), ("where/optional.0", 2L), ("where", 1L)], repeats);
    }

    [Fact]
    public void TheTemplateOfConstructWhere_StatesNoEntryOfItsOwn()
    {
        // Arrange.
        const string text = "PREFIX ex: <http://example.org/> CONSTRUCT WHERE { ?s ex:p ?o }";

        // Act.
        var records = Read(text).Elements;

        // Assert.
        Assert.Equal(true, records[0].Attributes["templateRepeatsWhere"]);
        Assert.Equal(["Query", "Prefix", "Scope", "Pattern"], records.Select(record => record.Type));
        Assert.Equal("template", records.Single(record => record.Type == "Scope").Attributes["path"]);
    }

    private static string Of(FblElement record) => Query[record.OwnSpan.Start..record.OwnSpan.End];
}
