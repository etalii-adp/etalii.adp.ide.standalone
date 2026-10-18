using EtAlii.Adp.Designer;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Editor;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// A real <see cref="ToolRouter"/> over the tool types a test names, for the hierarchy model and
/// its store, which take one and have no degraded mode without it. Unnamed kinds have no types,
/// and the editors are the plain-text fallback alone unless the test names its own.
/// </summary>
public static class TestToolRouters
{
    /// <summary>The fallback editor every deployment has.</summary>
    private static readonly EditorDefinition _plainText = new("plain", "Plain Text", IsFallback: true);

    public static ToolRouter For(params DiagramDefinition[] diagrams) =>
        For(new TestDiagramDefinitionCatalog(diagrams));

    public static ToolRouter For(IDiagramDefinitionCatalog diagrams, IReadOnlyList<DesignerDefinition>? designers = null, IReadOnlyList<EditorDefinition>? editors = null) =>
        new(
            new DiagramFileRouter(diagrams),
            new DesignerFileRouter(new DesignerDefinitionCatalog { All = designers ?? [] }),
            new EditorResolver(new EditorDefinitionCatalog { All = editors ?? [_plainText] }));
}
