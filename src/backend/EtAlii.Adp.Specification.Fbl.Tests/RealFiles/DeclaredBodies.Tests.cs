using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.RealFiles;

/// <summary>
/// The vendored declared bindings on every real file they claim (Requirement 11.1 to 11.5): read
/// without throwing and every byte accounted for, saved unchanged, edited and removed touching only
/// the splices' bytes and undone exactly, and an undo refused when the file has drifted.
/// </summary>
public class DeclaredBodiesTests
{
    public static TheoryData<string> Keys() => RealFileCorpus.Keys();

    public static TheoryData<string, string> Pairs() => RealFileCorpus.Pairs();

    [Theory]
    [MemberData(nameof(Keys))]
    public void TheEnumerationFindsTheFiles(string key)
    {
        // Arrange.
        var binding = RealFileCorpus.Find(key);

        // Act.
        var files = RealFileCorpus.Files(binding);

        // Assert.
        Assert.True(files.Count >= binding.Minimum, $"{key}: {files.Count} files were found under src/; at least {binding.Minimum} were there when this suite was written.");
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TheFileReads(string key, string file)
    {
        // Arrange.
        (FblBinding binding, byte[] bytes) = Load(key, file);

        // Act.
        var reading = BodyReading.Read(bytes, binding, RealFileCorpus.Options(binding, file));

        // Assert: unreadable only where listed, with the reason the reading gives.
        Divergences.Check("unreadable", key, file, reading.Unreadable is { } problem ? $"offset {problem.Offset}: {problem.Message}" : null);
        if (reading.Unreadable is not null) return;
        var gaps = reading.Family.Unaccounted();
        Assert.True(gaps.Count == 0, $"{file}: bytes no node of the reading owns: {string.Join(", ", gaps.Select(g => $"{g} '{reading.Text.Text(g)}'"))}");
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void ASaveWithoutAnEditWritesTheBytesThatWereRead(string key, string file)
    {
        // Arrange.
        (FblBinding binding, byte[] bytes) = Load(key, file);
        var body = OpenBody.Open(bytes, binding, RealFileCorpus.Options(binding, file));

        // Act.
        var result = body.Change(new ModelChange.Save());

        // Assert.
        if (body.IsReadOnly)
        {
            Assert.IsType<PlanResult.Refused>(result);
        }
        else
        {
            Assert.Empty(Assert.IsType<PlanResult.Planned>(result).Edit.Splices);
        }
        Assert.Equal(bytes, body.Bytes);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void AnEditChangesOnlyItsSplicesAndItsUndoRestoresTheFile(string key, string file)
    {
        // Arrange.
        (FblBinding binding, byte[] bytes) = Load(key, file);
        var body = OpenBody.Open(bytes, binding, RealFileCorpus.Options(binding, file));
        if (body.IsReadOnly || FirstWritable(body.Reading) is not { } target) return;

        // Act.
        var result = body.Change(new ModelChange.Set(target.Element.Id, new Dictionary<string, object?> { [target.Attribute] = target.Value + " edited" }));

        // Assert.
        Divergences.Check("edit", key, file, result is PlanResult.Refused refused ? $"{target.Element.Id}.{target.Attribute}: {refused.Reason}" : null);
        if (result is not PlanResult.Planned planned) return;
        AssertOnlySplicesChanged(bytes, body.Bytes, planned.Edit.Splices, file);
        Assert.IsType<UndoResult.Done>(body.Undo());
        Assert.Equal(bytes, body.Bytes);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void ARemovalChangesOnlyItsSplicesAndItsUndoRestoresTheFile(string key, string file)
    {
        // Arrange.
        (FblBinding binding, byte[] bytes) = Load(key, file);
        var body = OpenBody.Open(bytes, binding, RealFileCorpus.Options(binding, file));
        if (body.IsReadOnly) return;
        var element = body.Reading.Elements.FirstOrDefault(e => e.Rule.Remove is not null && e.Rule.ReadOnly is null);
        if (element is null) return;
        var holders = body.Model.Elements.Count(e => e.Type == element.Rule.Type);
        var idsShift = body.Model.Elements.Any(e => e.Type == element.Rule.Type && !e.IdIsStored);

        // Act.
        var result = body.Change(new ModelChange.Remove(element.Id));

        // Assert.
        Divergences.Check("remove", key, file, result is PlanResult.Refused refused ? $"{element.Id}: {refused.Reason}" : null);
        if (result is not PlanResult.Planned planned) return;
        Assert.NotEmpty(planned.Edit.Splices);
        AssertOnlySplicesChanged(bytes, body.Bytes, planned.Edit.Splices, file);
        // Where an entry of the type is named by its place (no id, or one an earlier entry holds), ids
        // move up with the entries and the removed id is taken over, so there the count tells.
        if (idsShift) Assert.True(body.Model.Elements.Count(e => e.Type == element.Rule.Type) < holders);
        else Assert.DoesNotContain(body.Model.Elements, e => e.Id == element.Id && e.Type == element.Rule.Type);
        Assert.IsType<UndoResult.Done>(body.Undo());
        Assert.Equal(bytes, body.Bytes);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void AnUndoAfterTheFileChangedIsRefused(string key, string file)
    {
        // Arrange.
        (FblBinding binding, byte[] bytes) = Load(key, file);
        var body = OpenBody.Open(bytes, binding, RealFileCorpus.Options(binding, file));
        if (body.IsReadOnly || FirstWritable(body.Reading) is not { } target) return;
        if (body.Change(new ModelChange.Set(target.Element.Id, new Dictionary<string, object?> { [target.Attribute] = target.Value + " edited" })) is not PlanResult.Planned) return;
        var edited = body.Bytes;
        var drifted = edited.Concat("\n"u8.ToArray()).ToArray();

        // Act.
        var result = body.Undo(drifted);

        // Assert.
        Assert.Equal(SplicedFile.DriftUndo, Assert.IsType<UndoResult.Refused>(result).Reason);
        Assert.Equal(edited, body.Bytes);
    }

    [Fact]
    public void EveryListedDivergenceNamesAFileOfTheSuite()
    {
        // Arrange.
        var registrations = RealFileCorpus.All.Where(f => f.EndsWith(".adp", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);

        // Act and assert.
        foreach (var divergence in Divergences.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(divergence.Reason), $"The divergence on {divergence.File} gives no reason.");
            var known = RealFileCorpus.Declared.Any(b => b.Key == divergence.Binding && RealFileCorpus.Files(b).Contains(divergence.File))
                || registrations.Contains(divergence.File);
            Assert.True(known, $"divergences.json lists {divergence.File} for {divergence.Binding}, which the suite does not read.");
        }
    }

    private static (FblBinding Binding, byte[] Bytes) Load(string key, string file) =>
        (RealFileCorpus.Binding(RealFileCorpus.Find(key)), File.ReadAllBytes(RealFileCorpus.FullPath(file)));

    /// <summary>
    /// The first element's first writable attribute (Requirement 11.4): the first element in document
    /// order with an attribute that is present, writable, a string, and neither its key, a reference
    /// nor a mapped value, whose change would mean something else than an edit of text.
    /// </summary>
    private static (ReadElement Element, string Attribute, string Value)? FirstWritable(BodyReading reading)
    {
        foreach (var element in reading.Elements.Where(e => e is { IsRelation: false, Rule.ReadOnly: null }))
        {
            foreach ((string name, AttributeBinding binding) in element.Rule.Attributes)
            {
                if (binding.IsComputed || binding.Parent is not null || binding.Reference is not null || binding.Map is not null || binding.Flag) continue;
                if (name == element.KeyAttribute) continue;
                if (element.Rule.Id?.From is { } from && from.Key == binding.Key && from.XmlAttribute == binding.XmlAttribute && from.Group == binding.Group && from.Capture == binding.Capture) continue;
                if (!element.Slots.TryGetValue(name, out var read) || !read.Present || !read.Writable || read.Value is not string { Length: > 0 } value) continue;
                return (element, name, value);
            }
        }
        return null;
    }

    /// <summary>Every byte outside the splices is unchanged: the bytes between splices match, in order, before and after.</summary>
    private static void AssertOnlySplicesChanged(byte[] before, byte[] after, IReadOnlyList<Splice> splices, string file)
    {
        var position = 0;
        var shift = 0;
        foreach (var splice in splices)
        {
            var kept = before.AsSpan(position, splice.Start - position);
            Assert.True(kept.SequenceEqual(after.AsSpan(position + shift, kept.Length)), $"{file}: bytes {position}..{splice.Start} changed outside the edit's splices.");
            var text = System.Text.Encoding.UTF8.GetByteCount(splice.Text);
            shift += text - (splice.End - splice.Start);
            position = splice.End;
        }
        var tail = before.AsSpan(position);
        Assert.True(after.Length == before.Length + shift && tail.SequenceEqual(after.AsSpan(position + shift)), $"{file}: bytes after {position} changed outside the edit's splices.");
    }
}
