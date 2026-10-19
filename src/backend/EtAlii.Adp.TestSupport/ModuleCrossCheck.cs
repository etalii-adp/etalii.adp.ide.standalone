using System.Text;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.History;
using Xunit;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// Requirement 11.6: the ids per type a vendored binding reads from each real file equal the ids the module's own reader
/// reads, every difference a listed divergence. The check of each module lives in that module's test project, so
/// that the FBL test project references no tool module (code-structure T287, SC-013); the corpus, the vendored bindings
/// and <c>divergences.json</c> it reads through here stay one.
/// </summary>
public static class ModuleCrossCheck
{
    /// <summary>How many ids of a difference the observation names before it counts the rest.</summary>
    private const int Named = 5;

    /// <summary>The real files the declared binding <paramref name="key"/> takes, for a theory.</summary>
    public static TheoryData<string> Files(string key)
    {
        var data = new TheoryData<string>();
        foreach (var file in RealFileCorpus.Files(RealFileCorpus.Find(key))) data.Add(file);
        return data;
    }

    /// <summary>
    /// Reads <paramref name="file"/> through the declared binding <paramref name="key"/> and through the module, and
    /// checks the difference against the ledger. <paramref name="idOf"/> gives the id an element of the binding is
    /// compared by, or null for one the module cannot know, such as one addressed only by its place.
    /// </summary>
    public static void Check(string key, string file, Func<FblElement, string?> idOf, Func<string, IReadOnlyDictionary<string, SortedSet<string>>> module)
    {
        // Arrange.
        var bytes = File.ReadAllBytes(RealFileCorpus.FullPath(file));
        var binding = RealFileCorpus.Binding(RealFileCorpus.Find(key));

        // Act.
        var body = OpenBody.Open(bytes, binding, RealFileCorpus.Options(binding, file));
        if (body.Model.Unreadable) return;
        var fbl = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var element in body.Model.Elements)
        {
            if (idOf(element) is { } id && !id.Contains('@', StringComparison.Ordinal)) Add(fbl, element.Type, id);
        }

        // Assert.
        Divergences.Check("cross-check", key, file, Difference(fbl, module(Encoding.UTF8.GetString(bytes))));
    }

    public static void Add(Dictionary<string, SortedSet<string>> ids, string type, string id)
    {
        if (!ids.TryGetValue(type, out var set))
        {
            set = new SortedSet<string>(StringComparer.Ordinal);
            ids[type] = set;
        }
        set.Add(id);
    }

    /// <summary>What the binding lacks and adds against the module, per type, or null when the two read the same.</summary>
    public static string? Difference(IReadOnlyDictionary<string, SortedSet<string>> fbl, IReadOnlyDictionary<string, SortedSet<string>> module)
    {
        var parts = new List<string>();
        foreach (var type in fbl.Keys.Union(module.Keys).Order(StringComparer.Ordinal))
        {
            var read = fbl.GetValueOrDefault(type) ?? [];
            var expected = module.GetValueOrDefault(type) ?? [];
            var lacks = expected.Except(read).ToList();
            var adds = read.Except(expected).ToList();
            if (lacks.Count == 0 && adds.Count == 0) continue;
            var part = new StringBuilder(type).Append(':');
            if (lacks.Count > 0) part.Append(" the binding lacks ").Append(List(lacks));
            if (adds.Count > 0) part.Append(lacks.Count > 0 ? ";" : "").Append(" the binding adds ").Append(List(adds));
            parts.Add(part.ToString());
        }
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string List(List<string> ids) =>
        $"{ids.Count} ({string.Join(", ", ids.Take(Named))}{(ids.Count > Named ? ", …" : "")})";
}
