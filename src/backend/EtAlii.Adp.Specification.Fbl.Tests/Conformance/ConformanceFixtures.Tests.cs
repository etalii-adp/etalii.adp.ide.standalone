using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
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
    private const int MinimumFixtures = 23;

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
    /// points is read as naming the file of that name here. The fixture itself is never edited.
    /// </summary>
    private static string BindingReference(string reference, string folder)
    {
        var hash = reference.LastIndexOf('#');
        var file = reference[..hash];
        if (file.Length == 0 || File.Exists(Path.GetFullPath(Path.Combine(folder, file)))) return reference;
        return $"../../{Path.GetFileName(file)}{reference[hash..]}";
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
        var copy = Path.Combine(Path.GetTempPath(), $"fbl-{Guid.NewGuid():N}", "fixture.json");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.WriteAllText(copy, changed);

        try
        {
            // Act.
            var failure = Record.Exception(() => Run("timeline-written-text", folder, File.ReadAllBytes(copy)));

            // Assert: it fails, naming the attribute.
            Assert.NotNull(failure);
            Assert.Contains("'label'", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(copy)!, true);
        }
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
        var subject = Subject.Open(input, inputName, binding, folder);
        var reference = BindingReference(root.GetProperty("binding").GetString()!, folder);
        var bindingText = File.ReadAllText(Path.GetFullPath(Path.Combine(folder, reference[..reference.LastIndexOf('#')])));

        // Act and assert: the reading.
        if (root.TryGetProperty("read", out var read)) AssertRead(subject, read, name);

        // Act and assert: every step.
        var index = 0;
        foreach (var step in root.GetProperty("steps").EnumerateArray())
        {
            index++;
            var label = $"{name} step {index}";
            var before = subject.Bytes;
            var expected = FixtureEdits.Splices(step);
            IReadOnlyList<Splice> actual;
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
                var result = subject.Change(ReadChange(step.GetProperty("edit"), subject.Model));
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

            // A refused step may leave out the document it leaves, which is the one before it, as compared above.
            if (step.TryGetProperty("refused", out _) && !step.TryGetProperty("expect", out _) && !step.TryGetProperty("expectFile", out _)) continue;
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

    private static void AssertRead(Subject subject, JsonElement read, string name)
    {
        var model = subject.Model!;
        if (read.TryGetProperty("unreadable", out var unreadable)) Assert.Equal(unreadable.GetBoolean(), model.Unreadable);
        if (read.TryGetProperty("elements", out var elements))
        {
            foreach (var element in elements.EnumerateArray())
            {
                var id = element.GetProperty("id").GetString()!;
                var type = element.GetProperty("type").GetString()!;
                var found = model.Find(id);
                Assert.True(found is not null, $"{name}: no element '{id}' was read; read: {string.Join(", ", model.Elements.Select(e => $"{e.Id} ({e.Type})"))}.");
                Assert.Equal(type, found.Type);
                if (element.TryGetProperty("parent", out var parent)) Assert.True(parent.GetString() == found.ParentId, $"{name}: '{id}' is read in '{found.ParentId}', where the fixture reads '{parent.GetString()}'.");
                if (element.TryGetProperty("attributes", out var attributes)) AssertAttributes(found, attributes, name);
            }
        }
        if (read.TryGetProperty("findings", out var findings))
        {
            // A fixture that lists no finding says the reading has none: a reference that does not
            // resolve, an entry without its id, would each be one.
            if (findings.GetArrayLength() == 0)
            {
                Assert.True(model.Findings.Count == 0, $"{name}: the fixture expects no finding; read: {string.Join("; ", model.Findings.Select(f => $"{f.Code} {f.Message}"))}");
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
    /// Each attribute a fixture's element names has that value and that JSON type, a string for a
    /// text (FBL 0.5 section 15.3); an attribute it does not name is not compared.
    /// </summary>
    private static void AssertAttributes(FblElement element, JsonElement attributes, string name)
    {
        foreach (var attribute in attributes.EnumerateObject())
        {
            var label = $"{name}: '{attribute.Name}' of '{element.Id}'";
            Assert.True(element.Attributes.TryGetValue(attribute.Name, out var actual), $"{label} was not read.");
            var expected = attribute.Value;
            var same = expected.ValueKind switch
            {
                JsonValueKind.String => actual is string text && text == expected.GetString(),
                JsonValueKind.Number => actual is long or int or double or decimal && Convert.ToDouble(actual, System.Globalization.CultureInfo.InvariantCulture).Equals(expected.GetDouble()),
                JsonValueKind.True or JsonValueKind.False => actual is bool flag && flag == expected.GetBoolean(),
                JsonValueKind.Null => actual is null,
                _ => false,
            };
            Assert.True(same, $"{label} is {Describe(actual)}, where the fixture reads {expected.GetRawText()}.");
        }
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string text => $"the text \"{text}\"",
        bool flag => $"the bool {(flag ? "true" : "false")}",
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
            if (set.TryGetProperty("occurrence", out _)) throw new NotSupportedException("A fixture edit naming an occurrence is not run yet.");
            return new ModelChange.Set(set.GetProperty("element").GetString()!, FixtureEdits.Attributes(set.GetProperty("attributes")));
        }
        if (edit.TryGetProperty("add", out var add))
        {
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
        if (position.TryGetProperty("before", out var before)) return Place(before.GetString()!);
        throw new InvalidOperationException($"Unknown fixture position: {position}.");

        int Place(string sibling) =>
            siblings.IndexOf(sibling) is var at and >= 0 ? at : throw new InvalidOperationException($"The position names '{sibling}', which is no sibling here.");
    }

    /// <summary>A fixture's subject: a body read through its binding, or a registration (an <c>.adp</c> input).</summary>
    private sealed class Subject
    {
        private OpenBody? _body;
        private OpenRegistration? _registration;
        private FblBinding? _binding;

        /// <summary>A registration's reading: the elements of its body and the findings of both (FBL §8.1, §8.5).</summary>
        private FblModel? Read { get; set; }

        public static Subject Open(byte[] input, string name, FblBinding binding, string folder) =>
            name.EndsWith(".adp", StringComparison.OrdinalIgnoreCase)
                ? Registration(input, name, binding, folder)
                : new Subject { _binding = binding, _body = OpenBody.Open(input, binding, new FblOptions { FileName = name, DeriveId = NaturalIds.For(binding.Name), TimeAttributes = NaturalIds.TimeAttributes(binding.Name), AttributeType = AttributeTypes(binding.Name) }) };

        /// <summary>
        /// A registration opened through its binding (FBL 0.7 §8.1). When its body is beside it, the body is read too, as
        /// the document the registration opens: its elements are the reading's, and the ids the registration's stale
        /// entries are told by (FBL §8.5); the findings are the body's and the registration's.
        /// </summary>
        private static Subject Registration(byte[] input, string name, FblBinding binding, string folder)
        {
            var registration = OpenRegistration.Open(input, binding);
            var subject = new Subject { _registration = registration };
            if (registration.Document.Body is not { } bodyName || !File.Exists(Path.Combine(folder, bodyName))) return subject;
            var body = OpenBody.Open(File.ReadAllBytes(Path.Combine(folder, bodyName)), binding, new FblOptions { FileName = bodyName });
            registration.KnownIds = body.Model.Elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
            subject.Read = body.Model with { Findings = [.. body.Model.Findings, .. registration.Findings(name)] };
            return subject;
        }

        /// <summary>The attribute types of a binding's tool type, which a binding does not carry (FBL 0.5 section 5.2): the timeline's, from its specification.</summary>
        private static Func<string, string, string?>? AttributeTypes(string binding) => binding == "timeline" ? TimelineTypes.Of : null;

        public byte[] Bytes => _body?.Bytes ?? _registration!.Bytes;

        public FblModel? Model => _body?.Model ?? Read;

        public PlanResult Change(ModelChange change)
        {
            if (_body is null) return _registration!.Change(change);
            return change is ModelChange.Remove remove && Unsets(remove.Id) is { Count: > 0 } unsets ? RemoveWith(remove, unsets) : _body.Change(change);
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

        public IReadOnlyList<Splice> Undo(string label) => Done(_body is not null ? _body.Undo() : _registration!.Undo(), label);

        public IReadOnlyList<Splice> Redo(string label) => Done(_body is not null ? _body.Redo() : _registration!.Redo(), label);

        private static IReadOnlyList<Splice> Done(UndoResult result, string label)
        {
            var done = result as UndoResult.Done;
            Assert.True(done is not null, $"{label}: refused with '{(result as UndoResult.Refused)?.Reason}'.");
            return done.Splices;
        }
    }
}
