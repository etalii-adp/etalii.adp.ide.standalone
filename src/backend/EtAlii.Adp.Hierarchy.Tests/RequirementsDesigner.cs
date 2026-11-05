using System.Text;
using EtAlii.Adp.Designer;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Routing;
using Xunit;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// A designer type recognised by the envelope on its body's first line, as the Hierarchical Requirement Designer is
/// (spec 029 R3, R4): a <c>.md</c> claimed as a shared extension through an FBL binding's line-1 marker, whose binding
/// creates the registration with the first setting. Its claim is answered by the FBL library's own routing.
/// </summary>
internal static class RequirementsDesigner
{
    public const string Origin = "fixture/requirements";

    public const string Envelope = "<!-- ded: 0.2 designer: fixture/requirements -->";

    private const string BindingJson = """
        {
          "fbl": "0.7",
          "bindings": {
            "markdown": {
              "claims": {
                "extensions": [".md"],
                "shared": true,
                "marker": { "pattern": "^<!--[ \\t]+ded:[ \\t]+[^ \\t]+[ \\t]+designer:[ \\t]+fixture/requirements[ \\t]+-->[ \\t]*$", "lines": 1 },
                "origins": ["fixture/requirements"]
              },
              "body": { "kind": "file", "family": "lines" },
              "reader": "declared",
              "elements": [
                { "name": "line", "type": "Line", "line": "^(?<text>.+)$", "attributes": { "text": { "group": "text" } } }
              ],
              "registration": { "createOnFirstPlacement": true }
            }
          }
        }
        """;

    private static FblBinding Binding { get; } = Load();

    public static DesignerDefinition Definition { get; } = new(
        Origin,
        "Fixture requirements",
        Formats: [new DesignerFormat("Markdown", ".md")],
        Claim: new DesignerClaim([".md"], (name, head) => BodyClaim.Claims(Binding, name, head), Envelope, BodyClaim.CreatesRegistration(Binding)));

    /// <summary>A body that carries the envelope, CRLF, as the template writes one.</summary>
    public static string Body(string title = "Plan") => $"{Envelope}\r\n# {title}\r\n";

    private static FblBinding Load()
    {
        var diagnostics = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(BindingJson), out var document);
        Assert.True(document is not null, string.Join("; ", diagnostics));
        return document.Bindings["markdown"];
    }
}
