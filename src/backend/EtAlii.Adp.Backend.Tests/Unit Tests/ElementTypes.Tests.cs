using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Diagram.Mindmap;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The element and relation type strings each module emits, written to the golden fixture both tiers
/// read from the backend's own constants (backend-centralization R12.1, R12.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>A generated fixture, not a maintained one.</b> <c>src/fixtures/cross-tier/element-types.json</c>
/// is compared with what <see cref="ElementTypeCatalog"/> produces; when they differ, this run's version
/// is written in its place and the test fails. So a constant changed without the fixture is a red here,
/// a deliberate change is one re-run and one commit away, and CI refuses a copy nobody regenerated -
/// the mechanism <c>ShippedExampleModelsTests</c> uses for the example models beside it.
/// </para>
/// <para>
/// The client suite reads the same file against the type ids each client module declares, so a type
/// renamed on one tier only is a red on whichever side drifted.
/// </para>
/// </remarks>
public partial class ElementTypesTests
{
    private const string Reason =
        "Written by ElementTypesTests from each diagram module's own type constants, through ElementTypeCatalog: " +
        "every element and relation type string a module puts on the wire, keyed by the module's folder under " +
        "src/diagrams. The backend's constants are the source and this file is their copy; the client suite reads " +
        "it against the type ids each client module declares (backend-centralization R12). An element is drawn as " +
        "a box of its own, a relation between two elements. Each list is sorted ordinally, and the modules by " +
        "folder name. Do not edit by hand: re-run the test and commit what it writes.";

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
    public void EveryWireTypeAModuleDeclares_IsInTheCatalog()
    {
        // Arrange: every public constant shaped like a wire type, in every application assembly that
        // is not a test assembly. Found by reflection so the catalog cannot be short by omission.
        var declared = ApplicationAssemblies.Find()
            .Where(assembly => !(assembly.GetName().Name ?? "").EndsWith(".Tests", StringComparison.Ordinal))
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
                .Select(field => (Where: $"{type.FullName}.{field.Name}", Value: (string)field.GetRawConstantValue()!)))
            .Where(constant => WireType().IsMatch(constant.Value))
            .ToList();
        var catalogued = ElementTypeCatalog.Modules
            .SelectMany(module => module.Elements.Concat(module.Relations))
            .ToHashSet(StringComparer.Ordinal);

        // Act.
        var missing = declared.Where(constant => !catalogued.Contains(constant.Value)).ToList();

        // Assert: a floor and a canary first, so a walk that found no assembly cannot pass by
        // finding nothing missing.
        Assert.True(declared.Count >= 80, $"found only {declared.Count} wire-type constants: {string.Join(", ", declared)}");
        Assert.Contains(declared, constant => constant.Value == MindmapElementMapper.NodeType);
        Assert.True(missing.Count == 0,
            "These wire types are declared by a module but missing from ElementTypeCatalog, so the cross-tier " +
            "fixture would not carry them: " + string.Join(", ", missing.Select(constant => $"{constant.Where} = {constant.Value}")));
    }

    [Fact]
    public void NoTypeString_IsListedTwice()
    {
        // Arrange.
        var all = ElementTypeCatalog.Modules.SelectMany(module => module.Elements.Concat(module.Relations)).ToList();

        // Act.
        var duplicates = all.GroupBy(type => type, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        var modules = ElementTypeCatalog.Modules.GroupBy(module => module.Module, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();

        // Assert.
        Assert.True(duplicates.Count == 0, "listed more than once: " + string.Join(", ", duplicates));
        Assert.True(modules.Count == 0, "module listed more than once: " + string.Join(", ", modules));
    }

    [Fact]
    public void EveryCataloguedModule_IsADiagramFolder()
    {
        // Arrange: the fixture's keys are what the client looks its modules up by.
        var diagrams = IoPath.Combine(RepositoryRoot(), "src", "diagrams");

        // Act.
        var strangers = ElementTypeCatalog.Modules
            .Select(module => module.Module)
            .Where(module => !Directory.Exists(IoPath.Combine(diagrams, module, "backend")))
            .ToList();

        // Assert.
        Assert.True(strangers.Count == 0, "not a module folder with a backend under src/diagrams: " + string.Join(", ", strangers));
    }

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
            foreach (var module in ElementTypeCatalog.Modules.OrderBy(module => module.Module, StringComparer.Ordinal))
            {
                json.WriteStartObject(module.Module);
                WriteSorted(json, "elements", module.Elements);
                WriteSorted(json, "relations", module.Relations);
                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static void WriteSorted(Utf8JsonWriter json, string name, IReadOnlyList<string> types)
    {
        json.WriteStartArray(name);
        foreach (var type in types.Order(StringComparer.Ordinal))
        {
            json.WriteStringValue(type);
        }

        json.WriteEndArray();
    }

    private static string FixturePath() => IoPath.Combine(RepositoryRoot(), "src", "fixtures", "cross-tier", "element-types.json");

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, "src", "fixtures", "cross-tier")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (src/fixtures/cross-tier) was not found above the test binary.");
    }

    /// <summary>The shape every module's element type takes on the wire: <c>vendor/type+kind</c>.</summary>
    [GeneratedRegex(@"^[a-z0-9-]+/[a-z0-9-]+\+[a-z0-9-]+$")]
    private static partial Regex WireType();
}
