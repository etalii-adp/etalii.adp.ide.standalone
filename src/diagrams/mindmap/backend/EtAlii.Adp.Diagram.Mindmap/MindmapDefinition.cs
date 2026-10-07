using System.Text;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Plugins;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// The mind map's bundled DISL definition (<c>definition/mindmap.dis</c>, from etalii-adp/etalii.adp),
/// loaded once, and what the module derives from it: the palette, the context menus, the property
/// rows, the findings and the deletion confirmation, mapped onto the host's types with the wire ids of
/// its <c>x-mindmap</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>The DISL model is read through FBL</b>: the binding <c>mindmap.fbl#mindmap</c> names the
/// persistence plugin <see cref="MindmapFreeplanePlugin"/>, which reads with the module's own
/// <see cref="MindmapDocument"/>, and <see cref="DislModelBuilder"/> builds the model from that reading.
/// The validator reads the text it is handed; the menus and rows read the document the store holds,
/// whose missing ids it has assigned in memory, so a node is addressed by the id the canvas knows.
/// </para>
/// <para>
/// <b>Viewer state is bound per call</b>: every node's <c>self.view.collapsed</c> is the asking
/// viewer's fold, which is what turns Collapse into Expand.
/// </para>
/// <para>
/// <b>The code wins where the two would differ</b>, and two host choices stay here: <c>env.readOnly</c>
/// is always false, because the module never offered a read-only menu; and the definition's
/// <c>Space</c> is sent as the key the client matches, <c>" "</c>.
/// </para>
/// </remarks>
internal static class MindmapDefinition
{
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(MindmapDefinition).Assembly, "mindmap.dis"));

    private static readonly Lazy<WireIdMap> LoadedIds = new(() => WireIdMap.Of(Specification, "x-mindmap"));

    private static readonly Lazy<FblBinding> LoadedBinding = new(LoadBinding);

    private static readonly Lazy<IReadOnlyList<ToolboxItemDefinition>> LoadedToolbox = new(() =>
    [
        .. ToolboxDerivation.Derive(Specification, Ids)
            .Select(tool => new ToolboxItemDefinition(tool.Id, tool.Label, tool.Icon, tool.Description, tool.DropActionId ?? "")),
    ]);

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>The wire ids of <c>x-mindmap</c>.</summary>
    public static WireIdMap Ids => LoadedIds.Value;

    /// <summary>The binding every mind map body is read with.</summary>
    public static FblBinding Binding => LoadedBinding.Value;

    /// <summary>The palette.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox => LoadedToolbox.Value;

    /// <summary>What <c>env</c> reads: editable, always, as the module's menus and rows have always been.</summary>
    public static DislEnv Env { get; } = new();

    /// <summary>
    /// The DISL model of <paramref name="document"/> as the store holds it, each node's
    /// <c>self.view</c> bound to <paramref name="view"/>'s fold when one is given.
    /// </summary>
    public static DislDiagram ModelOf(MindmapDocument document, MindmapConnectionView? view = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var diagram = DislModelBuilder.From(new FblModel(MindmapFreeplanePlugin.Elements(document), [], false), Specification).Diagram;
        if (view is not null)
        {
            foreach (var node in diagram.Nodes)
            {
                node.View = new CelMap { ["collapsed"] = view.IsFolded(WrittenId(node)) };
            }
        }

        return diagram;
    }

    /// <summary>The node <paramref name="id"/> names in <paramref name="diagram"/> - the first the file gives that id, as the document finds it - or null.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return id.Length == 0 ? null : diagram.ElementById(id);
    }

    /// <summary>The context menu of <paramref name="element"/>, in its groups.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return
        [
            .. ContextMenuDerivation.Derive(Specification, DislMenuTarget.Element(element), Env, Ids)
                .Select(group => new ContextActionGroupDefinition([.. group.Entries.Select(Action)])),
        ];
    }

    /// <summary>Whether <paramref name="element"/>'s menu offers <paramref name="actionId"/>: an action it does not offer does not apply to it.</summary>
    public static bool Offers(DislElement element, string actionId) =>
        ContextMenuDerivation.Derive(Specification, DislMenuTarget.Element(element), Env, Ids)
            .Any(group => group.Entries.Any(entry => entry.Id == actionId && entry.Available));

    /// <summary>The property rows of <paramref name="element"/>.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return [.. FormDerivation.Derive(Specification, element, Env, Ids).Select(Row)];
    }

    /// <summary>
    /// The confirmation deleting <paramref name="element"/> asks for, with the delete entry's icon, or
    /// null when it goes without asking.
    /// </summary>
    public static ContextConfirmationRequest? Confirmation(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (DeletionPolicy.Confirmation(Specification, element, env: Env) is not { } confirmation) return null;

        var icon = ContextMenuDerivation.Derive(Specification, DislMenuTarget.Element(element), Env, Ids)
            .SelectMany(group => group.Entries)
            .FirstOrDefault(entry => entry.Kind == "delete")?.Icon ?? "";
        return new ContextConfirmationRequest(confirmation.Title, icon, confirmation.Message, confirmation.ConfirmLabel, Danger: confirmation.Danger);
    }

    /// <summary>
    /// The definition's findings for <paramref name="text"/>, a body read through the binding as the file
    /// has it, named after <paramref name="file"/> - the file the finding is attributed to.
    /// </summary>
    public static IReadOnlyList<DislFinding> Findings(string text, string file)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(file);

        var body = PluginBody.Open(Encoding.UTF8.GetBytes(text), Binding, new MindmapFreeplanePlugin(), IoPath.GetFileName(file));
        var model = DislModelBuilder.From(body.Model, Specification);
        model.Diagram.File = file;
        IReadOnlyList<DislReaderFinding> reader =
        [
            .. model.Findings
                .Where(finding => finding.Code == FindingCodes.Unparseable)
                .Select(finding => DislReaderFinding.NotParsed(finding.Message, finding.Location?.Line)),
        ];
        return ConstraintEvaluator.Evaluate(Specification, model.Diagram, new DislConstraintOptions(Env, reader, WrittenId));
    }

    /// <summary>The <c>ID</c> the file writes for <paramref name="element"/>; empty for a node it gives none.</summary>
    public static string WrittenId(DislElement element) =>
        element.HostAttributes.GetValueOrDefault(MindmapFreeplanePlugin.StoredIdAttribute) as string ?? "";

    private static ContextActionDefinition Action(DerivedMenuEntry entry) => new(
        entry.Id,
        entry.Label,
        entry.Icon,
        entry.Shortcut is { } key ? new ContextShortcutDefinition(KeyOf(key)) : null,
        entry.Available,
        entry.UnavailableReason);

    /// <summary>A shortcut as the client sends and matches it: the definition's text, with <c>Space</c> as the key it is.</summary>
    private static string KeyOf(DerivedShortcut shortcut) => shortcut.Text == "Space" ? " " : shortcut.Text;

    /// <summary>A row as the grid draws it: a <c>textarea</c> is the Text editor, anything else a line.</summary>
    private static ContextPropertyDefinition Row(DerivedRow row) =>
        new(row.Id, row.Label, row.Value, row.Widget == "textarea" ? ContextPropertyEditor.Text : ContextPropertyEditor.Line, row.ReadOnlyReason, row.Group);

    private static FblBinding LoadBinding()
    {
        var assembly = typeof(MindmapDefinition).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith("mindmap.fbl", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var problems = FblDocumentLoader.Load(buffer.ToArray(), name, out var document);
        if (document is null)
        {
            throw new InvalidOperationException($"The mind map's FBL binding does not load: {string.Join("; ", problems)}");
        }

        return document.Bindings["mindmap"];
    }
}
