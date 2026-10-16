using System.Text;
using EtAlii.Adp.Context;
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
    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(MindmapDefinition).Assembly, "mindmap.fbl", "mindmap"));

    /// <summary>The bundled definition, with the palette, the menus and the rows every module derives the same way.</summary>
    public static DislDiagramDefinition Definition { get; } = new(typeof(MindmapDefinition).Assembly, "mindmap.dis", "x-mindmap");

    /// <summary>The definition.</summary>
    private static DislSpecification Specification => Definition.Specification;

    /// <summary>The wire ids of <c>x-mindmap</c>.</summary>
    private static WireIdMap Ids => Definition.Ids;

    /// <summary>The binding every mind map body is read with.</summary>
    public static FblBinding Binding => LoadedBinding.Value;

    /// <summary>What <c>env</c> reads: editable, always, as the module's menus and rows have always been.</summary>
    private static DislEnv Env { get; } = new();

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
        return Definition.Menus(DislMenuTarget.Element(element), Env);
    }

    /// <summary>Whether <paramref name="element"/>'s menu offers <paramref name="actionId"/>: an action it does not offer does not apply to it.</summary>
    public static bool Offers(DislElement element, string actionId) =>
        ContextMenuDerivation.Derive(Specification, DislMenuTarget.Element(element), Env, Ids)
            .Any(group => group.Entries.Any(entry => entry.Id == actionId && entry.Available));

    /// <summary>The property rows of <paramref name="element"/>.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Definition.Rows(element, Env);
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
    private static string WrittenId(DislElement element) =>
        element.HostAttributes.GetValueOrDefault(MindmapFreeplanePlugin.StoredIdAttribute) as string ?? "";

}
