using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Diagram.Mindmap;
using EtAlii.Adp.Diagram.Timeline;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Modules.Tests;

/// <summary>
/// The element and relation type strings each module emits, written to the golden fixture both tiers
/// read from the backend's own constants (backend-centralization R12.1, R12.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>A generated fixture, not a maintained one.</b> <c>src/fixtures/cross-tier/element-types.json</c>
/// is compared with what <see cref="WireTypes"/> reads off the modules' constants; when they differ, this run's version
/// is written in its place and the test fails. So a constant changed without the fixture is a red here,
/// a deliberate change is one re-run and one commit away, and CI refuses a copy nobody regenerated -
/// the mechanism <c>ShippedExampleModelsTests</c> uses for the example models beside it.
/// </para>
/// <para>
/// The client suite reads the same file against the type ids each client module declares, so a type
/// renamed on one tier only is a red on whichever side drifted.
/// </para>
/// </remarks>
public class ElementTypesTests
{
    private const string Reason =
        "Written by ElementTypesTests from each diagram module's own type constants: " +
        "every element and relation type string a module puts on the wire, keyed by the module's folder under " +
        "src/diagrams. The backend's constants are the source and this file is their copy; the client suite reads " +
        "it against the type ids each client module declares (backend-centralization R12). An element is drawn as " +
        "a box of its own, a relation (a constant marked [Relation]) between two elements. Each list is sorted " +
        "ordinally, and the modules by folder name. Do not edit by hand: re-run the test and commit what it writes.";

    [Fact]
    public void TheFixture_IsWhatTheBackendsConstantsProduce()
    {
        // Arrange.
        var path = FixturePath();
        var produced = Produce();

        // Act.
        var existing = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;
        var stale = existing != produced;
        if (stale)
        {
            File.WriteAllText(path, produced.ReplaceLineEndings("\r\n"), new UTF8Encoding(false));
        }

        // Assert.
        Assert.False(stale,
            $"{path} did not match the backend's type constants and has been rewritten from them - " +
            "review the difference and commit it, and bring the client's declared type ids along.");
    }

    [Fact]
    public void EveryWireTypeAModuleDeclares_BelongsToADiagramModule()
    {
        // Arrange.
        var declared = WireTypes.All;

        // Act: a wire-type constant outside every module folder would be left out of the fixture.
        var strangers = declared.Where(type => type.Module.Length == 0).ToList();

        // Assert: a floor and a canary first, so a walk that found no assembly cannot pass by
        // finding no stranger.
        Assert.True(declared.Count >= 80, $"found only {declared.Count} wire-type constants: {string.Join(", ", declared)}");
        Assert.Contains(declared, type => type is { Value: MindmapElementMapper.NodeType, Module: "mindmap", IsRelation: false });
        Assert.Contains(declared, type => type is { Value: TimelineElementMapper.ConnectionType, Module: "timeline", IsRelation: true });
        Assert.True(strangers.Count == 0,
            "These wire types are declared outside every module folder under src/diagrams, so the cross-tier " +
            "fixture cannot say whose they are: " + string.Join(", ", strangers.Select(type => $"{type.Where} = {type.Value}")));
    }

    [Fact]
    public void NoTypeString_IsDeclaredTwiceOver()
    {
        // Arrange.
        var byValue = WireTypes.All.GroupBy(type => type.Value, StringComparer.Ordinal).ToList();

        // Act: one string owned by two modules, or drawn both as a box and as a line.
        var shared = byValue.Where(group => group.Select(type => type.Module).Distinct(StringComparer.Ordinal).Count() > 1).ToList();
        var split = byValue.Where(group => group.Select(type => type.IsRelation).Distinct().Count() > 1).ToList();

        // Assert.
        Assert.True(shared.Count == 0, "declared by more than one module: " + Describe(shared));
        Assert.True(split.Count == 0, "declared as a relation and as an element: " + Describe(split));
    }

    [Fact]
    public void EveryRelationMark_IsOnAWireTypeConstant()
    {
        // Arrange: the mark is read only off wire-type constants, so anywhere else it would say nothing.
        var marked = WireTypes.Assemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(field => field.IsDefined(typeof(RelationAttribute), inherit: false))
            .ToList();

        // Act.
        var misplaced = marked.Where(field => !WireTypes.IsWireType(field)).Select(field => $"{field.DeclaringType?.FullName}.{field.Name}").ToList();

        // Assert.
        Assert.Contains(marked, field => field.Name == nameof(TimelineElementMapper.ConnectionType));
        Assert.True(misplaced.Count == 0, "[Relation] on a field that is not a public wire-type constant: " + string.Join(", ", misplaced));
    }

    private static string Describe(IEnumerable<IGrouping<string, WireTypes.WireType>> groups) =>
        string.Join("; ", groups.Select(group => $"{group.Key} ({string.Join(", ", group.Select(type => type.Where))})"));

    private static string Produce()
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions
               {
                   Indented = true,
                   NewLine = "\n",
                   Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
               }))
        {
            json.WriteStartObject();
            json.WriteString("reason", Reason);
            json.WriteStartObject("modules");
            foreach (var module in WireTypes.All.GroupBy(type => type.Module, StringComparer.Ordinal).OrderBy(module => module.Key, StringComparer.Ordinal))
            {
                json.WriteStartObject(module.Key);
                WriteSorted(json, "elements", module.Where(type => !type.IsRelation));
                WriteSorted(json, "relations", module.Where(type => type.IsRelation));
                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static void WriteSorted(Utf8JsonWriter json, string name, IEnumerable<WireTypes.WireType> types)
    {
        json.WriteStartArray(name);
        foreach (var type in types.Select(type => type.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            json.WriteStringValue(type);
        }

        json.WriteEndArray();
    }

    private static string FixturePath() => IoPath.Combine(RepositoryPaths.Root, "src", "fixtures", "cross-tier", "element-types.json");
}
