using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Conformance;

/// <summary>
/// Every round-trip fixture vendored from etalii-adp/etalii.adp, run through the library as FBL
/// §15.3 says a host passes one: reading the input gives what <c>read</c> lists, and every step
/// produces exactly its splices and its document, or its refusal with nothing written.
/// </summary>
public class ConformanceFixturesTests
{
    /// <summary>The number of fixtures vendored when this suite was written: fewer means the enumeration broke.</summary>
    private const int MinimumFixtures = 11;

    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (var folder in Directory.GetDirectories(Path.Combine(Repository.Conformance, "fixtures")).Order(StringComparer.Ordinal))
        {
            if (File.Exists(Path.Combine(folder, "fixture.json"))) data.Add(Path.GetFileName(folder));
        }
        return data;
    }

    [Fact]
    public void TheFixturesAreFound()
    {
        Assert.True(Fixtures().Count >= MinimumFixtures, $"Only {Fixtures().Count} fixtures were found; {MinimumFixtures} are vendored.");
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
        // Arrange.
        var folder = Path.Combine(Repository.Conformance, "fixtures", name);
        var fixturePath = Path.Combine(folder, "fixture.json");
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        var root = fixture.RootElement;
        var binding = FblDocumentLoader.ResolveReference(BindingReference(root.GetProperty("binding").GetString()!, folder), fixturePath, out var problems);
        Assert.DoesNotContain(problems, p => p.Severity == ProblemSeverity.Error);
        var inputName = root.GetProperty("input").GetString()!;
        var input = File.ReadAllBytes(Path.Combine(folder, inputName));
        var subject = Subject.Open(input, inputName, binding);
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
            // In body order, those at one offset kept as listed: FBL gives an order only to splices at
            // the same offset (section 6.5), and a fixture written since 0.3 lists a cascaded removal
            // bottom-up, as it is planned, where this library lists every edit in body order. The
            // two say the same edit.
            var expected = step.GetProperty("splices").EnumerateArray().Select(ReadSplice)
                .Select((splice, at) => (splice, at)).OrderBy(listed => listed.splice.Start).ThenBy(listed => listed.at).Select(listed => listed.splice).ToList();
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
                var result = subject.Change(ReadChange(step.GetProperty("edit")));
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
        var folder = Path.Combine(Repository.Conformance, "fixtures", name);
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

    private static Splice ReadSplice(JsonElement splice) => new(
        Splice.Parse(splice.GetProperty("operation").GetString()!),
        splice.GetProperty("start").GetInt32(),
        splice.GetProperty("end").GetInt32(),
        splice.GetProperty("text").GetString()!);

    private static ModelChange ReadChange(JsonElement edit)
    {
        if (edit.TryGetProperty("save", out _)) return new ModelChange.Save();
        if (edit.TryGetProperty("set", out var set))
        {
            return new ModelChange.Set(set.GetProperty("element").GetString()!, Attributes(set.GetProperty("attributes")));
        }
        if (edit.TryGetProperty("add", out var add))
        {
            return new ModelChange.Add(
                add.GetProperty("type").GetString()!,
                add.TryGetProperty("id", out var id) ? id.GetString() : null,
                add.TryGetProperty("attributes", out var attributes) ? Attributes(attributes) : new Dictionary<string, object?>(),
                add.TryGetProperty("parent", out var parent) ? parent.GetString() : null);
        }
        if (edit.TryGetProperty("remove", out var remove)) return new ModelChange.Remove(remove.GetProperty("element").GetString()!);
        return edit.TryGetProperty("place", out var place)
            ? new ModelChange.Place(place.GetProperty("element").GetString()!, place.GetProperty("x").GetDouble(), place.GetProperty("y").GetDouble())
            : throw new InvalidOperationException($"Unknown fixture edit: {edit}.");
    }

    private static Dictionary<string, object?> Attributes(JsonElement attributes) =>
        attributes.EnumerateObject().ToDictionary(p => p.Name, p => Value(p.Value), StringComparer.Ordinal);

    private static object? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => value.EnumerateArray().Select(Value).ToList(),
        _ => null,
    };

    /// <summary>A fixture's subject: a body read through its binding, or a registration (an <c>.adp</c> input).</summary>
    private sealed class Subject
    {
        private OpenBody? _body;
        private OpenRegistration? _registration;
        private FblBinding? _binding;

        public static Subject Open(byte[] input, string name, FblBinding binding) =>
            name.EndsWith(".adp", StringComparison.OrdinalIgnoreCase)
                ? new Subject { _registration = OpenRegistration.Open(input) }
                : new Subject { _binding = binding, _body = OpenBody.Open(input, binding, new FblOptions { FileName = name, DeriveId = NaturalIds.For(binding.Name), TimeAttributes = NaturalIds.TimeAttributes(binding.Name) }) };

        public byte[] Bytes => _body?.Bytes ?? _registration!.Bytes;

        public FblModel? Model => _body?.Model;

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
