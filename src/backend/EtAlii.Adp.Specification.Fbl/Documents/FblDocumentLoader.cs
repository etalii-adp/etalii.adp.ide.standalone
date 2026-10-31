using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Grammar;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Fbl.Documents;

/// <summary>
/// Loads an FBL document as FBL §14.1 says, apart from validating against the JSON Schema (left to
/// etalii.adp's own CI): parse rejecting duplicate keys, check the version, map every construct,
/// resolve names, compile every regular expression and CEL expression, and check the rules. Every
/// problem is collected; a document with an error yields no document.
/// </summary>
public static class FblDocumentLoader
{
    /// <summary>The FBL major version this library reads, and the newest minor it reads in full; a document of a newer one loads with a warning.</summary>
    private static readonly LanguageVersion Fbl = new("FBL", 0, 9, "FBL §2.7");

    public static IReadOnlyList<LoadDiagnostic> Load(string path, out FblDocument? document) =>
        Load(ReadShared(path), Path.GetDirectoryName(Path.GetFullPath(path)), out document);

    /// <summary>Reads at the sharing the repository's own reader uses, so a concurrent save is never refused.</summary>
    internal static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static IReadOnlyList<LoadDiagnostic> Load(byte[] json, out FblDocument? document) =>
        Load(json, null, out document);

    /// <summary>
    /// Loads a document whose files beside it are in <paramref name="folder"/>: the grammar file and the imports of a
    /// binding of the <c>grammar</c> family (FBL 0.9 §4.9). Without a folder, a binding that names a grammar file is refused.
    /// </summary>
    public static IReadOnlyList<LoadDiagnostic> Load(byte[] json, string? folder, out FblDocument? document)
    {
        var problems = new List<LoadDiagnostic>();
        document = null;
        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException e)
        {
            problems.Add(new LoadDiagnostic("", LoadSeverity.Error, $"The document is not JSON: {e.Message}"));
            return problems;
        }

        using (parsed)
        {
            var root = parsed.RootElement;
            problems.AddRange(JsonDuplicateKeys.Find(root, "FBL §2.1"));
            if (problems.Count > 0) return problems;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add(new LoadDiagnostic("", LoadSeverity.Error, "An FBL document is a JSON object."));
                return problems;
            }

            // A version this library does not read is reported, and the rest of the document still is (behaviour change B-08).
            var version = root.TryGetProperty("fbl", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : null;
            if (version is null)
            {
                problems.Add(new LoadDiagnostic("/fbl", LoadSeverity.Error, "The document does not say which FBL version it is written in."));
            }
            else if (Fbl.Check(version, "/fbl") is { } versionProblem)
            {
                problems.Add(versionProblem);
            }
            if (!root.TryGetProperty("bindings", out var bindings) || bindings.ValueKind != JsonValueKind.Object || !bindings.EnumerateObject().Any())
            {
                problems.Add(new LoadDiagnostic("/bindings", LoadSeverity.Error, "A document has at least one binding."));
                return problems;
            }

            var result = new Dictionary<string, FblBinding>(StringComparer.Ordinal);
            foreach (var property in bindings.EnumerateObject())
            {
                var pointer = JsonPointer.Append("/bindings", property.Name);
                if (!Names.IsName(property.Name))
                {
                    problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, $"'{property.Name}' is not a valid binding name."));
                }
                var binding = BindingReader.Read(property.Name, property.Value, pointer, problems);
                if (binding is not null)
                {
                    BindingChecker.Check(binding, pointer, problems);
                    if (binding.Body is { Family: Family.Grammar, Grammar: { } grammar })
                    {
                        grammar.Built = GrammarBuild.Build(grammar, folder, pointer + "/body", problems);
                        if (grammar.Built is { } built) GrammarChecks.CheckSelectors(binding, built, pointer, problems);
                    }
                    result[property.Name] = binding;
                }
            }

            if (problems.Any(p => p.Severity == LoadSeverity.Error)) return problems;
            document = new FblDocument { Version = version!, Bindings = result };
            return problems;
        }
    }

    /// <summary>Resolves a binding reference, <c>doc.fbl#name</c> or <c>#name</c>, against <paramref name="referrer"/> (FBL §2.3).</summary>
    public static FblBinding ResolveReference(string reference, string referrer, out IReadOnlyList<LoadDiagnostic> problems)
    {
        var hash = reference.LastIndexOf('#');
        if (hash < 0) throw new ArgumentException($"'{reference}' is not a binding reference.", nameof(reference));
        var file = reference[..hash];
        var name = reference[(hash + 1)..];
        var path = file.Length == 0 ? referrer : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(referrer) ?? ".", file));
        problems = Load(path, out var document);
        if (document is null) throw new InvalidOperationException($"The FBL document '{path}' does not load: {string.Join("; ", problems)}");
        return document.Bindings.TryGetValue(name, out var binding)
            ? binding
            : throw new InvalidOperationException($"The FBL document '{path}' has no binding '{name}'.");
    }
}

internal static class Names
{
    public static bool IsName(string name)
    {
        if (name.Length == 0) return false;
        if (!(char.IsAsciiLetter(name[0]) || name[0] == '_')) return false;
        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-')) return false;
        }
        return true;
    }
}
