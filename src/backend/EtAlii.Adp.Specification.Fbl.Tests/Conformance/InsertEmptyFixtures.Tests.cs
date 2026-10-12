using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Conformance;

/// <summary>
/// The fixture pair <c>insert-empty-yaml</c> and <c>insert-empty-json</c> (FBL 0.6, section 6.2: an
/// insert applies each attribute binding's <c>empty</c> rule, with a set's defaults), run against the
/// library as it is today. Spec 025 task T038 vendored them to show <c>YamlFamily.WireValue</c> and
/// <c>JsonFamily.WireValue</c> disagreeing about an empty value on insert (D9, behaviour-changes B-09);
/// the code is left unchanged until T216 corrects it.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here that does not compare with the fixture records where a family departs from
/// it today, and is the D9 divergence recorded as B-09 (and, for json, the layout of a new entry,
/// which is not D9). T216 replaces this class with the plain fixture run: it removes the two
/// fixtures from <see cref="Departing"/>, so that <see cref="ConformanceFixturesTests"/> runs them
/// like every other, and deletes the rest. A step a family already passes is compared with the
/// fixture as it stands.
/// </para>
/// <para>
/// The assertions are exact on purpose: a change to either family's <c>WireValue</c> reddens them,
/// which is the moment to replace them, not to edit them.
/// </para>
/// </remarks>
public class InsertEmptyFixturesTests
{
    /// <summary>
    /// The fixtures the library departs from today, which <see cref="ConformanceFixturesTests"/> leaves
    /// to this class rather than running as passing. T216 empties it.
    /// </summary>
    internal static readonly IReadOnlySet<string> Departing = new HashSet<string>(StringComparer.Ordinal) { "insert-empty-json", "insert-empty-yaml" };

    [Fact]
    public void TheYamlFamilyDepartsFromTheFixtureOnlyAsRecorded()
    {
        // Arrange.
        var run = new Run("insert-empty-yaml");

        // Act and assert, step 1: FBL 0.6 refuses an add that gives refused (empty refuse) its empty
        // value. Today it is planned, and every empty value but kept's (empty keep) is left out.
        Assert.True(run.Expected(1).TryGetProperty("refused", out _));
        var first = run.Add(1);
        Assert.Equal([new Splice(SpliceOperation.InsertEntry, 100, 100, "  - id: c2\r\n    kept: \"\"\r\n")], first);
        run.UndoOutsideTheFixture();

        // Step 2: FBL 0.6 writes undeclared (no empty, so keep, the default for a string) with the
        // empty string. Today it is left out, because the family keeps an empty value only under
        // an explicit empty keep. The rest of the splice is the fixture's.
        var second = Assert.Single(run.Add(2));
        var expected = Assert.Single(run.ExpectedSplices(2));
        Assert.Equal(expected.Text.Replace("    undeclared: \"\"\r\n", "", StringComparison.Ordinal), second.Text);
        Assert.NotEqual(expected.Text, second.Text);
        Assert.Equal((expected.Operation, expected.Start, expected.End), (second.Operation, second.Start, second.End));

        // Step 3: the undo takes back what step 2 wrote, which is shorter than the fixture's, and
        // leaves the fixture's document.
        Assert.Equal([new Splice(SpliceOperation.InsertEntry, 100, 126, "")], run.Undo());
        Assert.Equal(run.ExpectedDocument(3), run.Document);

        // Steps 4 and 5: an add that gives no attribute, and its undo, are the fixture's.
        run.StepAsTheFixture(4);
        run.StepAsTheFixture(5);
    }

    [Fact]
    public void TheJsonFamilyDepartsFromTheFixtureOnlyAsRecorded()
    {
        // Arrange.
        var run = new Run("insert-empty-json");

        // Act and assert, step 1: FBL 0.6 refuses an add that gives refused (empty refuse) its empty
        // value. Today it is planned, with every value the add gives, empty or not.
        Assert.True(run.Expected(1).TryGetProperty("refused", out _));
        var first = run.Add(1);
        Assert.Equal([new Splice(SpliceOperation.InsertEntry, 155, 155, ",\r\n    { \"id\": \"c2\", \"kept\": \"\", \"removed\": \"\", \"refused\": \"\", \"undeclared\": \"\" }")], first);
        run.UndoOutsideTheFixture();

        // Step 2: FBL 0.6 leaves removed (empty remove) out. Today it is written, because the family
        // writes every value that is not null. The family also writes the new entry on one line
        // where the fixture expects it on several (not D9: the layout of a new json entry).
        var second = Assert.Single(run.Add(2));
        Assert.Equal(new Splice(SpliceOperation.InsertEntry, 155, 155, ",\r\n    { \"id\": \"c2\", \"kept\": \"\", \"removed\": \"\", \"undeclared\": \"\" }"), second);
        Assert.NotEqual(run.ExpectedSplices(2), [second]);

        // Step 3: the undo takes back what step 2 wrote and leaves the fixture's document.
        Assert.Equal([new Splice(SpliceOperation.InsertEntry, 155, 221, "")], run.Undo());
        Assert.Equal(run.ExpectedDocument(3), run.Document);

        // Step 4: the keys are the fixture's (the id alone); only the one-line layout departs.
        var fourth = Assert.Single(run.Add(4));
        Assert.Equal(new Splice(SpliceOperation.InsertEntry, 155, 155, ",\r\n    { \"id\": \"c3\" }"), fourth);
        Assert.Equal(Keys(Assert.Single(run.ExpectedSplices(4)).Text), Keys(fourth.Text));

        // Step 5: the undo leaves the fixture's document.
        Assert.Equal([new Splice(SpliceOperation.InsertEntry, 155, 176, "")], run.Undo());
        Assert.Equal(run.ExpectedDocument(5), run.Document);
    }

    /// <summary>
    /// D9 itself: the same add (step 2 of each fixture, the same in both) gives the two families
    /// different keys, where the fixtures give both the same: id, kept and undeclared.
    /// </summary>
    [Fact]
    public void TheTwoFamiliesWriteDifferentKeysForTheSameAdd()
    {
        // Arrange.
        var yaml = new Run("insert-empty-yaml");
        var json = new Run("insert-empty-json");
        Assert.Equal(yaml.Expected(2).GetProperty("edit").GetRawText(), json.Expected(2).GetProperty("edit").GetRawText());

        // Act.
        var yamlKeys = Keys(Assert.Single(yaml.Add(2)).Text);
        var jsonKeys = Keys(Assert.Single(json.Add(2)).Text);

        // Assert.
        Assert.Equal(["id", "kept"], yamlKeys);
        Assert.Equal(["id", "kept", "removed", "undeclared"], jsonKeys);
        string[] fixtures = ["id", "kept", "undeclared"];
        Assert.Equal(fixtures, Keys(Assert.Single(yaml.ExpectedSplices(2)).Text));
        Assert.Equal(fixtures, Keys(Assert.Single(json.ExpectedSplices(2)).Text));
    }

    /// <summary>The keys a new yaml or json entry's text names, in order.</summary>
    private static string[] Keys(string text) =>
        [.. Regex.Matches(text, "(?:^|[\\s{,])(?:\"(?<key>[a-z]+)\"|(?<key>[a-z]+)): ").Select(match => match.Groups["key"].Value)];

    /// <summary>One fixture's body, opened through the binding beside it, and the fixture's steps.</summary>
    private sealed class Run
    {
        private readonly JsonElement[] _steps;
        private readonly OpenBody _body;

        public Run(string name)
        {
            var folder = Path.Combine(Repository.Conformance, "fixtures", name);
            var fixturePath = Path.Combine(folder, "fixture.json");
            using var fixture = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
            var root = fixture.RootElement.Clone();
            var binding = FblDocumentLoader.ResolveReference(root.GetProperty("binding").GetString()!, fixturePath, out var problems);
            Assert.DoesNotContain(problems, p => p.Severity == ProblemSeverity.Error);
            var input = root.GetProperty("input").GetString()!;
            _body = OpenBody.Open(File.ReadAllBytes(Path.Combine(folder, input)), binding, new FblOptions { FileName = input });
            _steps = [.. root.GetProperty("steps").EnumerateArray()];
        }

        public byte[] Document => _body.Bytes;

        public JsonElement Expected(int step) => _steps[step - 1];

        public List<Splice> ExpectedSplices(int step) =>
            [.. Expected(step).GetProperty("splices").EnumerateArray().Select(s => new Splice(Splice.Parse(s.GetProperty("operation").GetString()!), s.GetProperty("start").GetInt32(), s.GetProperty("end").GetInt32(), s.GetProperty("text").GetString()!))];

        public byte[] ExpectedDocument(int step) => Encoding.UTF8.GetBytes(Expected(step).GetProperty("expect").GetString()!);

        /// <summary>Plans and applies the add of a step, which must be planned, and returns its splices.</summary>
        public IReadOnlyList<Splice> Add(int step)
        {
            var add = Expected(step).GetProperty("edit").GetProperty("add");
            var attributes = add.TryGetProperty("attributes", out var given)
                ? given.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.GetString(), StringComparer.Ordinal)
                : [];
            var result = _body.Change(new ModelChange.Add(add.GetProperty("type").GetString()!, add.GetProperty("id").GetString(), attributes));
            var planned = Assert.IsType<PlanResult.Planned>(result);
            return planned.Edit.Splices;
        }

        public IReadOnlyList<Splice> Undo() => Assert.IsType<UndoResult.Done>(_body.Undo()).Splices;

        /// <summary>Takes back an edit the fixture says is refused, so the next step starts where the fixture's does.</summary>
        public void UndoOutsideTheFixture() => Undo();

        /// <summary>Runs a step the family already passes, and compares its splices and its document with the fixture's.</summary>
        public void StepAsTheFixture(int step)
        {
            var actual = Expected(step).TryGetProperty("undo", out _) ? Undo() : Add(step);
            Assert.Equal(ExpectedSplices(step), actual);
            Assert.Equal(ExpectedDocument(step), Document);
        }
    }
}
