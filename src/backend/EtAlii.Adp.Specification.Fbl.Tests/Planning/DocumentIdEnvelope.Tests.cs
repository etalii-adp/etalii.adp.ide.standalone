using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Planning;

/// <summary>
/// The document id written into a body that holds none (FBL 0.24 §17.2, etalii.adp spec 092, T028), in each family of a tree:
/// after the envelope a DED definition opens with, its version and its designer type, rather than between them; and the
/// version a conversion on load raises, which nothing else may change (§17.4, step 2).
/// </summary>
public class DocumentIdEnvelopeTests
{
    private const string Binding = """
        { "fbl": "0.9", "bindings": {
          "yaml": { "claims": { "extensions": [".yaml"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared",
            "header": { "key": "ded", "value": ["0.1", "0.3"], "required": true }, "document": { "id": { "key": "id" } },
            "elements": [ { "name": "table", "type": "Table", "at": "/",
              "attributes": { "version": { "key": "ded", "readOnly": "Changed by nobody but a migration." }, "designer": { "key": "designer", "readOnly": "Changed by nobody." }, "name": { "key": "name" } } } ] },
          "json": { "claims": { "extensions": [".json"] }, "body": { "kind": "file", "family": "json" }, "reader": "declared",
            "header": { "key": "ded", "value": ["0.1", "0.3"], "required": true }, "document": { "id": { "key": "id" } },
            "elements": [ { "name": "table", "type": "Table", "at": "/",
              "attributes": { "version": { "key": "ded", "readOnly": "Changed by nobody but a migration." }, "designer": { "key": "designer", "readOnly": "Changed by nobody." }, "name": { "key": "name" } } } ] },
          "xml": { "claims": { "extensions": [".xml"] }, "body": { "kind": "file", "family": "xml" }, "reader": "declared",
            "header": { "line": "^<ded(?:\\s|/|>)", "required": true }, "document": { "id": { "attribute": "id" } },
            "elements": [ { "name": "table", "type": "Table", "at": "/ded",
              "attributes": { "version": { "attribute": "version", "readOnly": "Changed by nobody but a migration." }, "designer": { "attribute": "designer", "readOnly": "Changed by nobody." }, "name": { "attribute": "name" } } } ] }
        } }
        """;

    private static OpenBody Open(string text, string family)
    {
        Assert.Empty(FblDocumentLoader.Load(Encoding.UTF8.GetBytes(Binding), out var document));
        return OpenBody.Open(Encoding.UTF8.GetBytes(text), document!.Bindings[family]);
    }

    /// <summary>The id the body's one table was read with.</summary>
    private static string TableOf(OpenBody body) => body.Model.Elements.Single(element => element.Type == "Table").Id;

    [Theory]
    [InlineData("yaml", "ded: \"0.1\"\r\ndesigner: etalii/knowledge\r\n# kept\r\nname: Cities\r\n", "ded: \"0.1\"\r\ndesigner: etalii/knowledge\r\nid: c1t9e5sx\r\n# kept\r\nname: Cities\r\n")]
    [InlineData("yaml", "ded: \"0.1\"\nname: Cities\ndesigner: etalii/knowledge\n", "ded: \"0.1\"\nid: c1t9e5sx\nname: Cities\ndesigner: etalii/knowledge\n")]
    [InlineData("json", "{\r\n  \"ded\": \"0.1\",\r\n  \"designer\": \"etalii/knowledge\",\r\n  \"name\": \"Cities\"\r\n}\r\n", "{\r\n  \"ded\": \"0.1\",\r\n  \"designer\": \"etalii/knowledge\",\r\n  \"id\": \"c1t9e5sx\",\r\n  \"name\": \"Cities\"\r\n}\r\n")]
    [InlineData("json", "{ \"ded\": \"0.1\", \"designer\": \"etalii/knowledge\", \"name\": \"Cities\" }", "{ \"ded\": \"0.1\", \"designer\": \"etalii/knowledge\", \"id\": \"c1t9e5sx\", \"name\": \"Cities\" }")]
    [InlineData("json", "{\n  \"ded\": \"0.1\",\n  \"id\": \"old12345\",\n  \"name\": \"Cities\"\n}\n", "{\n  \"ded\": \"0.1\",\n  \"id\": \"c1t9e5sx\",\n  \"name\": \"Cities\"\n}\n")]
    [InlineData("xml", "<ded version=\"0.1\" designer=\"etalii/knowledge\" name=\"Cities\">\r\n  <rows/>\r\n</ded>\r\n", "<ded version=\"0.1\" designer=\"etalii/knowledge\" id=\"c1t9e5sx\" name=\"Cities\">\r\n  <rows/>\r\n</ded>\r\n")]
    [InlineData("xml", "<ded name=\"Cities\"/>", "<ded id=\"c1t9e5sx\" name=\"Cities\"/>")]
    [InlineData("xml", "<ded version=\"0.1\" id=\"old12345\"/>", "<ded version=\"0.1\" id=\"c1t9e5sx\"/>")]
    public void ADocumentId_JoinsTheEnvelope_OrTakesThePlaceOfTheOneThere(string family, string before, string after)
    {
        // Arrange.
        var body = Open(before, family);

        // Act.
        var result = body.Change(new ModelChange.SetDocumentId("c1t9e5sx"));

        // Assert: every other byte is kept, and the id reads back.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal(after, Encoding.UTF8.GetString(body.Bytes));
        Assert.Equal("c1t9e5sx", body.Model.DocumentId);
    }

    [Theory]
    [InlineData("yaml", "ded: \"0.1\"\ndesigner: etalii/knowledge\nname: Cities\n", "ded: \"0.3\"\ndesigner: etalii/knowledge\nname: Cities\n")]
    [InlineData("json", "{\n  \"ded\": \"0.1\",\n  \"designer\": \"etalii/knowledge\"\n}\n", "{\n  \"ded\": \"0.3\",\n  \"designer\": \"etalii/knowledge\"\n}\n")]
    [InlineData("xml", "<ded version=\"0.1\" designer=\"etalii/knowledge\"/>", "<ded version=\"0.3\" designer=\"etalii/knowledge\"/>")]
    public void TheVersion_IsRaisedByAConversion_AndByNothingElse(string family, string before, string after)
    {
        // Arrange.
        var body = Open(before, family);
        var raise = new Dictionary<string, object?> { ["version"] = "0.3" };

        // Act: an ordinary set, then the conversion's.
        var refused = body.Change(new ModelChange.Set(TableOf(body), raise));
        var converted = body.Change(new ModelChange.Set(TableOf(body), raise) { Converting = true });

        // Assert: the binding's reason refuses the first; the second writes the version and nothing more.
        Assert.Equal("Changed by nobody but a migration.", Assert.IsType<PlanResult.Refused>(refused).Reason);
        Assert.IsType<PlanResult.Planned>(converted);
        Assert.Equal(after, Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void AConversion_StillCannotWriteASlotThatIsNotThere()
    {
        // Arrange: a body whose designer type is absent.
        var body = Open("ded: \"0.1\"\nname: Cities\n", "yaml");

        // Act.
        var result = body.Change(new ModelChange.Set(TableOf(body), new Dictionary<string, object?> { ["designer"] = "etalii/knowledge" }) { Converting = true });

        // Assert: a conversion rewrites what the envelope holds; it does not make up what it lacks.
        Assert.IsType<PlanResult.Refused>(result);
        Assert.Equal("ded: \"0.1\"\nname: Cities\n", Encoding.UTF8.GetString(body.Bytes));
    }
}
