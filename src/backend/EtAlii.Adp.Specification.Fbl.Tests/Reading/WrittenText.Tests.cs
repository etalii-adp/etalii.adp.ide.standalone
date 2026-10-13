using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Reading;

/// <summary>
/// What a slot reads from a yaml scalar (FBL 0.5 §5.2, §5.3; Requirement 1.1 of fbl-written-text):
/// a string attribute, an id and a reference read what the author wrote.
/// </summary>
public class WrittenTextTests
{
    private static readonly FblBinding _binding = Inline("""
        [{
          "name": "item", "type": "Item", "at": "/items/*",
          "id": { "from": { "key": "id" } },
          "attributes": { "label": { "key": "label" } }
        },
        {
          "name": "task", "type": "Task", "at": "/tasks/*",
          "id": { "from": { "key": "id" } },
          "attributes": { "owner": { "key": "owner", "reference": { "to": ["item"], "by": "id" } } }
        }]
        """);

    private static readonly FblOptions _strings = new() { FileName = "plan.t", AttributeType = (_, attribute) => attribute == "label" ? "string" : null };

    /// <summary>Each case of Requirement 1.1: what is read, and what FBL 0.5 says it reads.</summary>
    public static TheoryData<string> Cases => ["label yes", "label 1.0", "label null", "label ~", "id 007", "reference 007"];

    [Theory]
    [MemberData(nameof(Cases))]
    public void AScalarReadsAsWritten(string name)
    {
        // Arrange.
        var (body, expected, observe) = Case(name);

        // Act.
        var model = Read(body);

        // Assert.
        Assert.Equal(expected, observe(model));
    }

    private static (string Body, object? Expected, Func<FblModel, object?> Observe) Case(string name) => name switch
    {
        "label yes" => ("items:\n  - id: a\n    label: yes\n", "yes", m => m.Find("a")!.Attributes["label"]),
        "label 1.0" => ("items:\n  - id: a\n    label: 1.0\n", "1.0", m => m.Find("a")!.Attributes["label"]),
        "label null" => ("items:\n  - id: a\n    label: null\n", "null", m => m.Find("a")!.Attributes["label"]),
        "label ~" => ("items:\n  - id: a\n    label: ~\n", "~", m => m.Find("a")!.Attributes["label"]),
        "id 007" => ("items:\n  - id: 007\n    label: Seven\n", "007", m => m.Elements.Single().Id),
        // The element named is written quoted, so its id is "007" whatever is read; the reference is written plain.
        "reference 007" => ("items:\n  - id: \"007\"\ntasks:\n  - id: b\n    owner: 007\n", "", m => string.Join(" ", m.Findings.Select(f => f.Message))),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static FblModel Read(string body) => OpenBody.Open(Encoding.UTF8.GetBytes(body), _binding, _strings).Model;

    private static FblBinding Inline(string elements)
    {
        var json = $$"""{ "fbl": "0.1", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared", "elements": {{elements}} } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.Empty(problems);
        return document!.Bindings["t"];
    }
}
