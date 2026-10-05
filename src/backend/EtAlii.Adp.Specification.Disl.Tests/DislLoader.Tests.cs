using System.Text;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>The steps of DISL §14.1 the loader takes: parse, version, names, inheritance, and compiling in context.</summary>
public class DislLoaderTests
{
    [Fact]
    public void AMinimalSpecification_Loads()
    {
        var result = DislLoader.Load(Specifications.With());

        Assert.NotNull(result.Specification);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("org.example.test", result.Specification.LanguageId);
    }

    // Step 1.

    [Fact]
    public void ADuplicateKey_IsRefusedWhereItRepeats()
    {
        const string json = """{ "disl": "0.2", "language": { "id": "a", "version": "1.0.0", "id": "b" }, "metamodel": { "types": {} } }""";

        Assert.Equal(["error at /language/id: The key 'id' appears more than once in one object (DISL §2.1)."], Specifications.Diagnostics(json));
    }

    [Fact]
    public void AByteOrderMark_IsRefused()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Specifications.With())).ToArray();

        var result = DislLoader.Load(bytes);

        Assert.Null(result.Specification);
        Assert.Equal("A specification is UTF-8 without a byte-order mark (DISL §2.1).", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public void TextThatIsNotJson_IsRefused()
    {
        var result = DislLoader.Load("{ \"disl\": ");

        Assert.Null(result.Specification);
        Assert.StartsWith("The specification is not JSON:", Assert.Single(result.Diagnostics).Message, StringComparison.Ordinal);
    }

    // Step 2.

    [Fact]
    public void AHigherMajorVersion_IsRefused()
    {
        Assert.Equal(["error at /disl: This runtime reads DISL 0.x; DISL 1.0 is refused (DISL §2.9)."], Specifications.Diagnostics(Specifications.With("\"disl\": \"1.0\"")));
    }

    [Fact]
    public void AHigherMinorVersion_IsWarnedAboutAndLoaded()
    {
        var result = DislLoader.Load(Specifications.With("\"disl\": \"0.3\""));

        Assert.NotNull(result.Specification);
        Assert.Equal("warning at /disl: This runtime knows DISL up to 0.2; what DISL 0.3 adds is not read (DISL §2.9).", Assert.Single(result.Diagnostics).ToString());
    }

    [Fact]
    public void TheDeprecatedDedlKey_IsReadAndWarnedAbout()
    {
        const string json = """{ "dedl": "0.1", "language": { "id": "a", "version": "1.0.0" }, "metamodel": { "types": {} } }""";

        var result = DislLoader.Load(json);

        Assert.Equal("0.1", result.Specification!.Disl);
        Assert.Equal(["warning at /dedl: 'dedl' is the deprecated alias of 'disl' (DISL §18)."], result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    [Fact]
    public void AMissingVersion_IsRefused()
    {
        const string json = """{ "language": { "id": "a", "version": "1.0.0" }, "metamodel": { "types": {} } }""";

        Assert.Equal(["error at /disl: A specification declares the DISL version it targets (DISL §2.9)."], Specifications.Diagnostics(json));
    }

    // Steps 5 and 6.

    [Fact]
    public void ASupertypeThatIsNotDeclared_IsRefused()
    {
        var json = Specifications.With("""
            "metamodel": { "types": { "Thing": { "extends": "Nothing" } } }
            """);

        Assert.Equal(["error at /metamodel/types/Thing/extends: 'Thing' extends 'Nothing', which is not a node type of this specification (DISL §4.7)."], Specifications.Diagnostics(json));
    }

    [Fact]
    public void ACycleOfSupertypes_IsRefused()
    {
        var json = Specifications.With("""
            "metamodel": { "types": { "A": { "extends": "B" }, "B": { "extends": "A" } } }
            """);

        Assert.Contains("is its own supertype", string.Join("\n", Specifications.Diagnostics(json)), StringComparison.Ordinal);
        Assert.Null(DislLoader.Load(json).Specification);
    }

    [Fact]
    public void ADiamond_IsLinearisedByC3()
    {
        var json = Specifications.With("""
            "metamodel": { "types": {
              "O": { "abstract": true, "attributes": { "o": { "type": "string" } } },
              "A": { "abstract": true, "extends": "O", "attributes": { "a": { "type": "int" } } },
              "B": { "abstract": true, "extends": "O", "attributes": { "b": { "type": "bool" } } },
              "D": { "extends": ["A", "B"], "attributes": { "d": { "type": "number" } } } } }
            """);

        var metamodel = Specifications.Loaded(json).Metamodel;

        Assert.Equal(["D", "A", "B", "O"], metamodel.Types["D"].Linearisation);
        Assert.Equal(["o", "b", "a", "d"], metamodel.Types["D"].Attributes.Keys);
        Assert.True(metamodel.IsA("D", "O"));
        Assert.False(metamodel.IsA("A", "B"));
        Assert.Equal(["O", "A", "B", "D"], metamodel.SubtypesOf("O").Select(type => type.Name));
    }

    [Fact]
    public void AnAttributeInheritedDifferentlyFromTwoSupertypes_IsRefusedUnlessRedeclared()
    {
        const string types = """
            "A": { "abstract": true, "attributes": { "x": { "type": "int" } } },
            "B": { "abstract": true, "attributes": { "x": { "type": "string" } } },
            """;
        var conflicting = Specifications.With("\"metamodel\": { \"types\": {" + types + "\"D\": { \"extends\": [\"A\", \"B\"] } } }");
        var redeclared = Specifications.With("\"metamodel\": { \"types\": {" + types + "\"D\": { \"extends\": [\"A\", \"B\"], \"attributes\": { \"x\": { \"type\": \"int\" } } } } }");

        Assert.Equal(["error at /metamodel/types/D: 'D' inherits 'x' from both 'B' and 'A', differently, and does not redeclare it (DISL §4.7)."], Specifications.Diagnostics(conflicting));
        Assert.Equal("int", Specifications.Loaded(redeclared).Metamodel.Types["D"].Attributes["x"].Type);
    }

    [Fact]
    public void ASubtypeThatChangesAnAttributesType_IsRefused()
    {
        var json = Specifications.With("""
            "metamodel": { "types": { "A": { "attributes": { "x": { "type": "int" } } }, "B": { "extends": "A", "attributes": { "x": { "type": "string" } } } } }
            """);

        Assert.Equal(["error at /metamodel/types/B/attributes/x: 'B' redeclares 'x' as string, changing its type int; a subtype may only narrow it (DISL §4.7)."], Specifications.Diagnostics(json));
    }

    [Fact]
    public void AReservedAttributeName_IsRefused()
    {
        var json = Specifications.With("""
            "metamodel": { "types": { "Thing": { "attributes": { "parent": { "type": "string" } } } } }
            """);

        Assert.Equal(["error at /metamodel/types/Thing/attributes/parent: 'parent' is a reserved name and cannot be an attribute (DISL §2.2)."], Specifications.Diagnostics(json));
    }

    [Fact]
    public void AnAttributeTypeThatNamesNothing_IsRefused()
    {
        var json = Specifications.With("""
            "metamodel": { "types": { "Thing": { "attributes": { "size": { "type": "Size" } } } } }
            """);

        Assert.Equal(["error at /metamodel/types/Thing/attributes/size/type: 'Size' is not a primitive, an enum, a data type or a type of this specification (DISL §4.3)."], Specifications.Diagnostics(json));
    }

    [Fact]
    public void ARelationEndThatNamesNothing_IsRefused()
    {
        var json = Specifications.With("""
            "metamodel": { "types": { "Thing": {} }, "relations": { "Link": { "source": "Thing", "target": ["Thing", "Other"] } } }
            """);

        Assert.Equal(["error at /metamodel/relations/Link/target: 'Other' is not a type of this specification (DISL §4.9)."], Specifications.Diagnostics(json));
    }

    [Fact]
    public void TheLabelAttribute_FollowsTheDefaultRule()
    {
        var json = Specifications.With("""
            "metamodel": { "types": {
              "Keyed": { "attributes": { "title": { "type": "string" }, "code": { "type": "string", "key": true } } },
              "Plain": { "attributes": { "count": { "type": "int" }, "title": { "type": "string" } } },
              "Named": { "labelAttribute": "count", "attributes": { "count": { "type": "int" }, "title": { "type": "string" } } } } }
            """);

        var types = Specifications.Loaded(json).Metamodel.Types;

        Assert.Equal("code", types["Keyed"].LabelAttribute);
        Assert.Equal("title", types["Plain"].LabelAttribute);
        Assert.Equal("count", types["Named"].LabelAttribute);
    }

    [Fact]
    public void ToolsOperationsAndScopesThatNameNothing_AreRefused()
    {
        var json = Specifications.With("""
            "metamodel": { "types": { "Thing": {}, "Base": { "abstract": true } } },
            "toolbox": {
              "groups": [ { "id": "g", "tools": [ { "id": "a", "creates": "Base" }, { "id": "b", "creates": "Missing" } ] } ],
              "contextMenus": [ { "for": ["Thing", "Ghost"], "tools": [ { "kind": "operation", "operation": "nothing" } ] } ] },
            "constraints": { "rules": [ { "id": "r", "scope": "Phantom", "rule": "true" } ] },
            "persistence": { "ids": { "types": { "Spectre": { "strategy": "derived", "expression": "'x'" } } } }
            """);

        Assert.Equal(
            [
                "error at /toolbox/groups/0/tools/0/creates: 'Base' is abstract, so nothing can create one (DISL §4.6).",
                "error at /toolbox/groups/0/tools/1/creates: 'Missing' is not a type of this specification (DISL §2.7).",
                "error at /toolbox/contextMenus/0/for: 'Ghost' is not a type of this specification (DISL §2.7).",
                "error at /toolbox/contextMenus/0/tools/0/operation: 'nothing' is not an operation of behavior.operations (DISL §7.3).",
                "error at /constraints/rules/0/scope: 'Phantom' is not a type of this specification (DISL §2.7).",
                "error at /persistence/ids/types/Spectre: 'Spectre' is not a type of this specification (DISL §2.7).",
            ],
            Specifications.Diagnostics(json));
    }

    // Step 8.

    [Fact]
    public void AnExpression_IsRefusedAVariableItsContextDoesNotBind()
    {
        // newValue is a gesture:change variable; an invariant's rule is in the constraint context.
        var json = Specifications.With("""
            "constraints": { "rules": [ { "id": "r", "scope": "Thing", "rule": "newValue != ''" }, { "id": "g", "kind": "change", "scope": "Thing", "rule": "newValue != ''" } ] }
            """);

        var diagnostic = Assert.Single(DislLoader.Load(json).Diagnostics);

        Assert.Equal("/constraints/rules/0/rule", diagnostic.Pointer);
        Assert.StartsWith("'newValue' is not a variable here; available: self, diagram, env.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtensionsAndDocumentation_AreNotCompiled()
    {
        var json = Specifications.With("""
            "constraints": { "rules": [ { "id": "r", "scope": "Thing", "rule": "true", "doc": { "summary": "when: no CEL here" }, "x-rule.extra": { "rule": "this is ((( not CEL" } } ] },
            "x-tool": { "when": "neither ((( is this" }
            """);

        var specification = Specifications.Loaded(json);

        Assert.Equal(["/constraints/rules/0/rule"], specification.Expressions.Select(expression => expression.Pointer));
        Assert.Equal(["x-tool"], specification.Extensions.Keys);
    }

    [Fact]
    public void ActionsBindTheirNames_ForTheExpressionsAfterThem()
    {
        var json = Specifications.With("""
            "behavior": { "operations": { "copy": { "for": "Thing", "actions": [
              { "let": { "base": "self.name" } },
              { "create": { "type": "'Thing'", "attributes": { "name": "base + ' copy'" } }, "as": "made" },
              { "select": "made" } ] } } }
            """);

        var specification = Specifications.Loaded(json);

        Assert.Equal(DislContexts.Operation, specification.ExpressionAt("/behavior/operations/copy/actions/2/select")!.Context);
        Assert.Equal(4, specification.Expressions.Count);
    }

    [Fact]
    public void ACelStringWhereNoContextIsKnown_IsWarnedAboutAndCompiled()
    {
        var json = Specifications.With("""
            "simulationsElsewhere": { "next": { "cel": "self.name" } }
            """);

        var result = DislLoader.Load(json);

        Assert.NotNull(result.Specification);
        Assert.Equal("warning at /simulationsElsewhere/next/cel: This runtime does not know the CEL context of this position; it was compiled with every variable of any context.", Assert.Single(result.Diagnostics).ToString());
    }

    [Fact]
    public void TheCostLimit_IsTheLanguagesOrOneMillion()
    {
        Assert.Equal(1_000_000, Specifications.Loaded(Specifications.With()).CostLimit);
        Assert.Equal(500, Specifications.Loaded(Specifications.With("""
            "language": { "id": "a", "version": "1.0.0", "limits": { "celCost": 500 } }
            """)).CostLimit);
    }
}
