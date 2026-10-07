using System.Text;
using System.Xml;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Plugins;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// The FBL §11 persistence plugin of the binding <c>mindmap.fbl#mindmap</c>
/// (<c>net.etalii.adp.freeplane.mm</c>): it reads a Freeplane <c>.mm</c> body with
/// <see cref="MindmapDocument"/>, the module's own reader, so the DISL model sees exactly the nodes,
/// texts, notes and links the canvas and the commands see.
/// </summary>
/// <remarks>
/// <para>
/// <b>A <c>&lt;node&gt;</c> is a <c>Node</c></b> with <c>text</c>, <c>notes</c>, <c>link</c> (only when
/// the file has a <c>LINK</c>, empty or not, so <c>has(self.link)</c> means what <c>Link is not null</c>
/// meant), <c>folded</c>, <c>position</c> (only when the file has one), and <c>storedId</c>, the
/// <c>ID</c> the file writes. Its parent is the enclosing node, in the slot <c>children</c>.
/// </para>
/// <para>
/// <b>An element's id is its <c>ID</c> for the first node the file gives it</b>. A later holder of
/// the same <c>ID</c>, and a node with none, is given an ephemeral id that no <c>ID</c> can equal (it
/// holds a NUL, which XML forbids), so every element has its own id while findings still name each
/// node by what the file writes (<c>storedId</c>).
/// </para>
/// <para>
/// <b>It reads; it does not plan.</b> The standalone host edits the XML in place through the module's
/// commands, which carry their own undo, so <see cref="Plan"/> refuses every change and the host never
/// asks it to. An element's span is empty: nothing is planned against it, and its line locates it.
/// </para>
/// </remarks>
public sealed class MindmapFreeplanePlugin : IPersistencePlugin
{
    /// <summary>The plugin's id, as the binding's <c>reader.plugin</c> names it.</summary>
    public const string PluginId = "net.etalii.adp.freeplane.mm";

    /// <summary>The binding's element type, which <c>persistence.typeMap</c> maps to the metamodel's.</summary>
    public const string NodeType = "Node";

    /// <summary>The attribute holding the <c>ID</c> the file writes, which the model keeps beside the metamodel's.</summary>
    public const string StoredIdAttribute = "storedId";

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public PluginReadResult Read(PluginReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = request.Files.FirstOrDefault(file => file.RelativePath.Length == 0)
            ?? throw new ArgumentException("The request holds no body.", nameof(request));

        MindmapDocument document;
        try
        {
            // Read-only, as the validator always judged it: no id is assigned, so the model is the
            // file exactly as written.
            document = MindmapDocument.Parse(Encoding.UTF8.GetString(body.Bytes), assignMissingIds: false);
        }
        catch (MindmapFormatException exception)
        {
            return new PluginReadResult([], [new Finding(FindingCodes.Unparseable, FindingSeverity.Error, exception.Message, null)], true);
        }

        return new PluginReadResult(Elements(document), [], false);
    }

    /// <summary>
    /// The elements of <paramref name="document"/> as <see cref="Read"/> gives them: for a document the
    /// host already holds, whose ids it may have assigned in memory.
    /// </summary>
    public static IReadOnlyList<FblElement> Elements(MindmapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var elements = new List<FblElement>();
        var modelIds = new Dictionary<MindmapNode, string>(NodeComparer.Instance);
        var holders = new Dictionary<string, int>(StringComparer.Ordinal);
        var index = 0;
        foreach (var node in document.Nodes)
        {
            index++;
            var written = node.Id;
            var seen = written.Length == 0 ? 0 : holders[written] = holders.GetValueOrDefault(written) + 1;
            var id = written.Length == 0 ? $"\0{index}" : seen == 1 ? written : $"{written}\0{seen}";
            modelIds[node] = id;

            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["text"] = node.Text,
                ["notes"] = node.Notes,
                ["folded"] = node.Folded,
                [StoredIdAttribute] = written,
            };
            if (node.Link is { } link) attributes["link"] = link;
            if (node.Position is { } position) attributes["position"] = position;

            var parent = node.Parent is { } owner ? modelIds[owner] : null;
            elements.Add(new FblElement(id, seen == 1, NodeType, "node", false, attributes, parent, parent is null ? null : "children", null, null, default, LineOf(node)));
        }

        return elements;
    }

    /// <inheritdoc />
    public PluginPlanResult Plan(PluginPlanRequest request) =>
        new PluginPlanResult.Refused("A mind map is edited in place by the module's commands, which carry their own undo.");

    /// <inheritdoc />
    public byte[] Template(PluginTemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Encoding.UTF8.GetBytes(new MindmapDocumentFactory().CreateEmptyDocument(request.Name));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Watch(PluginReadResult last) => [];

    /// <summary>The 1-based line the node's element starts on; 0 for a node made in memory since the read.</summary>
    private static int LineOf(MindmapNode node) =>
        node.Element is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;

    /// <summary>Nodes by their element: two views over one element are one node.</summary>
    private sealed class NodeComparer : IEqualityComparer<MindmapNode>
    {
        public static NodeComparer Instance { get; } = new();

        public bool Equals(MindmapNode? x, MindmapNode? y) => ReferenceEquals(x?.Element, y?.Element);

        public int GetHashCode(MindmapNode obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Element);
    }
}
