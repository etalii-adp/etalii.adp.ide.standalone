using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Conformance;

/// <summary>
/// Every round-trip fixture vendored from etalii-adp/etalii.adp, run through the library as FBL
/// §15.3 says a host passes one: reading the input gives what <c>read</c> lists, and every step
/// produces exactly its splices and its document, or its refusal with nothing written.
/// </summary>
public class ConformanceFixturesTests
{
    /// <summary>The number of fixtures vendored: fewer means the enumeration broke.</summary>
    private const int MinimumFixtures = 50;

    /// <summary>The reason a fixture is pending because of the lexer: ATD §3.6's longest match, which EtAlii.Lark's priority-first lexer does not give.</summary>
    private const string LongestMatch = "the lexer takes a keyword that only begins a longer word, as 'title Pipelines' and 'inertial' read, where ATD §3.6 takes the longest match (EtAlii.Lark; the ruling on the lexer is pending with the product owner)";

    /// <summary>
    /// The fixtures vendored before the library passes them, by name: the text of the failure each still gives, and why. They
    /// are held out of the theories; <see cref="ThePendingFixturesStillFailAsRecorded"/> fails when one passes, or fails
    /// otherwise, so that the entry is taken out, or its reason brought up to date, in the change that makes it so.
    /// </summary>
    private static readonly Dictionary<string, (string Fails, string Why)> Pending = new(StringComparer.Ordinal)
    {
        ["wardley-annotation-positions"] = ("'title' of 'title@1' was not read.", LongestMatch + "; its items (FBL 0.12 section 5.2) are read and written, as GrammarItemsTests show"),
        ["wardley-duplicate-names/same-key-twice"] = ("a splice names the file 'same-key-twice.identities.json'", "an add writes its element's identity entry in the same edit (FBL 0.12 sections 8.6 and 8.7), which etalii.adp spec 071 task T012 (F3) delivers"),
        ["wardley-legacy-identities"] = ("'title' of 'title@1' was not read.", LongestMatch + "; then a removal prunes the identity entry in the same edit (FBL 0.12 section 8.7), spec 071 task T012 (F3)"),
        ["wardley-pipelines"] = ("'title' of 'title@1' was not read.", LongestMatch),
        ["wardley-rename-identity"] = ("fbl.unparsed-text", LongestMatch + ", 'title Annotations' read as the annotations keyword; then a rename moves the identity entries' keys in the same edit (FBL 0.12 section 8.6), spec 071 task T012 (F3)"),
        ["wardley-trailer/lenient-trailer"] = ("'inertia' of 'component@8' is the bool true", LongestMatch),
    };

    /// <summary>The fixtures copied in etalii.adp's own layout, beside the Knowledge designer's.</summary>
    private const string AdpFixtures = "etalii.adp/specifications/fbl/fixtures";

    /// <summary>
    /// Every fixture, at any depth (FBL 0.7 §15.3): those under <c>fixtures/</c> by their path there, and those in
    /// etalii.adp's layout by theirs from the corpus. The Knowledge designer's are run by
    /// <see cref="KnowledgeFixturesTests"/>, with the attribute types and derived ids that tool type needs, and the Hierarchical
    /// Requirement Designer's by <see cref="HierarchicalRequirementFixturesTests"/>, through what DESL adds for it.
    /// </summary>
    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (var name in Under("fixtures", "").Concat(Under(AdpFixtures, AdpFixtures + "/")).Order(StringComparer.Ordinal))
        {
            if (Pending.ContainsKey(name)) continue;
            if (!name.StartsWith(AdpFixtures + "/knowledge-", StringComparison.Ordinal) && !name.StartsWith(AdpFixtures + "/hierarchical-requirement-", StringComparison.Ordinal)) data.Add(name);
        }
        return data;

        static IEnumerable<string> Under(string folder, string prefix)
        {
            var root = Path.Combine(Repository.Conformance, folder);
            return Directory.GetFiles(root, "fixture.json", SearchOption.AllDirectories)
                .Select(fixture => prefix + Path.GetRelativePath(root, Path.GetDirectoryName(fixture)!).Replace('\\', '/'));
        }
    }

    /// <summary>The folder of the fixture <paramref name="name"/>, as <see cref="Fixtures"/> names it.</summary>
    private static string Folder(string name) =>
        name.StartsWith(AdpFixtures + "/", StringComparison.Ordinal)
            ? Path.Combine(Repository.Conformance, name)
            : Path.Combine(Repository.Conformance, "fixtures", name);

    [Fact]
    public void TheFixturesAreFound()
    {
        Assert.True(Fixtures().Count >= MinimumFixtures, $"Only {Fixtures().Count} fixtures were found; {MinimumFixtures} are vendored.");
    }

    /// <summary>
    /// Each pending fixture, run as the theories run one, still fails with the failure its entry records: one that passes, or
    /// fails for another reason, fails here (see <see cref="Pending"/>).
    /// </summary>
    [Fact]
    public void ThePendingFixturesStillFailAsRecorded()
    {
        foreach ((string name, (string fails, string why)) in Pending)
        {
            // Act.
            var folder = Folder(name);
            var failure = Record.Exception(() => Run(name, folder, File.ReadAllBytes(Path.Combine(folder, "fixture.json"))));

            // Assert.
            Assert.True(failure is not null, $"The pending fixture {name} passes; take it out of Pending.");
            Assert.True(failure.Message.Contains(fails, StringComparison.Ordinal), $"The pending fixture {name}, pending because {why}, now fails otherwise: {failure.Message}");
        }
    }

    [Fact]
    public void TheTimelineSpecificationsAttributeTypes_AreRead()
    {
        // Assert: a reading told no type compares no written text, so an empty table would pass
        // every timeline fixture whose values YAML happens to type as the fixture does.
        Assert.True(TimelineTypes.Count >= 10, $"Only {TimelineTypes.Count} attribute types were read from timeline.dis.");
        Assert.Equal("string", TimelineTypes.Of("Period", "label"));
        Assert.Null(TimelineTypes.Of("Moment", "end"));
    }

    /// <summary>
    /// A fixture's binding reference, as this corpus holds it. A fixture of a tool type names its
    /// binding where etalii.adp keeps it, under <c>definitions/</c>, a folder this corpus does not
    /// have: the binding is vendored beside the others, and a reference whose file is not where it
    /// points is read as naming the file of that name here, at the corpus's root. The fixture itself is
    /// never edited.
    /// </summary>
    private static string BindingReference(string reference, string folder)
    {
        var hash = reference.LastIndexOf('#');
        var file = reference[..hash];
        if (file.Length == 0 || File.Exists(Path.GetFullPath(Path.Combine(folder, file)))) return reference;
        var vendored = Path.Combine(Repository.Conformance, Path.GetFileName(file));
        return Path.GetRelativePath(folder, vendored).Replace('\\', '/') + reference[hash..];
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheFixturePasses(string name)
    {
        var folder = Folder(name);
        Run(name, folder, File.ReadAllBytes(Path.Combine(folder, "fixture.json")));
    }

    /// <summary>
    /// The runner fails on a fixture whose reading names a typed value where a text is read
    /// (Requirement 5.5): a copy of <c>timeline-written-text</c> made in the test's temporary folder,
    /// one expected label changed from the string "yes" to true.
    /// </summary>
    [Fact]
    public void TheRunnerFailsOnAnAttributeValueOfAnotherType()
    {
        // Arrange.
        var folder = Path.Combine(Repository.Conformance, "fixtures", "timeline-written-text");
        var original = File.ReadAllText(Path.Combine(folder, "fixture.json"));
        var changed = original.Replace("\"label\": \"yes\"", "\"label\": true", StringComparison.Ordinal);
        Assert.NotEqual(original, changed);
        using var scratch = TestFolder.Create("fbl");
        var copy = scratch.Combine("fixture.json");
        File.WriteAllText(copy, changed);

        // Act.
        var failure = Record.Exception(() => Run("timeline-written-text", folder, File.ReadAllBytes(copy)));

        // Assert: it fails, naming the attribute.
        Assert.NotNull(failure);
        Assert.Contains("'label'", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The runner fails on a grammar fixture with one wrong offset (etalii.adp spec 072, Requirement 7.3): a copy of
    /// <c>w3c-turtle-grammar</c> whose set of the label starts one byte late, which shows that it runs byte for byte with its splices.
    /// </summary>
    [Fact]
    public void TheRunnerFailsOnAPlantedOffsetOfAGrammarFixture()
    {
        // Arrange.
        var folder = Path.Combine(Repository.Conformance, "fixtures", "w3c-turtle-grammar");
        var original = File.ReadAllText(Path.Combine(folder, "fixture.json"));
        const string planted = "\"start\": 192,";
        var at = original.IndexOf(planted, StringComparison.Ordinal);
        Assert.True(at >= 0);
        var changed = original[..at] + "\"start\": 193," + original[(at + planted.Length)..];
        using var scratch = TestFolder.Create("fbl");
        var copy = scratch.Combine("fixture.json");
        File.WriteAllText(copy, changed);

        // Act.
        var failure = Record.Exception(() => Run("w3c-turtle-grammar", folder, File.ReadAllBytes(copy)));

        // Assert.
        Assert.NotNull(failure);
        Assert.Contains("192", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Runs a fixture's reading and steps; its binding and input are found from <paramref name="folder"/>.</summary>
    private static void Run(string name, string folder, byte[] fixtureBytes)
    {
        // Arrange.
        var fixturePath = Path.Combine(folder, "fixture.json");
        using var fixture = JsonDocument.Parse(fixtureBytes);
        var root = fixture.RootElement;
        var binding = FblDocumentLoader.ResolveReference(BindingReference(root.GetProperty("binding").GetString()!, folder), fixturePath, out var problems);
        Assert.DoesNotContain(problems, p => p.Severity == LoadSeverity.Error);
        var inputName = root.GetProperty("input").GetString()!;
        var input = File.ReadAllBytes(Path.Combine(folder, inputName));
        var origin = Template(root, binding, input, name);
        var subject = Subject.Open(input, inputName, binding, folder, origin);
        var reference = BindingReference(root.GetProperty("binding").GetString()!, folder);
        var bindingText = File.ReadAllText(Path.GetFullPath(Path.Combine(folder, reference[..reference.LastIndexOf('#')])));

        // Act and assert: the reading.
        var aliases = Aliases(root, subject.Model, folder);
        if (root.TryGetProperty("read", out var read)) AssertRead(subject, read, name, binding, aliases);

        // Act and assert: every step.
        var index = 0;
        foreach (var step in root.GetProperty("steps").EnumerateArray())
        {
            index++;
            var label = $"{name} step {index}";
            var before = subject.Bytes;
            var expected = FixtureEdits.Splices(step);
            IReadOnlyList<Splice> actual;
            subject.Expect(step, label);
            if (step.TryGetProperty("undo", out var undo) && undo.GetBoolean())
            {
                actual = subject.Undo(label);
            }
            else if (step.TryGetProperty("redo", out var redo) && redo.GetBoolean())
            {
                actual = subject.Redo(label);
            }
            else
            {
                var result = subject.Change(ReadChange(Aliased(step.GetProperty("edit"), aliases), subject.Model));
                if (step.TryGetProperty("refused", out var refused))
                {
                    var refusal = Assert.IsType<PlanResult.Refused>(result);
                    // FBL words a refusal only where the binding gives the words (a `readOnly` text, a
                    // `when` with a reason); elsewhere the reason is the host's own, and a fixture's
                    // text for it is how its author would say it, not something a host can be held to.
                    if (bindingText.Contains(refused.GetString()!, StringComparison.Ordinal)) Assert.Equal(refused.GetString(), refusal.Reason);
                    Assert.False(string.IsNullOrWhiteSpace(refusal.Reason), $"{label}: refused without a reason.");
                    Assert.Equal(before, subject.Bytes);
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

            // FBL 0.10: the bytes after the step of each file beside the input that its splices name.
            if (step.TryGetProperty("expectFiles", out var files)) subject.AssertFiles(files, label);

            // A refused step may leave out the document it leaves, which is the one before it, as compared above; and a
            // step whose splices are the body's a registration opens states that body's bytes in expectFiles alone.
            if (!step.TryGetProperty("expect", out _) && !step.TryGetProperty("expectFile", out _)) continue;
            var expect = step.TryGetProperty("expect", out var text)
                ? Encoding.UTF8.GetBytes(text.GetString()!)
                : File.ReadAllBytes(Path.Combine(folder, step.GetProperty("expectFile").GetString()!));
            Assert.True(expect.AsSpan().SequenceEqual(subject.Bytes), $"{label}: the document differs from 'expect'.\nExpected:\n{Encoding.UTF8.GetString(expect)}\nActual:\n{Encoding.UTF8.GetString(subject.Bytes)}");
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryByteOfTheInputBelongsToTheReading(string name)
    {
        // Arrange.
        var folder = Folder(name);
        var fixturePath = Path.Combine(folder, "fixture.json");
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        var inputName = fixture.RootElement.GetProperty("input").GetString()!;
        if (inputName.EndsWith(".adp", StringComparison.OrdinalIgnoreCase)) return;
        var binding = FblDocumentLoader.ResolveReference(BindingReference(fixture.RootElement.GetProperty("binding").GetString()!, folder), fixturePath, out _);

        // Act.
        var reading = Rules.BodyReading.Read(File.ReadAllBytes(Path.Combine(folder, inputName)), binding, new FblOptions { FileName = inputName });

        // Assert: the byte-coverage invariant of FBL §4.1.
        Assert.Null(reading.Unreadable);
        Assert.Empty(reading.Family.Unaccounted());
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void AnUnchangedSaveOfTheInputPlansNoSpliceAndKeepsItsBytes(string name)
    {
        // Arrange.
        var folder = Folder(name);
        var fixturePath = Path.Combine(folder, "fixture.json");
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        var inputName = fixture.RootElement.GetProperty("input").GetString()!;
        var binding = FblDocumentLoader.ResolveReference(BindingReference(fixture.RootElement.GetProperty("binding").GetString()!, folder), fixturePath, out _);
        var input = File.ReadAllBytes(Path.Combine(folder, inputName));
        var subject = Subject.Open(input, inputName, binding, folder);

        // Act.
        var result = subject.Change(new ModelChange.Save());

        // Assert.
        var planned = Assert.IsType<PlanResult.Planned>(result);
        Assert.Empty(planned.Edit.Splices);
        Assert.Equal(input, subject.Bytes);
    }

    /// <summary>
    /// A fixture's <c>template</c> (FBL 0.10 §15.3): its input is the new document the binding's template gives for that
    /// file name and origin, written by the library and compared byte for byte, and it is read as that origin's reading.
    /// Null, the binding's every rule, for a fixture without one.
    /// </summary>
    private static string? Template(JsonElement root, FblBinding binding, byte[] input, string name)
    {
        if (!root.TryGetProperty("template", out var template)) return null;
        var origin = template.GetProperty("origin").GetString()!;
        var written = Fbl.Routing.TemplateWriter.Produce(binding, origin, template.GetProperty("name").GetString()!, rule => throw new InvalidOperationException($"{name}: a template stated by a fixture makes no id ({rule})."));
        Assert.True(written is not null && written.AsSpan().SequenceEqual(input), $"{name}: the input is not what the binding's template writes for {template.GetRawText()}.");
        return origin;
    }

    private static void AssertRead(Subject subject, JsonElement read, string name, FblBinding binding, IReadOnlyDictionary<string, string> aliases)
    {
        var model = subject.Model!;
        var documentType = NaturalIds.DocumentType(binding.Name);
        if (read.TryGetProperty("unreadable", out var unreadable)) Assert.Equal(unreadable.GetBoolean(), model.Unreadable);
        if (documentType is not null && read.TryGetProperty("document", out var document))
        {
            // FBL 0.7 §15.3: the attributes of the document, the element of the type the tool type maps as the diagram.
            var diagram = model.Elements.FirstOrDefault(element => element.Type == documentType);
            Assert.True(diagram is not null, $"{name}: no {documentType} was read as the document.");
            if (document.TryGetProperty("attributes", out var attributes)) AssertAttributes(Document(model, diagram, binding.Name), attributes, name);
        }
        if (read.TryGetProperty("elements", out var elements))
        {
            // A fixture that lists no element says the reading has none but the document (FBL 0.7 §15.3).
            if (elements.GetArrayLength() == 0)
            {
                var read0 = model.Elements.Where(element => element.Type != documentType).ToList();
                Assert.True(read0.Count == 0, $"{name}: the fixture expects no element; read: {string.Join(", ", read0.Select(e => $"{e.Id} ({e.Type})"))}.");
            }
            foreach (var element in elements.EnumerateArray())
            {
                var id = element.GetProperty("id").GetString()!;
                var type = element.GetProperty("type").GetString()!;
                var found = model.Find(aliases.GetValueOrDefault(id) ?? id);
                Assert.True(found is not null, $"{name}: no element '{id}' was read; read: {string.Join(", ", model.Elements.Select(e => $"{e.Id} ({e.Type})"))}.");
                Assert.Equal(type, found.Type);
                if (element.TryGetProperty("parent", out var parent)) Assert.True((parent.GetString() is { } named ? aliases.GetValueOrDefault(named) ?? named : null) == found.ParentId, $"{name}: '{id}' is read in '{found.ParentId}', where the fixture reads '{parent.GetString()}'.");
                if (element.TryGetProperty("attributes", out var attributes)) AssertAttributes(Typed(found, binding.Name), attributes, name);
            }
        }
        if (read.TryGetProperty("findings", out var findings))
        {
            // A fixture that lists no finding says the reading has none: a reference that does not
            // resolve, an entry without its id, would each be one.
            // The findings the tool type turns off are DISL's, not the library's to compare (DISL constraints).
            var disabled = NaturalIds.DisabledFindings(binding.Name);
            var reported = model.Findings.Where(f => !disabled.Contains(f.Code)).ToList();
            if (findings.GetArrayLength() == 0)
            {
                Assert.True(reported.Count == 0, $"{name}: the fixture expects no finding; read: {string.Join("; ", reported.Select(f => $"{f.Code} {f.Message}"))}");
            }
            foreach (var finding in findings.EnumerateArray())
            {
                var code = finding.GetProperty("rule").GetString()!;
                int? line = finding.TryGetProperty("line", out var l) ? l.GetInt32() : null;
                Assert.Contains(model.Findings, f => f.Code == code && (line is null || f.Location?.Line == line));
            }
        }
    }

    /// <summary>
    /// The read element each of the fixture's elements stands for where the reading holds no element of its id and the binding
    /// stores none for it: a fixture must name every element (FBL §15.3), and where FBL reads no id the name is the fixture
    /// author's, which etalii.adp's validator accepts as any. Such an element is the next one of its type, in document order,
    /// whose id is not stored and that no other fixture element stands for. An id a legacy identities file beside the input
    /// stores (FBL 0.12 §8.7, the array of <c>{id, kind, key}</c>) is never aliased, so a stored id the library fails to read still fails.
    /// </summary>
    private static Dictionary<string, string> Aliases(JsonElement root, FblModel? model, string folder)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        if (model is null || !root.TryGetProperty("read", out var read) || !read.TryGetProperty("elements", out var elements)) return aliases;
        var stored = StoredIds(folder);
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in elements.EnumerateArray())
        {
            var id = element.GetProperty("id").GetString()!;
            if (model.Find(id) is not null || stored.Contains(id)) continue;
            var type = element.GetProperty("type").GetString()!;
            var stands = model.Elements.FirstOrDefault(candidate => candidate.Type == type && !candidate.IdIsStored && !claimed.Contains(candidate.Id) && elements.EnumerateArray().All(other => other.GetProperty("id").GetString() != candidate.Id));
            if (stands is null) continue;
            claimed.Add(stands.Id);
            aliases[id] = stands.Id;
        }
        return aliases;
    }

    /// <summary>The ids the legacy identities files beside a fixture's input store.</summary>
    private static HashSet<string> StoredIds(string folder)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(folder, "*.identities.json"))
        {
            using var identities = JsonDocument.Parse(File.ReadAllBytes(file));
            if (identities.RootElement.ValueKind != JsonValueKind.Array) continue;
            foreach (var entry in identities.RootElement.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) ids.Add(id.GetString()!);
            }
        }
        return ids;
    }

    /// <summary>A fixture's edit with every element id it names that <see cref="Aliases"/> maps replaced by the read element's.</summary>
    private static JsonElement Aliased(JsonElement edit, IReadOnlyDictionary<string, string> aliases)
    {
        if (aliases.Count == 0) return edit;
        var node = JsonNode.Parse(edit.GetRawText())!;
        Replace(node, aliases);
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    private static readonly HashSet<string> NamingKeys = new(StringComparer.Ordinal) { "element", "id", "parent", "after", "before" };

    private static void Replace(JsonNode node, IReadOnlyDictionary<string, string> aliases)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array.Where(item => item is not null)) Replace(item!, aliases);
            return;
        }
        if (node is not JsonObject value) return;
        foreach ((string key, JsonNode? child) in value.ToList())
        {
            if (child is JsonValue text && NamingKeys.Contains(key) && text.TryGetValue<string>(out var id) && aliases.TryGetValue(id, out var read)) value[key] = read;
            else if (child is not null) Replace(child, aliases);
        }
    }

    /// <summary>
    /// The document's attributes (FBL 0.7 §15.3): the diagram element's, and those of every further element its tool type
    /// maps onto the diagram (<see cref="NaturalIds.DocumentParts"/>), each under the name the type map gives it.
    /// </summary>
    private static FblElement Document(FblModel model, FblElement diagram, string binding)
    {
        var parts = NaturalIds.DocumentParts(binding);
        if (parts.Count == 0) return diagram;
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var element in model.Elements.Where(element => element == diagram || (element.Type != diagram.Type && parts.ContainsKey(element.Type))).Prepend(diagram).Distinct())
        {
            var renames = parts.GetValueOrDefault(element.Type) ?? new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string attribute, object? value) in element.Attributes) attributes[renames.GetValueOrDefault(attribute) ?? attribute] = DislValue(binding, element.Type, attribute, value);
        }
        return diagram with { Attributes = attributes };
    }

    /// <summary>An element with its attributes as DISL types them (<see cref="DislValue"/>).</summary>
    private static FblElement Typed(FblElement element, string binding) =>
        element with { Attributes = element.Attributes.ToDictionary(pair => pair.Key, pair => DislValue(binding, element.Type, pair.Key, pair.Value), StringComparer.Ordinal) };

    /// <summary>
    /// A value as DISL types it: the grammar family reads a slot as its text, as the lines and blocks families do, and "a text
    /// always reads" (FBL 0.5 §5.2), so a value the tool type types <c>int</c>, <c>number</c> or <c>bool</c> is converted by
    /// DISL, not FBL; the C4 types' and the Wardley map's are converted here. Any other value is as it is read.
    /// </summary>
    private static object? DislValue(string binding, string type, string attribute, object? value)
    {
        var typed = binding switch
        {
            "workspace" => C4Types.Of(type, attribute),
            "map" => WardleyTypes.Of(type, attribute),
            _ => null,
        };
        if (value is IEnumerable<object?> list && list.ToList() is var items && items.All(item => item is IReadOnlyDictionary<string, object?>))
        {
            return items.Cast<IReadOnlyDictionary<string, object?>>().Select(object? (item) => item.ToDictionary(pair => pair.Key, pair => DislValue(binding, $"{type}.{attribute}", pair.Key, pair.Value), StringComparer.Ordinal)).ToList();
        }
        if (value is not string text) return value;
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        return typed switch
        {
            "int" when long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, invariant, out var whole) => whole,
            "number" when double.TryParse(text, System.Globalization.NumberStyles.Float, invariant, out var number) => number,
            "bool" when text is "true" or "false" => text == "true",
            _ => value,
        };
    }

    /// <summary>
    /// Each attribute a fixture's element names has that value and that JSON type, a string for a
    /// text (FBL 0.5 section 15.3); an attribute it does not name is not compared, and one it names null is absent.
    /// </summary>
    private static void AssertAttributes(FblElement element, JsonElement attributes, string name)
    {
        foreach (var attribute in attributes.EnumerateObject())
        {
            var label = $"{name}: '{attribute.Name}' of '{element.Id}'";
            var expected = attribute.Value;
            var read = element.Attributes.TryGetValue(attribute.Name, out var actual);
            // A list that holds no item is absent, so that its first item is written as an insertion is (FBL 0.7 §4.6), and the fixture states it as [].
            if (!read && expected is { ValueKind: JsonValueKind.Array } && expected.GetArrayLength() == 0) continue;
            Assert.True(read || expected.ValueKind == JsonValueKind.Null, $"{label} was not read.");
            Assert.True(Same(actual, expected), $"{label} is {Describe(actual)}, where the fixture reads {expected.GetRawText()}.");
        }
    }

    /// <summary>Whether a read value is the fixture's: of its JSON type, and a list item by item, as a <c>many</c> attribute reads.</summary>
    private static bool Same(object? actual, JsonElement expected) => expected.ValueKind switch
    {
        JsonValueKind.String => actual is string text && text == expected.GetString(),
        JsonValueKind.Number => actual is long or int or double or decimal && Convert.ToDouble(actual, System.Globalization.CultureInfo.InvariantCulture).Equals(expected.GetDouble()),
        JsonValueKind.True or JsonValueKind.False => actual is bool flag && flag == expected.GetBoolean(),
        JsonValueKind.Null => actual is null,
        JsonValueKind.Array => actual is IEnumerable<object?> list && list.ToList() is var items && items.Count == expected.GetArrayLength() && items.Zip(expected.EnumerateArray()).All(pair => Same(pair.First, pair.Second)),
        JsonValueKind.Object => actual is IReadOnlyDictionary<string, object?> map && map.Count == expected.EnumerateObject().Count() && expected.EnumerateObject().All(member => map.TryGetValue(member.Name, out var item) && Same(item, member.Value)),
        _ => false,
    };

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string text => $"the text \"{text}\"",
        bool flag => $"the bool {(flag ? "true" : "false")}",
        IEnumerable<object?> list => $"the list [{string.Join(", ", list.Select(Describe))}]",
        IReadOnlyDictionary<string, object?> map => $"the map {{{string.Join(", ", map.Select(pair => $"{pair.Key}: {Describe(pair.Value)}"))}}}",
        _ => $"the {value.GetType().Name} {Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}",
    };

    /// <summary>
    /// A fixture's edit as a model change. A position (FBL 0.7 §11.2) is converted to the model change's index, which
    /// counts the siblings as they are before the change, the moved element included; an <c>after</c> or <c>before</c>
    /// is the place of that sibling, found in <paramref name="model"/> among the elements with the same parent and type.
    /// </summary>
    private static ModelChange ReadChange(JsonElement edit, FblModel? model)
    {
        if (edit.ValueKind == JsonValueKind.Array) throw new NotSupportedException("A fixture edit that is a list of changes is not run yet.");
        if (edit.TryGetProperty("setting", out var setting)) return FixtureEdits.Setting(setting);
        if (edit.TryGetProperty("move", out var move))
        {
            var id = move.GetProperty("element").GetString()!;
            var parent = move.GetProperty("parent").GetString();
            var type = model?.Find(id)?.Type;
            return new ModelChange.Move(id, parent, Index(move.GetProperty("position"), model, parent, type) ?? -1);
        }
        if (edit.TryGetProperty("save", out _)) return new ModelChange.Save();
        if (edit.TryGetProperty("set", out var set))
        {
            return set.TryGetProperty("occurrence", out _) ? throw new NotSupportedException("A fixture edit naming an occurrence is not run yet.") : new ModelChange.Set(set.GetProperty("element").GetString()!, FixtureEdits.Attributes(set.GetProperty("attributes")));
        }
        if (edit.TryGetProperty("add", out var add))
        {
            // A position after an entry of another type is no index among siblings (etalii.adp spec 074).
            if (add.TryGetProperty("position", out var placed) && placed.TryGetProperty("after", out var following)
                && model?.Find(following.GetString()!) is { } anchor && anchor.Type != add.GetProperty("type").GetString())
            {
                return new ModelChange.AddAfter(add.GetProperty("type").GetString()!, FixtureEdits.Attributes(add.GetProperty("attributes")), anchor.Id);
            }
            return new ModelChange.Add(
                add.GetProperty("type").GetString()!,
                add.TryGetProperty("id", out var id) ? id.GetString() : null,
                add.TryGetProperty("attributes", out var attributes) ? FixtureEdits.Attributes(attributes) : new Dictionary<string, object?>(),
                add.TryGetProperty("parent", out var parent) ? parent.GetString() : null,
                add.TryGetProperty("position", out var position) ? Index(position, model, add.TryGetProperty("parent", out var under) ? under.GetString() : null, add.GetProperty("type").GetString()) : null,
                add.TryGetProperty("slot", out var slot) ? slot.GetString() : null);
        }
        if (edit.TryGetProperty("remove", out var remove)) return new ModelChange.Remove(remove.GetProperty("element").GetString()!);
        return edit.TryGetProperty("place", out var place)
            ? new ModelChange.Place(place.GetProperty("element").GetString()!, place.GetProperty("x").GetDouble(), place.GetProperty("y").GetDouble())
            : throw new InvalidOperationException($"Unknown fixture edit: {edit}.");
    }

    /// <summary>The model change's index for a fixture's position among <paramref name="parent"/>'s children of <paramref name="type"/>.</summary>
    private static int? Index(JsonElement position, FblModel? model, string? parent, string? type)
    {
        if (position.TryGetProperty("index", out var index)) return index.GetInt32();
        var siblings = model?.Elements.Where(element => element.ParentId == parent && element.Type == type).Select(element => element.Id).ToList() ?? [];
        if (position.TryGetProperty("after", out var after)) return Place(after.GetString()!) + 1;
        return position.TryGetProperty("before", out var before)
            ? Place(before.GetString()!)
            : throw new InvalidOperationException($"Unknown fixture position: {position}.");

        int Place(string sibling) =>
            siblings.IndexOf(sibling) is var at and >= 0 ? at : throw new InvalidOperationException($"The position names '{sibling}', which is no sibling here.");
    }

    /// <summary>
    /// A fixture's subject: a body read through its binding, or a registration (an <c>.adp</c> input). A registration whose
    /// body is beside it opens that body as the reading of its origin, selecting its <c>resource</c> (FBL 0.10 §8.2, §15.3):
    /// a change of the model is that body's, and a placement or a setting the registration's.
    /// </summary>
    private sealed class Subject
    {
        private OpenBody? _body;
        private OpenRegistration? _registration;
        private FblBinding? _binding;

        /// <summary>The path of the body a registration opens, as a step's splices name it in <c>file</c>; null for a body input or a registration alone.</summary>
        private string? _bodyName;

        /// <summary>The input's folder, which the names of <c>expectFiles</c> are relative to.</summary>
        private string _folder = "";

        /// <summary>
        /// The legacy layout the registration's positions are kept in (FBL §8.7): the binding's <c>legacyLayout</c> beside the
        /// body, when the registration has no <c>layout:</c> block and the file exists; null otherwise.
        /// </summary>
        private LegacySidecar? _sidecar;

        /// <summary>The path of the legacy layout, as a step's splices name it in <c>file</c>.</summary>
        private string? _sidecarName;

        /// <summary>
        /// The legacy identities the body's sidecar ids are read from (FBL 0.12 §8.7): the binding's <c>legacyIdentities</c>
        /// beside the body, when the registration has no <c>identities:</c> block and the file exists; null otherwise.
        /// </summary>
        private LegacySidecar? _identities;

        /// <summary>The path of the legacy identities, as <c>expectFiles</c> names it.</summary>
        private string? _identitiesName;

        /// <summary>Which file each edit that can be undone wrote, and each that can be redone.</summary>
        private readonly Stack<Written> _done = new();
        private readonly Stack<Written> _undone = new();

        /// <summary>The files a step can write: the input, the body a registration opens, and the legacy layout it keeps positions in.</summary>
        private enum Written
        {
            Input,
            Body,
            Sidecar,
        }

        /// <summary>A registration's reading: the elements of its body and the findings of both (FBL §8.1, §8.5).</summary>
        private FblModel? Read { get; set; }

        public static Subject Open(byte[] input, string name, FblBinding binding, string folder, string? origin = null) =>
            name.EndsWith(".adp", StringComparison.OrdinalIgnoreCase)
                ? Registration(input, name, binding, folder)
                : new Subject { _binding = binding, _folder = folder, _body = OpenBody.Open(input, binding, Options(binding, name, origin, null, null)) };

        /// <summary>The options a fixture's body is read with: the ids, times and types of its tool type, which a binding does not carry, and its reading.</summary>
        private static FblOptions Options(FblBinding binding, string name, string? origin, string? resource, string? view, IReadOnlyDictionary<string, string>? identities = null) => new()
        {
            Identities = identities ?? new Dictionary<string, string>(StringComparer.Ordinal),
            FileName = name,
            DeriveId = NaturalIds.For(binding.Name),
            TimeAttributes = NaturalIds.TimeAttributes(binding.Name),
            AttributeType = AttributeTypes(binding.Name),
            Many = NaturalIds.Many(binding.Name),
            Origin = origin,
            Resource = resource,
            View = view,
        };

        /// <summary>
        /// A registration opened through its binding (FBL 0.7 §8.1). When its body is beside it, the body is read too, as
        /// the document the registration opens, the reading of the registration's origin that selects its resource (FBL
        /// 0.10 §8.2): its elements are the reading's, and the ids the registration's stale entries are told by (FBL §8.5);
        /// the findings are the body's and the registration's, a resource the body does not hold among them.
        /// </summary>
        private static Subject Registration(byte[] input, string name, FblBinding binding, string folder)
        {
            var registration = OpenRegistration.Open(input, binding);
            var subject = new Subject { _registration = registration, _binding = binding, _folder = folder };
            // Without a body header, the body is the sibling of the registration's base name (FBL §8.2).
            var bodyName = registration.Document.Body
                ?? (BodyLocator.Locate(Path.Combine(folder, name), registration.Document, binding, folder) is { Exists: true, Path: { } located } ? Path.GetFileName(located) : null);
            if (bodyName is null || !File.Exists(Path.Combine(folder, bodyName))) return subject;
            var origin = binding.Claims.Origins.Contains(registration.Document.Origin) ? registration.Document.Origin : null;
            // The ids the registration keeps (FBL §8.6), or, without its identities block, the legacy identities file beside the body (FBL 0.12 §8.7).
            var identities = registration.Document.IdentityMap();
            if (binding.ForOrigin(origin).Registration.LegacyIdentities is { } legacy && registration.Document.Identities is null
                && LegacySidecar.PathFor(legacy, Path.Combine(folder, bodyName)) is var identitiesPath && File.Exists(identitiesPath))
            {
                subject._identities = LegacySidecar.Open(File.ReadAllBytes(identitiesPath));
                subject._identitiesName = Path.GetRelativePath(folder, identitiesPath).Replace('\\', '/');
                identities = subject._identities.Identities();
            }
            var body = OpenBody.Open(File.ReadAllBytes(Path.Combine(folder, bodyName)), binding, Options(binding, bodyName, origin, registration.Document.Resource, registration.Document.View, identities));
            registration.KnownIds = body.Model.Elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
            registration.ShowsNoView = body.Model.ShowsNoView(registration.Document.View);
            if (binding.ForOrigin(origin).Registration.ResourceCapture is not null) registration.KnownResources = body.Model.Resources;
            subject._body = body;
            subject._bodyName = bodyName;
            var findings = new List<Finding>([.. body.Model.Findings, .. registration.Findings(name)]);
            if (binding.ForOrigin(origin).Registration.LegacyLayout is { } pattern && registration.Document.Layout is null
                && LegacySidecar.PathFor(pattern, Path.Combine(folder, bodyName)) is var sidecarPath && File.Exists(sidecarPath))
            {
                subject._sidecar = LegacySidecar.Open(File.ReadAllBytes(sidecarPath));
                subject._sidecarName = Path.GetRelativePath(folder, sidecarPath).Replace('\\', '/');
                findings.AddRange(subject._sidecar.Findings(body.Model.SelectedView, subject.Ids, subject._sidecarName));
            }
            if (subject._identities is not null)
            {
                var kinds = binding.AllRules.Select(rule => rule.Id?.SidecarKind).OfType<string>().ToHashSet(StringComparer.Ordinal);
                findings.AddRange(subject._identities.IdentityFindings(kinds, subject._identitiesName!));
            }
            subject.Read = body.Model with { Findings = findings };
            return subject;
        }

        /// <summary>The attribute types of a binding's tool type, which a binding does not carry (FBL 0.5 section 5.2): the timeline's, from its specification.</summary>
        private static Func<string, string, string?>? AttributeTypes(string binding) => binding switch
        {
            "timeline" => TimelineTypes.Of,
            "workspace" => C4Types.Of,
            _ => null,
        };

        /// <summary>Whether the input is a registration that opened its body, whose model edits go to that body.</summary>
        private bool OpensItsBody => _registration is not null && _body is not null;

        /// <summary>The ids of the body's reading, which a legacy layout's keys are matched with (FBL 0.11 §8.7).</summary>
        private HashSet<string> Ids => _body!.Model.Elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);

        /// <summary>The file a change writes: the body a registration opens for a model change, the legacy layout for a placement while it is kept there, else the input.</summary>
        private Written Target(ModelChange change) =>
            _registration is null ? Written.Input
            : OpensItsBody && change is not (ModelChange.Place or ModelChange.Setting or ModelChange.Identify) ? Written.Body
            : _sidecar is not null && change is ModelChange.Place && !_registration.ShowsNoView ? Written.Sidecar
            : Written.Input;

        private string? FileOf(Written written) => written switch
        {
            Written.Body => _bodyName,
            Written.Sidecar => _sidecarName,
            _ => null,
        };

        /// <summary>The input's bytes: the body, or the registration.</summary>
        public byte[] Bytes => _registration?.Bytes ?? _body!.Bytes;

        public FblModel? Model => _registration is null ? _body?.Model : Read is null ? null : _body!.Model with { Findings = Read.Findings };

        /// <summary>
        /// That every splice a step lists names the file the step writes (FBL 0.10 §15.3): the body a registration opens by
        /// its <c>file</c>, and the input by none.
        /// </summary>
        public void Expect(JsonElement step, string label)
        {
            var writes = step.TryGetProperty("undo", out var undo) && undo.GetBoolean() ? (_done.Count > 0 ? _done.Peek() : Written.Input)
                : step.TryGetProperty("redo", out var redo) && redo.GetBoolean() ? (_undone.Count > 0 ? _undone.Peek() : Written.Input)
                : step.TryGetProperty("edit", out var edit) ? Target(ReadChange(edit, Model)) : Written.Input;
            var expected = FileOf(writes);
            foreach (var splice in step.GetProperty("splices").EnumerateArray())
            {
                var file = splice.TryGetProperty("file", out var named) ? named.GetString() : null;
                Assert.True(file == expected, $"{label}: a splice names the file '{file}', where the step writes {(expected is null ? "the input" : $"'{expected}'")}.");
            }
        }

        /// <summary>The bytes of each file <c>expectFiles</c> names after a step: the body a registration opens, the legacy layout it keeps its positions in and the legacy identities its ids are read from are the files beside it this runner keeps.</summary>
        public void AssertFiles(JsonElement files, string label)
        {
            foreach (var file in files.EnumerateObject())
            {
                var path = Path.GetFullPath(Path.Combine(_folder, file.Name));
                var actual = _bodyName is not null && path == Path.GetFullPath(Path.Combine(_folder, _bodyName)) ? _body!.Bytes
                    : _sidecarName is not null && path == Path.GetFullPath(Path.Combine(_folder, _sidecarName)) ? _sidecar!.Bytes
                    : _identitiesName is not null && path == Path.GetFullPath(Path.Combine(_folder, _identitiesName)) ? _identities!.Bytes
                    : null;
                Assert.True(actual is not null, $"{label}: expectFiles names '{file.Name}', which is no file beside the input that it opens.");
                var expected = Encoding.UTF8.GetBytes(file.Value.GetString()!);
                Assert.True(expected.AsSpan().SequenceEqual(actual), $"{label}: '{file.Name}' differs from expectFiles.\nExpected:\n{file.Value.GetString()}\nActual:\n{Encoding.UTF8.GetString(actual)}");
            }
        }

        public PlanResult Change(ModelChange change)
        {
            var target = Target(change);
            var result = target switch
            {
                Written.Sidecar => _sidecar!.Change(sidecar => sidecar.PlanPlace(_body!.Model.SelectedView, ((ModelChange.Place)change).Id, ((ModelChange.Place)change).X, ((ModelChange.Place)change).Y, Ids)),
                Written.Input when _registration is not null => _registration.Change(change),
                _ => change is ModelChange.Remove remove && Unsets(remove.Id) is { Count: > 0 } unsets ? RemoveWith(remove, unsets) : _body!.Change(change),
            };
            if (result is PlanResult.Planned { Edit.Splices.Count: > 0 })
            {
                _done.Push(target);
                _undone.Clear();
            }
            return result;
        }

        /// <summary>
        /// The keys DISL's default deletion policy clears when an element is removed: every reference
        /// to it from an entry that does not go with it. A fixture of a tool type gives a removal as
        /// one edit with those splices in it, because to its user it is one gesture; FBL itself only
        /// cascades (section 6.2), so the runner supplies the policy as it supplies the derived ids.
        /// </summary>
        private List<ModelChange.Set> Unsets(string id)
        {
            var model = _body!.Model;
            if (!NaturalIds.UnsetsReferences(_binding!.Name) || model.Elements.FirstOrDefault(e => e.Id == id) is not { } removed) return [];
            var cascade = _binding.AllRules.First(rule => rule.Name == removed.Rule).Remove?.Cascade ?? [];
            var unsets = new List<ModelChange.Set>();
            foreach (var other in model.Elements)
            {
                if (other.Id == id || cascade.Contains(other.Rule) || other.ParentId == id) continue;
                var cleared = _binding.AllRules.First(rule => rule.Name == other.Rule).Attributes
                    .Where(attribute => attribute.Value.Reference is { } reference && reference.To.Contains(removed.Rule)
                        && other.Attributes.TryGetValue(attribute.Key, out var value) && value as string == id)
                    .ToDictionary(attribute => attribute.Key, _ => (object?)null, StringComparer.Ordinal);
                if (cleared.Count > 0) unsets.Add(new ModelChange.Set(other.Id, cleared));
            }
            return unsets;
        }

        /// <summary>Plans the unsets and the removal against the body as it is, and applies them as one edit.</summary>
        private PlanResult RemoveWith(ModelChange.Remove remove, List<ModelChange.Set> unsets)
        {
            var splices = new List<Splice>();
            foreach (var change in unsets.Cast<ModelChange>().Append(remove))
            {
                var planned = _body!.Plan(change);
                if (planned is not PlanResult.Planned { Edit: var part }) return planned;
                splices.AddRange(part.Splices);
            }
            var edit = new Edit([.. splices.OrderBy(splice => splice.Start)]);
            _body!.Apply(edit);
            return new PlanResult.Planned(edit);
        }

        public IReadOnlyList<Splice> Undo(string label)
        {
            var written = _done.Count > 0 ? _done.Pop() : Written.Input;
            _undone.Push(written);
            return Done(FileWritten(written).Undo(), label);
        }

        public IReadOnlyList<Splice> Redo(string label)
        {
            var written = _undone.Count > 0 ? _undone.Pop() : Written.Input;
            _done.Push(written);
            return Done(FileWritten(written).Redo(), label);
        }

        /// <summary>The file an edit wrote, which its undo and redo write again.</summary>
        private SplicedFile FileWritten(Written written) => written switch
        {
            Written.Sidecar => _sidecar!,
            Written.Body => _body!,
            _ => (SplicedFile?)_registration ?? _body!,
        };

        private static IReadOnlyList<Splice> Done(UndoResult result, string label)
        {
            var done = result as UndoResult.Done;
            Assert.True(done is not null, $"{label}: refused with '{(result as UndoResult.Refused)?.Reason}'.");
            return done.Splices;
        }
    }
}
