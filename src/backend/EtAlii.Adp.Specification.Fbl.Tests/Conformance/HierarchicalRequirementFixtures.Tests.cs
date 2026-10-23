using System.Globalization;
using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Conformance;

/// <summary>
/// The Hierarchical Requirement Designer's fixtures, vendored from etalii-adp/etalii.adp under <c>Conformance/etalii.adp</c>
/// (hierarchical-requirement task T044, FR-063): each is run as FBL 0.7 section 15.3 says a host passes one, through
/// <see cref="HierarchicalRequirementDocument"/>, which stands in for what DESL adds to the library. What a fixture's
/// <c>read</c> lists is read, every step plans exactly its splices and leaves exactly its document, or is refused with
/// nothing written, an unchanged save writes nothing, and every byte of a readable input belongs to the reading.
/// </summary>
/// <remarks>
/// Of a fixture's findings the run compares those the library reports: the FBL codes and std.unparseable,
/// std.unreadableEntry and std.duplicateId. The others are DESL's (a missing id, containment, a mention, a required
/// value), which the module's own tests hold the designer to.
/// </remarks>
public class HierarchicalRequirementFixturesTests
{
    /// <summary>The number of fixtures vendored: fewer means the enumeration broke.</summary>
    private const int VendoredFixtures = 23;

    private static string FixturesRoot => Path.Combine(Repository.Conformance, "etalii.adp", "specifications", "fbl", "fixtures");

    /// <summary>Every fixture whose folder starts with <c>hierarchical-requirement-</c>, at any depth.</summary>
    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (var fixture in Directory.GetFiles(FixturesRoot, "fixture.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Path.GetRelativePath(FixturesRoot, Path.GetDirectoryName(fixture)!).Replace('\\', '/');
            if (name.StartsWith("hierarchical-requirement-", StringComparison.Ordinal)) data.Add(name);
        }
        return data;
    }

    [Fact]
    public void TheFixturesAreFound()
    {
        Assert.Equal(VendoredFixtures, Fixtures().Count);
    }

    [Fact]
    public void TheSpecificationIsRead()
    {
        // Assert: a stand-in told nothing would read every attribute as text and every list as absent.
        Assert.True(HierarchicalRequirementSpecification.Many("Requirement", "tags"));
        Assert.False(HierarchicalRequirementSpecification.Many("Requirement", "category"));
        Assert.Equal("bool", HierarchicalRequirementSpecification.TypeOf("Assessment", "resolved"));
        Assert.Contains("Unreadable", HierarchicalRequirementSpecification.UnreadableTypes());
        Assert.Equal("question", HierarchicalRequirementSpecification.Key("AssessmentKind", "Question"));
        Assert.Equal(3, HierarchicalRequirementSpecification.Initial("categories").Count);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheFixturePasses(string name)
    {
        var folder = Path.Combine(FixturesRoot, name);
        Run(name, folder, File.ReadAllBytes(Path.Combine(folder, "fixture.json")));
    }

    /// <summary>
    /// The run fails on a fixture one of whose splices is off by a byte: a copy of <c>hierarchical-requirement-structure</c>
    /// made in the test's temporary folder, the first splice of its first step ending one byte later.
    /// </summary>
    [Fact]
    public void TheRunFailsOnASpliceThatIsOffByOneByte()
    {
        // Arrange.
        var folder = Path.Combine(FixturesRoot, "hierarchical-requirement-structure");
        var fixture = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "fixture.json")))!;
        var splice = fixture["steps"]![0]!["splices"]![0]!;
        splice["end"] = splice["end"]!.GetValue<int>() + 1;

        // Act.
        var failure = Record.Exception(() => Run("hierarchical-requirement-structure", folder, Encoding.UTF8.GetBytes(fixture.ToJsonString())));

        // Assert: it fails at that step's splices.
        Assert.NotNull(failure);
        Assert.Contains("step 1: expected splices", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void SavingWithoutAnEdit_WritesTheBytesThatWereRead(string name)
    {
        // Arrange.
        (string folder, string inputName, FblBinding binding) = Open(name);
        var document = HierarchicalRequirementDocument.Open(folder, inputName, binding);
        var before = document.Bytes;

        // Act.
        var result = document.Change(JsonDocument.Parse("""{ "save": true }""").RootElement);

        // Assert.
        var planned = Assert.IsType<PlanResult.Planned>(result);
        Assert.Empty(planned.Edit.Splices);
        Assert.Equal(before, document.Bytes);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryByteOfAReadableInputBelongsToTheReading(string name)
    {
        // Arrange.
        (string folder, string inputName, FblBinding binding) = Open(name);
        if (inputName.EndsWith(".adp", StringComparison.OrdinalIgnoreCase)) return;
        var input = File.ReadAllBytes(Path.Combine(folder, inputName));

        // Act.
        var reading = Rules.BodyReading.Read(input, binding, HierarchicalRequirementSpecification.Options(inputName));

        // Assert: the byte-coverage invariant of FBL section 4.1, for the inputs that are read at all.
        if (reading.Unreadable is not null) return;
        Assert.Empty(reading.Family.Unaccounted());
    }

    private static (string Folder, string InputName, FblBinding Binding) Open(string name)
    {
        var folder = Path.Combine(FixturesRoot, name);
        var fixturePath = Path.Combine(folder, "fixture.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        var fixture = document.RootElement.Clone();
        var binding = FblDocumentLoader.ResolveReference(fixture.GetProperty("binding").GetString()!, fixturePath, out var problems);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        return (folder, fixture.GetProperty("input").GetString()!, binding);
    }

    /// <summary>Runs a fixture's reading and steps; its binding and input are found from <paramref name="folder"/>.</summary>
    private static void Run(string name, string folder, byte[] fixtureBytes)
    {
        // Arrange.
        using var parsed = JsonDocument.Parse(fixtureBytes);
        var root = parsed.RootElement;
        var fixturePath = Path.Combine(folder, "fixture.json");
        var binding = FblDocumentLoader.ResolveReference(root.GetProperty("binding").GetString()!, fixturePath, out var problems);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        var inputName = root.GetProperty("input").GetString()!;
        var document = HierarchicalRequirementDocument.Open(folder, inputName, binding);

        // Act and assert: the reading.
        if (root.TryGetProperty("read", out var read)) AssertRead(document, read, name, inputName);

        // Act and assert: every step.
        var index = 0;
        foreach (var step in root.GetProperty("steps").EnumerateArray())
        {
            index++;
            var label = $"{name} step {index}";
            var before = document.Bytes;
            var expected = FixtureEdits.Splices(step);
            IReadOnlyList<Splice> actual;
            if (step.TryGetProperty("undo", out var undo) && undo.GetBoolean())
            {
                actual = Done(document.Undo(), label);
            }
            else if (step.TryGetProperty("redo", out var redo) && redo.GetBoolean())
            {
                actual = Done(document.Redo(), label);
            }
            else
            {
                var result = document.Change(step.GetProperty("edit"));
                if (step.TryGetProperty("refused", out _))
                {
                    var refusal = Assert.IsType<PlanResult.Refused>(result);
                    Assert.False(string.IsNullOrWhiteSpace(refusal.Reason), $"{label}: refused without a reason.");
                    Assert.Equal(before, document.Bytes);
                    actual = [];
                }
                else
                {
                    var planned = result as PlanResult.Planned;
                    Assert.True(planned is not null, $"{label}: refused with '{(result as PlanResult.Refused)?.Reason}'.");
                    actual = planned.Edit.Splices;
                }
            }
            Assert.True(expected.SequenceEqual(actual), $"{label}: expected splices\n  {string.Join("\n  ", expected)}\nbut planned\n  {string.Join("\n  ", actual)}");

            // A step that names no document leaves the one it started from.
            var expect = step.TryGetProperty("expect", out var text) ? Encoding.UTF8.GetBytes(text.GetString()!)
                : step.TryGetProperty("expectFile", out var file) ? File.ReadAllBytes(Path.Combine(folder, file.GetString()!))
                : before;
            Assert.True(expect.AsSpan().SequenceEqual(document.Bytes), $"{label}: the document differs from 'expect'.\nExpected:\n{Encoding.UTF8.GetString(expect)}\nActual:\n{Encoding.UTF8.GetString(document.Bytes)}");
        }
    }

    private static IReadOnlyList<Splice> Done(UndoResult result, string label)
    {
        var done = result as UndoResult.Done;
        Assert.True(done is not null, $"{label}: refused with '{(result as UndoResult.Refused)?.Reason}'.");
        return done.Splices;
    }

    private static readonly HashSet<string> _libraryCodes = new(StringComparer.Ordinal) { "std.unparseable", "std.unreadableEntry", "std.duplicateId" };

    private static bool IsLibrarys(string code) => code.StartsWith("fbl.", StringComparison.Ordinal) || _libraryCodes.Contains(code);

    private static void AssertRead(HierarchicalRequirementDocument document, JsonElement read, string name, string inputName)
    {
        if (read.TryGetProperty("unreadable", out var unreadable)) Assert.Equal(unreadable.GetBoolean(), document.Unreadable);
        var elements = document.Elements();
        if (read.TryGetProperty("elements", out var expected))
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var element in expected.EnumerateArray())
            {
                var id = element.GetProperty("id").GetString()!;
                var occurrence = seen[id] = seen.GetValueOrDefault(id) + 1;
                var found = elements.Where(e => e.Id == id).Skip(occurrence - 1).FirstOrDefault();
                Assert.True(found is not null, $"{name}: no element '{id}' (occurrence {occurrence}) was read; read: {string.Join(", ", elements.Select(e => $"{e.Id} ({e.Type})"))}.");
                Assert.Equal(element.GetProperty("type").GetString(), found.Type);
                if (element.TryGetProperty("parent", out var parent)) Assert.True(parent.GetString() == found.ParentId, $"{name}: '{id}' is read in '{found.ParentId}', where the fixture reads '{parent.GetString()}'.");
                if (element.TryGetProperty("attributes", out var attributes)) AssertAttributes(found, attributes, name);
            }
        }
        if (read.TryGetProperty("findings", out var findings))
        {
            var wanted = findings.EnumerateArray()
                .Select(finding => (Code: finding.GetProperty("rule").GetString()!, Line: finding.TryGetProperty("line", out var line) ? line.GetInt32() : 0))
                .Where(finding => IsLibrarys(finding.Code))
                .Order()
                .ToList();
            var actual = document.Findings(inputName).Where(finding => IsLibrarys(finding.Code)).Select(finding => (finding.Code, Line: finding.Location?.Line ?? 0)).Order().ToList();
            Assert.True(wanted.SequenceEqual(actual), $"{name}: the fixture expects the findings {string.Join(", ", wanted)}; read: {string.Join(", ", actual)}.");
        }
    }

    /// <summary>Each attribute a fixture's element names has that value and that JSON type; a list is compared member by member.</summary>
    private static void AssertAttributes(DesignerElement element, JsonElement attributes, string name)
    {
        foreach (var attribute in attributes.EnumerateObject())
        {
            var label = $"{name}: '{attribute.Name}' of '{element.Id}'";
            Assert.True(element.Attributes.TryGetValue(attribute.Name, out var actual), $"{label} was not read.");
            Assert.True(Same(attribute.Value, actual), $"{label} is {Describe(actual)}, where the fixture reads {attribute.Value.GetRawText()}.");
        }
    }

    private static bool Same(JsonElement expected, object? actual) => expected.ValueKind switch
    {
        JsonValueKind.String => actual is string text && text == expected.GetString(),
        JsonValueKind.Number => actual is long or int or double && Convert.ToDouble(actual, CultureInfo.InvariantCulture).Equals(expected.GetDouble()),
        JsonValueKind.True or JsonValueKind.False => actual is bool flag && flag == expected.GetBoolean(),
        JsonValueKind.Null => actual is null,
        JsonValueKind.Array => actual is IReadOnlyList<object?> list && list.Count == expected.GetArrayLength() && expected.EnumerateArray().Select((item, at) => Same(item, list[at])).All(same => same),
        _ => false,
    };

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string text => $"the text \"{text}\"",
        bool flag => $"the bool {(flag ? "true" : "false")}",
        IReadOnlyList<object?> list => $"the list [{string.Join(", ", list.Select(Describe))}]",
        _ => $"the {value.GetType().Name} {Convert.ToString(value, CultureInfo.InvariantCulture)}",
    };
}
