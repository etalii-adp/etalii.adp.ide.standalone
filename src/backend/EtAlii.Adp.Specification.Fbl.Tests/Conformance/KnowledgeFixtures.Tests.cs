using System.Globalization;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Conformance;

/// <summary>
/// The Knowledge designer's bindings and fixtures, vendored from etalii-adp/etalii.adp under
/// <c>Conformance/etalii.adp</c> in that repository's own layout, read through this library
/// (knowledge-designer task 8, Requirements 2.4 and 2.7): what each fixture's <c>read</c> says is
/// read, an unchanged save writes nothing, and the same table in YAML, JSON and XML is one model.
/// </summary>
/// <remarks>
/// The fixtures' edit steps are not run here yet: they need reordering, positioned adds and
/// several values per edit, which this library gains in the rest of task 8.
/// </remarks>
public class KnowledgeFixturesTests
{
    private static string Root => Path.Combine(Repository.Conformance, "etalii.adp");

    private static string FixturesRoot => Path.Combine(Root, "specifications", "fbl", "fixtures");

    /// <summary>
    /// Fixtures whose listed findings this library does not report yet, each with its reason. The
    /// test then asserts that NONE is reported, so an entry here fails the day the library catches
    /// up and cannot outlive the gap it excuses.
    /// </summary>
    /// <remarks>
    /// <c>knowledge-kept</c>: a cell whose <c>number</c> is "about half a million" must be an
    /// unreadable entry (FBL §7.4, a value that does not convert to the attribute's type). The
    /// attribute's type is the specification's, and this library is not yet told it; today the
    /// value is read as text and the cell is an element. The rest of knowledge-designer task 8
    /// gives the reading the specification's attribute types.
    /// </remarks>
    private static readonly string[] NotYetReported = ["knowledge-kept"];

    /// <summary>The number of knowledge fixtures vendored: fewer means the enumeration broke.</summary>
    private const int VendoredFixtures = 7;

    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (var fixture in Directory.GetFiles(FixturesRoot, "fixture.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(FixturesRoot, Path.GetDirectoryName(fixture)!).Replace('\\', '/'));
        }
        return data;
    }

    [Fact]
    public void TheFixturesAreFound()
    {
        Assert.Equal(VendoredFixtures, Fixtures().Count);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheFixtureIsReadAsItSays(string name)
    {
        // Arrange and act.
        (JsonElement fixture, OpenBody body, _) = Open(name);
        var model = body.Model;
        var read = fixture.GetProperty("read");

        // Assert: readable, every element the fixture lists is read under that id and type.
        Assert.False(model.Unreadable);
        foreach (var element in read.GetProperty("elements").EnumerateArray())
        {
            var id = element.GetProperty("id").GetString()!;
            var found = model.Find(id);
            Assert.True(found is not null, $"{name}: no element '{id}' was read; read: {string.Join(", ", model.Elements.Select(e => $"{e.Id} ({e.Type})"))}.");
            Assert.Equal(element.GetProperty("type").GetString(), found.Type);
        }

        // Assert: the findings it lists and no others - a clean file is read without one.
        var expected = read.TryGetProperty("findings", out var findings) && !NotYetReported.Contains(name)
            ? findings.EnumerateArray().Select(f => (Code: f.GetProperty("rule").GetString()!, Line: f.GetProperty("line").GetInt32())).ToList()
            : [];
        Assert.Equal(
            expected.Order(),
            model.Findings.Select(f => (f.Code, Line: f.Location?.Line ?? 0)).Order());
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void SavingWithoutAnEdit_WritesTheBytesThatWereRead(string name)
    {
        // Arrange.
        (_, OpenBody body, byte[] input) = Open(name);

        // Act.
        var result = body.Change(new ModelChange.Save());

        // Assert.
        var planned = Assert.IsType<PlanResult.Planned>(result);
        Assert.Empty(planned.Edit.Splices);
        Assert.Equal(input, body.Bytes);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryByteOfTheInputBelongsToTheReading(string name)
    {
        // Arrange.
        (JsonElement fixture, _, byte[] input) = Open(name);
        var binding = BindingOf(fixture, name);

        // Act.
        var reading = Rules.BodyReading.Read(input, binding, new FblOptions { FileName = "cities", DeriveId = KnowledgeIds.Derive });

        // Assert: the byte-coverage invariant of FBL §4.1.
        Assert.Null(reading.Unreadable);
        Assert.Empty(reading.Family.Unaccounted());
    }

    [Fact]
    public void TheSameTableInYamlJsonAndXml_IsOneModel()
    {
        // Arrange and act: the definition's own example, once per format.
        var yaml = Shape(Open("knowledge-equivalence/yaml").Body.Model);
        var json = Shape(Open("knowledge-equivalence/json").Body.Model);
        var xml = Shape(Open("knowledge-equivalence/xml").Body.Model);

        // Assert: the same elements, in the same order, with the same parents and values.
        Assert.NotEmpty(yaml);
        Assert.Equal(yaml, json);
        Assert.Equal(yaml, xml);
    }

    /// <summary>
    /// A model as what a format must not change: each element's id, type, parent, slot and values.
    /// A value is compared by its written form, since XML holds every value as text and YAML and
    /// JSON hold numbers and booleans as such.
    /// </summary>
    private static List<string> Shape(FblModel model) =>
        model.Elements
            .Select(element =>
                $"{element.Id} {element.Type} in {element.ParentId ?? "-"}/{element.ParentSlot ?? "-"}: " +
                string.Join(", ", element.Attributes
                    .Where(attribute => attribute.Value is not null)
                    .OrderBy(attribute => attribute.Key, StringComparer.Ordinal)
                    .Select(attribute => $"{attribute.Key}={Written(attribute.Value)}")))
            .ToList();

    private static string Written(object? value) => value switch
    {
        bool flag => flag ? "true" : "false",
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value?.ToString() ?? "",
    };

    private static FblBinding BindingOf(JsonElement fixture, string name)
    {
        var fixturePath = Path.Combine(FixturesRoot, name, "fixture.json");
        var binding = FblDocumentLoader.ResolveReference(fixture.GetProperty("binding").GetString()!, fixturePath, out var problems);
        Assert.DoesNotContain(problems, problem => problem.Severity == ProblemSeverity.Error);
        return binding;
    }

    private static (JsonElement Fixture, OpenBody Body, byte[] Input) Open(string name)
    {
        var folder = Path.Combine(FixturesRoot, name);
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "fixture.json")));
        var fixture = document.RootElement.Clone();
        var inputName = fixture.GetProperty("input").GetString()!;
        var input = File.ReadAllBytes(Path.GetFullPath(Path.Combine(folder, inputName)));
        var body = OpenBody.Open(input, BindingOf(fixture, name), new FblOptions { FileName = Path.GetFileName(inputName), DeriveId = KnowledgeIds.Derive });
        return (fixture, body, input);
    }
}
