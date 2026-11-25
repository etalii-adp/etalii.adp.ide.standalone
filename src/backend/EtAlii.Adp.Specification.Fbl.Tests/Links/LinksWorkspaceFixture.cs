using System.Xml.Linq;
using EtAlii.Adp.Specification.Fbl.Links;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Specification.Fbl.Tests.Links;

/// <summary>
/// The conformance fixture of FBL 0.23 section 17, <c>specifications/fbl/fixtures/links-workspace/</c> of etalii.adp (spec 089),
/// vendored under <c>Conformance/etalii.adp/</c> and indexed as one workspace. Each format is read by a reader of its own here,
/// enough to find document ids, element ids, their names and links, as the fixture's own validator does, so that the index
/// does not depend on a tool type it is used to test: the timeline's and dependency graph's top-level <c>id</c> and
/// <c>elements</c> named by <c>label</c>, the knowledge file's <c>id</c> and rows named by their title property, and the mind
/// map's <c>adp-id</c> and <c>adp-link</c> node attributes, its nodes named by <c>text</c>.
/// </summary>
internal static class LinksWorkspaceFixture
{
    /// <summary>The fixture's folder, which is the workspace's root.</summary>
    private static string Root { get; } = Path.Combine(Repository.Conformance, "etalii.adp", "specifications", "fbl", "fixtures", "links-workspace").Replace('\\', '/');

    /// <summary>The fixture's <c>conformance.json</c>.</summary>
    public static string Conformance => Path.Combine(Root, "conformance.json");

    /// <summary>The workspace of every document of the fixture, each indexed as its format reads.</summary>
    public static LinkWorkspace Workspace() =>
        new(Root, Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Select(path => path.Replace('\\', '/')).Order(StringComparer.Ordinal).Select(Indexed).OfType<IndexedDocument>());

    /// <summary>The path of <paramref name="relative"/>, a path of the fixture as <c>conformance.json</c> names it.</summary>
    public static string PathOf(string relative) => $"{Root}/{relative}";

    private static IndexedDocument? Indexed(string path) => Path.GetExtension(path) switch
    {
        ".tml" or ".dgr" => Diagram(path),
        ".yaml" => Knowledge(path),
        ".mm" => Mindmap(path),
        _ => null,
    };

    private static IndexedDocument Diagram(string path)
    {
        var root = Mapping(path);
        var elements = new List<IndexedElement>();
        var links = new List<HeldLink>();
        foreach (var entry in Sequence(root, "elements").OfType<YamlMappingNode>())
        {
            var id = Scalar(entry, "id") ?? "";
            elements.Add(new IndexedElement(id, [new KeyValuePair<string, string>("label", Scalar(entry, "label") ?? "")]));
            if (Scalar(entry, "link") is { } link) links.Add(new HeldLink(id, link));
        }
        return new IndexedDocument(path, Scalar(root, "id"), "label", elements, links);
    }

    private static IndexedDocument Knowledge(string path)
    {
        var root = Mapping(path);
        var properties = Sequence(root, "properties").OfType<YamlMappingNode>().ToList();
        var names = properties.ToDictionary(property => Scalar(property, "id") ?? "", property => Scalar(property, "name") ?? "", StringComparer.Ordinal);
        var title = properties.FirstOrDefault(property => Scalar(property, "title") == "true") is { } titled ? Scalar(titled, "name") : null;
        var links = properties.Where(property => Scalar(property, "target") is not null).Select(property => new HeldLink(Scalar(property, "id")!, Scalar(property, "target")!)).ToList();
        var elements = new List<IndexedElement>();
        foreach (var row in Sequence(root, "rows").OfType<YamlMappingNode>())
        {
            var id = Scalar(row, "id") ?? "";
            var attributes = new List<KeyValuePair<string, string>>();
            foreach (var cell in Sequence(row, "cells").OfType<YamlMappingNode>())
            {
                var property = Scalar(cell, "property") ?? "";
                if (Scalar(cell, "text") is { } text) attributes.Add(new KeyValuePair<string, string>(names.GetValueOrDefault(property, property), text));
                foreach (var picked in Sequence(cell, "rows").OfType<YamlMappingNode>())
                {
                    if (Scalar(picked, "row") is { } link) links.Add(new HeldLink($"{id}/{property}", link));
                }
            }
            elements.Add(new IndexedElement(id, attributes));
        }
        return new IndexedDocument(path, Scalar(root, "id"), title, elements, links);
    }

    private static IndexedDocument Mindmap(string path)
    {
        var map = XDocument.Load(path);
        var nodes = map.Descendants("node").ToList();
        var central = nodes.FirstOrDefault();
        var elements = nodes.Select(node => new IndexedElement((string?)node.Attribute("ID") ?? "", [new KeyValuePair<string, string>("text", (string?)node.Attribute("TEXT") ?? "")])).ToList();
        var links = nodes.SelectMany(node => AttributeOf(node, "adp-link") is { } link ? [new HeldLink((string?)node.Attribute("ID") ?? "", link)] : Array.Empty<HeldLink>()).ToList();
        return new IndexedDocument(path, central is null ? null : AttributeOf(central, "adp-id"), "text", elements, links);
    }

    /// <summary>The value of the Freeplane attribute <paramref name="name"/> that <paramref name="node"/> holds itself.</summary>
    private static string? AttributeOf(XElement node, string name) =>
        node.Elements("attribute").FirstOrDefault(attribute => (string?)attribute.Attribute("NAME") == name)?.Attribute("VALUE")?.Value;

    private static YamlMappingNode Mapping(string path)
    {
        var stream = new YamlStream();
        using var reader = new StreamReader(path);
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static string? Scalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode { Value: { } value } ? value : null;

    private static IEnumerable<YamlNode> Sequence(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlSequenceNode sequence ? sequence.Children : [];
}
