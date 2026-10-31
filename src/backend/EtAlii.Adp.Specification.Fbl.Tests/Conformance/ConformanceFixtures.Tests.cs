using System.Text;
using System.Text.Json;
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
    private const int MinimumFixtures = 47;

    /// <summary>
    /// The fixtures of FBL 0.9's grammar family and of FBL 0.13's split slot (etalii.adp spec 072), vendored before the
    /// library reads the family and held out of the theories until it does. Each task of the specification takes its
    /// fixtures out of this set; <see cref="TheGrammarFixturesAreRefusedForTheirFamily"/> holds what is left.
    /// </summary>
    private static readonly HashSet<string> Pending = new(StringComparer.Ordinal) { "grammar-split-slot", "w3c-turtle-grammar", "w3c-turtle-grammar-partial" };

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
            if (Pending.Contains(name)) continue;
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
    /// Requirement 1.1 of etalii.adp spec 072: the gap is seen before it is closed. Every fixture still held out of the
    /// theories is a fixture whose binding this library refuses for its family, <c>grammar</c>, which it does not read
    /// yet; a pending fixture that loads, or that is refused for no reason of its family, fails here.
    /// </summary>
    [Fact]
    public void TheGrammarFixturesAreRefusedForTheirFamily()
    {
        foreach (var name in Pending)
        {
            // Arrange.
            var folder = Path.Combine(Repository.Conformance, "fixtures", name);
            using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "fixture.json")));
            var reference = BindingReference(fixture.RootElement.GetProperty("binding").GetString()!, folder);
            var path = Path.GetFullPath(Path.Combine(folder, reference[..reference.LastIndexOf('#')]));

            // Act.
            var problems = FblDocumentLoader.Load(path, out var document);

            // Assert.
            Assert.Null(document);
            Assert.Contains(problems, p => p is { Severity: LoadSeverity.Error, Message: "A declared file body names its family." } && p.Pointer.EndsWith("/body/family", StringComparison.Ordinal));
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
        if (root.TryGetProperty("read", out var read)) AssertRead(subject, read, name, binding);

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

    private static void AssertRead(Subject subject, JsonElement read, string name, FblBinding binding)
    {
        var model = subject.Model!;
        var documentType = NaturalIds.DocumentType(binding.Name);
        if (read.TryGetProperty("unreadable", out var unreadable)) Assert.Equal(unreadable.GetBoolean(), model.Unreadable);
        if (documentType is not null && read.TryGetProperty("document", out var document))
        {
            // FBL 0.7 §15.3: the attributes of the document, the element of the type the tool type maps as the diagram.
            var diagram = model.Elements.FirstOrDefault(element => element.Type == documentType);
            Assert.True(diagram is not null, $"{name}: no {documentType} was read as the document.");
            if (document.TryGetProperty("attributes", out var attributes)) AssertAttributes(diagram, attributes, name);
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

        /// <summary>Which file each edit that can be undone wrote, and each that can be redone: true for the body a registration opens.</summary>
        private readonly Stack<bool> _done = new();
        private readonly Stack<bool> _undone = new();

        /// <summary>A registration's reading: the elements of its body and the findings of both (FBL §8.1, §8.5).</summary>
        private FblModel? Read { get; set; }

        public static Subject Open(byte[] input, string name, FblBinding binding, string folder, string? origin = null) =>
            name.EndsWith(".adp", StringComparison.OrdinalIgnoreCase)
                ? Registration(input, name, binding, folder)
                : new Subject { _binding = binding, _folder = folder, _body = OpenBody.Open(input, binding, Options(binding, name, origin, null)) };

        /// <summary>The options a fixture's body is read with: the ids, times and types of its tool type, which a binding does not carry, and its reading.</summary>
        private static FblOptions Options(FblBinding binding, string name, string? origin, string? resource) => new()
        {
            FileName = name,
            DeriveId = NaturalIds.For(binding.Name),
            TimeAttributes = NaturalIds.TimeAttributes(binding.Name),
            AttributeType = AttributeTypes(binding.Name),
            Origin = origin,
            Resource = resource,
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
            if (registration.Document.Body is not { } bodyName || !File.Exists(Path.Combine(folder, bodyName))) return subject;
            var origin = binding.Claims.Origins.Contains(registration.Document.Origin) ? registration.Document.Origin : null;
            var body = OpenBody.Open(File.ReadAllBytes(Path.Combine(folder, bodyName)), binding, Options(binding, bodyName, origin, registration.Document.Resource));
            registration.KnownIds = body.Model.Elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
            if (binding.ForOrigin(origin).Registration.ResourceCapture is not null) registration.KnownResources = body.Model.Resources;
            subject._body = body;
            subject._bodyName = bodyName;
            subject.Read = body.Model with { Findings = [.. body.Model.Findings, .. registration.Findings(name)] };
            return subject;
        }

        /// <summary>The attribute types of a binding's tool type, which a binding does not carry (FBL 0.5 section 5.2): the timeline's, from its specification.</summary>
        private static Func<string, string, string?>? AttributeTypes(string binding) => binding == "timeline" ? TimelineTypes.Of : null;

        /// <summary>Whether the input is a registration that opened its body, whose model edits go to that body.</summary>
        private bool OpensItsBody => _registration is not null && _body is not null;

        /// <summary>The input's bytes: the body, or the registration.</summary>
        public byte[] Bytes => _registration?.Bytes ?? _body!.Bytes;

        public FblModel? Model => _registration is null ? _body?.Model : Read is null ? null : _body!.Model with { Findings = Read.Findings };

        /// <summary>
        /// That every splice a step lists names the file the step writes (FBL 0.10 §15.3): the body a registration opens by
        /// its <c>file</c>, and the input by none.
        /// </summary>
        public void Expect(JsonElement step, string label)
        {
            var writesBody = step.TryGetProperty("undo", out var undo) && undo.GetBoolean() ? _done.Count > 0 && _done.Peek()
                : step.TryGetProperty("redo", out var redo) && redo.GetBoolean() ? _undone.Count > 0 && _undone.Peek()
                : OpensItsBody && step.TryGetProperty("edit", out var edit) && IsBodyEdit(edit);
            foreach (var splice in step.GetProperty("splices").EnumerateArray())
            {
                var file = splice.TryGetProperty("file", out var named) ? named.GetString() : null;
                Assert.True(writesBody ? file == _bodyName : file is null, $"{label}: a splice names the file '{file}', where the step writes {(writesBody ? $"'{_bodyName}'" : "the input")}.");
            }
        }

        /// <summary>The bytes of each file <c>expectFiles</c> names after a step: the body a registration opens is the one file beside it this runner writes.</summary>
        public void AssertFiles(JsonElement files, string label)
        {
            foreach (var file in files.EnumerateObject())
            {
                Assert.True(_bodyName is not null && Path.GetFullPath(Path.Combine(_folder, file.Name)) == Path.GetFullPath(Path.Combine(_folder, _bodyName)), $"{label}: expectFiles names '{file.Name}', which is not the body the input opens.");
                var expected = Encoding.UTF8.GetBytes(file.Value.GetString()!);
                Assert.True(expected.AsSpan().SequenceEqual(_body!.Bytes), $"{label}: '{file.Name}' differs from expectFiles.\nExpected:\n{file.Value.GetString()}\nActual:\n{Encoding.UTF8.GetString(_body.Bytes)}");
            }
        }

        /// <summary>A change of the model, which a registration that opened its body hands that body; a placement and a setting are the registration's.</summary>
        private static bool IsBodyEdit(JsonElement edit) => !edit.TryGetProperty("place", out _) && !edit.TryGetProperty("setting", out _);

        public PlanResult Change(ModelChange change)
        {
            var toBody = _registration is null || (OpensItsBody && change is not (ModelChange.Place or ModelChange.Setting or ModelChange.Identify));
            var result = !toBody ? _registration!.Change(change)
                : change is ModelChange.Remove remove && Unsets(remove.Id) is { Count: > 0 } unsets ? RemoveWith(remove, unsets) : _body!.Change(change);
            if (result is PlanResult.Planned { Edit.Splices.Count: > 0 })
            {
                _done.Push(toBody && _registration is not null);
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
            var toBody = _done.Count > 0 ? _done.Pop() : _registration is null;
            _undone.Push(toBody);
            return Done(toBody || _registration is null ? _body!.Undo() : _registration.Undo(), label);
        }

        public IReadOnlyList<Splice> Redo(string label)
        {
            var toBody = _undone.Count > 0 ? _undone.Pop() : _registration is null;
            _done.Push(toBody);
            return Done(toBody || _registration is null ? _body!.Redo() : _registration.Redo(), label);
        }

        private static IReadOnlyList<Splice> Done(UndoResult result, string label)
        {
            var done = result as UndoResult.Done;
            Assert.True(done is not null, $"{label}: refused with '{(result as UndoResult.Refused)?.Reason}'.");
            return done.Splices;
        }
    }
}
