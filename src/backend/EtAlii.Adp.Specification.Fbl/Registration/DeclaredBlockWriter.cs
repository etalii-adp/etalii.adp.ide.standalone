using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Specification.Fbl.Registration;

/// <summary>
/// Writes a setting into a block of the registration that the binding declares (FBL 0.7 §8.1, §8.4): an entry
/// <c>&lt;key&gt;: &lt;value&gt;</c>, indented by two spaces, with the registration's line ending; the block created when it is
/// missing, after the nearest block before it in the order <c>layout:</c>, <c>identities:</c>, then the declared blocks;
/// and the entries of a block keyed by ids that name no element pruned in the same edit (FBL §8.5).
/// </summary>
internal static class DeclaredBlockWriter
{
    public static List<Splice> Plan(RegistrationDocument document, IReadOnlyList<RegistrationBlockDeclaration> declarations, RegistrationBlockDeclaration declaration, ModelChange.Setting setting, IReadOnlySet<string>? knownIds)
    {
        var name = declaration.Name;
        var block = document.Block(name);
        var stale = block is null || !declaration.KeyedByIds || knownIds is null
            ? []
            : block.Entries.Where(entry => entry.Key != setting.Key && !knownIds.Contains(entry.Key)).ToList();
        var splices = stale.Select(entry => new Splice(SpliceOperation.RemoveEntry, entry.Line.Start, entry.Line.End, "")).ToList();
        var kept = block?.Entries.Except(stale).ToList() ?? [];
        var existing = kept.FirstOrDefault(entry => entry.Key == setting.Key);
        if (setting.Position is not null && !declaration.Kept)
        {
            Planning.Plan.Refuse($"The entries of '{name}' stand in the order of their keys, so none is moved.");
        }

        if (setting.RemoveEntry)
        {
            if (existing is null) Planning.Plan.Refuse($"The block '{name}' has no entry '{setting.Key}'.");
            if (kept.Count == 1) splices.Add(new Splice(SpliceOperation.RemoveContainer, block!.NameLine.Start, block.NameLine.End, ""));
            splices.Add(new Splice(SpliceOperation.RemoveEntry, existing!.Line.Start, existing.Line.End, ""));
            return Ordered(splices);
        }
        if (setting.Value is { } value) CheckValue(value);
        var text = document.Text;

        if (existing is not null)
        {
            if (setting.Rename is { } rename)
            {
                CheckKey(rename);
                if (rename != existing.Key && kept.Any(entry => entry.Key == rename)) Planning.Plan.Refuse($"The block '{name}' has an entry '{rename}' already.");
                if (rename != existing.Key) splices.Add(new Splice(SpliceOperation.ReplaceValue, existing.KeySpan.Start, existing.KeySpan.End, rename));
            }
            if (setting.Position is { } position)
            {
                var others = kept.Where(entry => entry != existing).ToList();
                var at = Place(others, position, block!);
                var line = text.Text(existing.Line);
                if (setting.Value is { } moved && moved != existing.Value)
                {
                    var start = existing.ValueSpan.Start - existing.Line.Start;
                    var bytes = Encoding.UTF8.GetBytes(line);
                    line = Encoding.UTF8.GetString(bytes, 0, start) + moved + Encoding.UTF8.GetString(bytes, existing.ValueSpan.End - existing.Line.Start, bytes.Length - (existing.ValueSpan.End - existing.Line.Start));
                }
                splices.Add(new Splice(SpliceOperation.RemoveEntry, existing.Line.Start, existing.Line.End, ""));
                splices.Add(new Splice(SpliceOperation.InsertEntry, at, at, line));
            }
            else if (setting.Value is { } changed && changed != existing.Value)
            {
                splices.Add(new Splice(SpliceOperation.ReplaceValue, existing.ValueSpan.Start, existing.ValueSpan.End, changed));
            }
            return Ordered(splices);
        }

        CheckKey(setting.Key);
        if (setting.Rename is not null) Planning.Plan.Refuse($"The block '{name}' has no entry '{setting.Key}' to rename.");
        if (setting.Value is not { } added)
        {
            Planning.Plan.Refuse($"A new entry of '{name}' needs a value.");
            return splices;
        }
        var indent = new string(' ', kept.FirstOrDefault()?.Indent ?? 2);
        if (block is null)
        {
            var offset = CreateAt(document, declarations, name);
            var newline = text.NewlineAt(Math.Max(0, offset - 1), "\n");
            if (Endless(text, offset))
            {
                splices.Add(new Splice(SpliceOperation.EnsureContainer, offset, offset, newline + name + ":"));
                splices.Add(new Splice(SpliceOperation.InsertEntry, offset, offset, $"{newline}{indent}{setting.Key}: {added}"));
            }
            else
            {
                splices.Add(new Splice(SpliceOperation.EnsureContainer, offset, offset, name + ":" + newline));
                splices.Add(new Splice(SpliceOperation.InsertEntry, offset, offset, $"{indent}{setting.Key}: {added}{newline}"));
            }
            return splices;
        }
        var place = declaration.Kept
            ? Place(kept, setting.Position, block)
            : kept.FirstOrDefault(entry => CompareUtf8(entry.Key, setting.Key) > 0)?.Line.Start ?? kept.LastOrDefault()?.Line.End ?? block.NameLine.End;
        var ending = text.NewlineAt(Math.Max(0, place - 1), "\n");
        splices.Add(new Splice(SpliceOperation.InsertEntry, place, place, Endless(text, place) ? $"{ending}{indent}{setting.Key}: {added}" : $"{indent}{setting.Key}: {added}{ending}"));
        return Ordered(splices);
    }

    /// <summary>A value is one line of text, not empty, and holds no <c>": "</c>, since an entry's key is everything before the last one (FBL 0.7 §8.1).</summary>
    private static void CheckValue(string value)
    {
        if (value.Length == 0) Planning.Plan.Refuse("A setting's value cannot be empty.");
        if (value.Contains(": ", StringComparison.Ordinal)) Planning.Plan.Refuse("A setting's value cannot hold ': '.");
        if (value.Contains('\n') || value.Contains('\r')) Planning.Plan.Refuse("A setting's value is one line.");
    }

    private static void CheckKey(string key)
    {
        if (key.Length == 0) Planning.Plan.Refuse("A setting's key cannot be empty.");
        if (key.Contains('\n') || key.Contains('\r')) Planning.Plan.Refuse("A setting's key is one line.");
    }

    /// <summary>Where an entry goes in a <c>kept</c> block among <paramref name="others"/> (FBL 0.7 §8.4): at its position, else after the last entry.</summary>
    private static int Place(List<RegistrationEntry> others, SettingPosition? position, RegistrationBlock block)
    {
        var last = others.LastOrDefault()?.Line.End ?? block.NameLine.End;
        if (position is null) return last;
        if (position.Index is { } index) return index >= 0 && index < others.Count ? others[index].Line.Start : last;
        if (position.After is { } after) return Entry(others, after).Line.End;
        return position.Before is { } before ? Entry(others, before).Line.Start : last;
    }

    private static RegistrationEntry Entry(List<RegistrationEntry> entries, string key)
    {
        var entry = entries.FirstOrDefault(candidate => candidate.Key == key);
        if (entry is null) Planning.Plan.Refuse($"The position names '{key}', which is no entry of the block.");
        return entry!;
    }

    /// <summary>Where a missing block is created (FBL 0.7 §8.4): after the nearest block before it in the order of blocks that exists, else after the headers.</summary>
    private static int CreateAt(RegistrationDocument document, IReadOnlyList<RegistrationBlockDeclaration> declarations, string name)
    {
        var order = new List<string> { "layout", "identities" };
        order.AddRange(declarations.Select(declaration => declaration.Name));
        for (var i = order.IndexOf(name) - 1; i >= 0; i--)
        {
            if (document.Block(order[i]) is { } before) return before.Span.End;
        }
        return document.AfterHeaders;
    }

    /// <summary>Whether <paramref name="offset"/> is the end of a registration whose last line has no line ending.</summary>
    private static bool Endless(Text.BodyText text, int offset) => offset == text.Length && offset > 0 && text.Lines[^1].Ending.Length == 0;

    /// <summary>The splices in body order; at one offset an insertion before a removal that starts there (FBL §8.5).</summary>
    private static List<Splice> Ordered(List<Splice> splices) =>
        splices.Select((splice, at) => (splice, at)).OrderBy(p => p.splice.Start).ThenBy(p => p.splice.End).ThenBy(p => p.at).Select(p => p.splice).ToList();

    /// <summary>Ordinal order of keys: byte order of their UTF-8 encoding (FBL §8.3).</summary>
    private static int CompareUtf8(string a, string b) => Encoding.UTF8.GetBytes(a).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(b));
}
