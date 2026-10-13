using System.Text;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Expressions;
using EtAlii.Adp.Specification.Fbl.History;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Expressions;

/// <summary>FBL's contexts of the CEL engine (FBL §2.4): what each refuses and what each offers. The engine's own tests are in EtAlii.Adp.Specification.Cel.Tests.</summary>
public class ExpressionsTests
{
    [Theory]
    [InlineData("groups.name == 'x'", CelContext.Tree, "'groups' is not a variable here; available: entry, parent, path, line, registration.")]
    [InlineData("path.x == 1", CelContext.Lines, "'path' is not a variable here; available: entry, parent, groups, line, registration.")]
    [InlineData("entry.x == 1", CelContext.Insert, "'entry' is not a variable here; available: attributes.")]
    [InlineData("entry.x.y.z()", CelContext.Tree, "The function 'z' is not supported by this CEL evaluator.")]
    [InlineData("timestamp('2026-01-01')", CelContext.Tree, "The function 'timestamp' is not supported by this CEL evaluator.")]
    public void AnExpressionOutsideTheSubsetFailsToCompile(string expression, CelContext context, string message)
    {
        // Act.
        var refused = Record.Exception(() => FblCel.Compile(expression, context));

        // Assert.
        Assert.IsType<CelException>(refused);
        Assert.Contains(message, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("yaml", "items:\n  - label: yes\n", "yes|plain|yes")]
    [InlineData("yaml", "items:\n  - label: 007\n", "007|plain|7")]
    [InlineData("yaml", "items:\n  - label: 'it''s'\n", "it's|single|it's")]
    [InlineData("yaml", "items:\n  - label: \"a\\tb\"\n", "a\tb|double|a\tb")]
    [InlineData("yaml", "items:\n  - label: |\n      one\n", "one\n|literal|one\n")]
    [InlineData("yaml", "items:\n  - label: >\n      one\n", "one\n|folded|one\n")]
    [InlineData("json", "{ \"items\": [{ \"label\": \"yes\" }] }", "yes|double|yes")]
    [InlineData("json", "{ \"items\": [{ \"label\": 1.50 }] }", "1.50|plain|1.5")]
    [InlineData("json", "{ \"items\": [{ \"label\": true }] }", "true|plain|true")]
    public void TextAndStyleAnswerBesideEntryForEachScalar(string family, string body, string expected)
    {
        // Arrange: a computed slot reading all three variables.
        var binding = Binding(family, """{ "seen": { "value": "text.label + '|' + style.label + '|' + string(entry.label)" } }""");

        // Act.
        var model = OpenBody.Open(Encoding.UTF8.GetBytes(body), binding, new FblOptions { FileName = "plan.t" }).Model;

        // Assert.
        Assert.Equal(expected, Assert.Single(model.Elements).Attributes["seen"]);
    }

    [Fact]
    public void HasIsTrueForAKeyWithNoValueAndFalseForAnAbsentKey()
    {
        // Arrange.
        var binding = Binding("yaml", """{ "label": { "key": "label" } }""", when: "has(entry.end)", version: "0.1");

        // Act.
        var model = OpenBody.Open(Encoding.UTF8.GetBytes("items:\n  - label: open\n    end:\n  - label: absent\n"), binding, new FblOptions { FileName = "plan.t" }).Model;

        // Assert: only the entry that writes end: is matched.
        Assert.Equal(["open"], model.Elements.Select(e => e.Attributes["label"]));
    }

    private static FblBinding Binding(string family, string attributes, string? when = null, string version = "0.5")
    {
        var condition = when is null ? "" : $$""", "when": "{{when}}" """;
        var json = $$"""{ "fbl": "{{version}}", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "{{family}}" }, "reader": "declared", "elements": [{ "name": "item", "type": "Item", "at": "/items/*"{{condition}}, "attributes": {{attributes}} }] } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.Empty(problems);
        return document!.Bindings["t"];
    }
}
