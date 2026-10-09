using System.Text;
using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Conformance;

/// <summary>
/// The edit steps of the Knowledge designer's vendored fixtures (knowledge-designer task 8,
/// Requirement 2.7): a step passes when it plans exactly the splices the fixture lists and leaves
/// exactly the document it expects, and an undo when it returns the bytes the undone edit started
/// from.
/// </summary>
/// <remarks>
/// <b>This is a ratchet, because the library does not pass every step yet.</b> Each fixture runs
/// until its first step that fails, and the number of steps passed must be exactly the number
/// recorded in <see cref="StepsThatPass"/>. Fewer is a regression. More means the library caught
/// up: raise the number in the same change, so the record never understates what works. A fixture
/// is done when its number is its step count, and the ratchet goes when all of them are.
/// <para>
/// <b>One kind of step is taken from the fixture rather than planned.</b> A fixture duplicates a
/// view with an add that carries <c>x-copyOf</c>, and expects one new entry holding the copy's
/// settings in the order the original has them. Nothing in FBL or in <c>knowledge.des</c> says
/// how that entry is planned: the <c>duplicateView</c> operation is a transaction of creates,
/// which under this binding would write the settings in another order. Until the definition
/// says which is meant, such a step applies the fixture's own splices, so the steps after it can
/// still be run, and the number of steps taken this way is recorded beside the number that pass.
/// A taken step proves nothing about this library.
/// </para>
/// </remarks>
public class KnowledgeFixtureStepsTests
{
    private static string FixturesRoot => Path.Combine(Repository.Conformance, "etalii.adp", "specifications", "fbl", "fixtures");

    /// <summary>
    /// How many steps of each fixture go through today, of how many, and how many of those were
    /// taken from the fixture rather than planned by the library.
    /// </summary>
    public static TheoryData<string, int, int, int> StepsThatPass => new()
    {
        { "knowledge-kept", 11, 11, 0 },
        { "knowledge-yaml", 132, 132, 1 },
        { "knowledge-json", 132, 132, 1 },
        { "knowledge-xml", 132, 132, 1 },
    };

    [Theory]
    [MemberData(nameof(StepsThatPass))]
    public void TheFixturesStepsPass_AsFarAsRecorded(string name, int passing, int of, int taken)
    {
        // Arrange.
        var folder = Path.Combine(FixturesRoot, name);
        var fixturePath = Path.Combine(folder, "fixture.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        var fixture = document.RootElement;
        var binding = FblDocumentLoader.ResolveReference(fixture.GetProperty("binding").GetString()!, fixturePath, out var problems);
        Assert.DoesNotContain(problems, problem => problem.Severity == ProblemSeverity.Error);
        var inputName = fixture.GetProperty("input").GetString()!;
        var body = OpenBody.Open(
            File.ReadAllBytes(Path.Combine(folder, inputName)),
            binding,
            new FblOptions { FileName = inputName, DeriveId = KnowledgeIds.Derive, AttributeType = KnowledgeTypes.Of });
        var steps = fixture.GetProperty("steps").EnumerateArray().ToList();

        // Act: every step in order, until the first that fails.
        var passed = 0;
        var takenFromTheFixture = 0;
        string? failure = null;
        foreach (var step in steps)
        {
            var label = $"{name} step {passed + 1} ({Describe(step)})";
            if (IsTakenFromTheFixture(step))
            {
                failure = Take(step, body, label);
                if (failure is null) takenFromTheFixture++;
            }
            else
            {
                failure = Run(step, body, label);
            }

            if (failure is not null) break;
            passed++;
        }

        // Assert: the fixture has the steps the record says, and exactly the recorded number pass.
        Assert.Equal(of, steps.Count);
        Assert.True(
            passed == passing,
            passed < passing
                ? $"{name}: only {passed} of {of} steps pass, where {passing} did. The first failure:\n{failure}"
                : $"{name}: {passed} of {of} steps pass, more than the {passing} recorded. Raise the number in StepsThatPass. The first failure now:\n{failure ?? "none"}");
        Assert.Equal(taken, takenFromTheFixture);
    }

    /// <summary>Whether a step's edit is one the definition does not say how to plan (see the class remarks).</summary>
    private static bool IsTakenFromTheFixture(JsonElement step) =>
        step.TryGetProperty("edit", out var edit) && edit.TryGetProperty("add", out var add) && add.TryGetProperty("x-copyOf", out _);

    /// <summary>Applies a step's own splices as one edit, and says what went wrong, or null when its document resulted.</summary>
    private static string? Take(JsonElement step, OpenBody body, string label)
    {
        body.Apply(new Edit(step.GetProperty("splices").EnumerateArray().Select(ReadSplice).ToList()));
        var expect = Encoding.UTF8.GetBytes(step.GetProperty("expect").GetString()!);
        return expect.AsSpan().SequenceEqual(body.Bytes) ? null : $"{label}: the fixture's own splices do not give its 'expect'.";
    }

    /// <summary>Runs one step, and says what went wrong, or null when the step passed.</summary>
    private static string? Run(JsonElement step, OpenBody body, string label)
    {
        var before = body.Bytes;

        // A fixture lists an edit's splices in the order the edit made them; the library plans
        // them in body order. Every offset is into the document before the step and no two
        // overlap, so only the order of splices at one offset means anything (FBL §6.5), and a
        // stable sort by start keeps exactly that.
        var expected = step.GetProperty("splices").EnumerateArray().Select(ReadSplice).OrderBy(splice => splice.Start).ToList();
        IReadOnlyList<Splice> actual;
        try
        {
            if (step.TryGetProperty("undo", out var undo) && undo.GetBoolean())
            {
                if (body.Undo() is not UndoResult.Done done) return $"{label}: the undo was refused.";
                actual = done.Splices;
            }
            else
            {
                var result = body.Change(ReadChange(step.GetProperty("edit"), body.Model));
                if (step.TryGetProperty("refused", out var refused))
                {
                    // Refused, and nothing written. The fixture's sentence is not compared: it is
                    // the message of the designer's own rule (knowledge.des), which this library
                    // does not evaluate, and the binding gives no reason of its own for it.
                    if (result is not PlanResult.Refused) return $"{label}: planned, where the fixture refuses with '{refused.GetString()}'.";
                    return before.AsSpan().SequenceEqual(body.Bytes) ? null : $"{label}: a refused edit changed the document.";
                }

                if (result is not PlanResult.Planned planned) return $"{label}: refused with '{(result as PlanResult.Refused)?.Reason}'.";
                actual = planned.Edit.Splices;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return $"{label}: {exception.GetType().Name}: {exception.Message}";
        }

        if (!expected.SequenceEqual(actual))
        {
            return $"{label}: expected splices\n  {string.Join("\n  ", expected)}\nbut planned\n  {string.Join("\n  ", actual)}";
        }

        // A step that names no document expects the one it started from: a save without an edit.
        var expect = step.TryGetProperty("expect", out var text) ? Encoding.UTF8.GetBytes(text.GetString()!) : before;
        return expect.AsSpan().SequenceEqual(body.Bytes)
            ? null
            : $"{label}: the document differs from 'expect'.\nExpected:\n{Encoding.UTF8.GetString(expect)}\nActual:\n{Encoding.UTF8.GetString(body.Bytes)}";
    }

    private static string Describe(JsonElement step) =>
        step.TryGetProperty("edit", out var edit) ? edit.GetRawText() : step.TryGetProperty("undo", out _) ? "undo" : "redo";

    private static Splice ReadSplice(JsonElement splice) => new(
        Splice.Parse(splice.GetProperty("operation").GetString()!),
        splice.GetProperty("start").GetInt32(),
        splice.GetProperty("end").GetInt32(),
        splice.GetProperty("text").GetString()!);

    /// <summary>
    /// A fixture's move index as <see cref="ModelChange.Move"/> counts it. A fixture counts among
    /// the siblings once the element is taken out (FBL §11.2); the library counts among them as
    /// they are before the move, the element included. The two differ by one for a move further on.
    /// </summary>
    private static int IndexBeforeTheMove(FblModel model, string id, int indexAfterRemoval)
    {
        var element = model.Find(id) ?? throw new InvalidOperationException($"The fixture moves '{id}', which the model does not have.");
        var siblings = model.Elements
            .Where(other => other.ParentId == element.ParentId && other.ParentSlot == element.ParentSlot && (element.ParentId is not null || other.Type == element.Type))
            .ToList();
        var current = siblings.FindIndex(other => other.Id == id);
        return indexAfterRemoval >= current ? indexAfterRemoval + 1 : indexAfterRemoval;
    }

    private static ModelChange ReadChange(JsonElement edit, FblModel model)
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
                add.TryGetProperty("parent", out var parent) ? parent.GetString() : null,
                add.TryGetProperty("position", out var position) ? position.GetProperty("index").GetInt32() : null,
                add.TryGetProperty("x-slot", out var slot) ? slot.GetString() : null);
        }

        if (edit.TryGetProperty("remove", out var remove)) return new ModelChange.Remove(remove.GetProperty("element").GetString()!);
        if (edit.TryGetProperty("move", out var move))
        {
            var moved = move.GetProperty("element").GetString()!;
            return new ModelChange.Move(
                moved,
                move.GetProperty("parent").ValueKind == JsonValueKind.Null ? null : move.GetProperty("parent").GetString(),
                IndexBeforeTheMove(model, moved, move.GetProperty("position").GetProperty("index").GetInt32()));
        }

        throw new InvalidOperationException($"Unknown fixture edit: {edit}.");
    }

    private static Dictionary<string, object?> Attributes(JsonElement attributes) =>
        attributes.EnumerateObject().ToDictionary(property => property.Name, property => Value(property.Value), StringComparer.Ordinal);

    private static object? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var whole) ? whole : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => value.EnumerateArray().Select(Value).ToList(),
        _ => null,
    };
}
