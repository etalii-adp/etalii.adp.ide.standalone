using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;

namespace EtAlii.Adp.Editor.Xml;

/// <summary>A problem compiling a schema: the sentence, and where in which file, when the platform says.</summary>
internal sealed record SchemaProblem(string Sentence, string? FullPath, int Line, int Column);

/// <summary>
/// What compiling a set of root schemas gave: the compiled set, or the first problem that stopped
/// it, or the root that declares XSD 1.1; and every project file asked for while compiling.
/// </summary>
internal sealed record CompiledSchema(XmlSchemaSet? Set, SchemaProblem? Problem, string? Version11, IReadOnlyList<string> Files);

/// <summary>
/// Compiled schemas kept between requests (xml-editor Requirement 5.8 and Performance): one entry
/// per set of root schemas, kept with the last-write time of every file the resolver was asked
/// for - includes, imports and redefines among them, and files missing at the time. The times are
/// compared before each use, and a difference compiles again; an entry unused for ten minutes is
/// dropped. Nothing is compiled again while nothing has changed.
/// </summary>
internal sealed partial class SchemaCache(TimeProvider time)
{
    private static readonly TimeSpan Unused = TimeSpan.FromMinutes(10);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public SchemaCache()
        : this(TimeProvider.System)
    {
    }

    /// <summary>How many times a set was compiled; a test reads it to see that nothing was compiled again.</summary>
    public int Compilations { get; private set; }

    /// <summary>The compiled set for <paramref name="schemaPaths"/>, the full paths of the root schemas, inside <paramref name="rootPath"/>.</summary>
    public CompiledSchema Get(string rootPath, IReadOnlyList<string> schemaPaths)
    {
        var key = string.Join('\n', [rootPath, .. schemaPaths.Order(StringComparer.Ordinal)]);
        var now = time.GetUtcNow();
        lock (_lock)
        {
            foreach (var stale in _entries.Where(pair => now - pair.Value.LastUsed > Unused).Select(pair => pair.Key).ToArray())
            {
                _entries.Remove(stale);
            }

            if (!_entries.TryGetValue(key, out var entry) || entry.Changed())
            {
                entry = new Entry(Compile(rootPath, schemaPaths, null));
                _entries[key] = entry;
            }

            entry.LastUsed = now;
            return entry.Schema;
        }
    }

    /// <summary>Compiles a schema from its text as typed - a document that is itself a schema - without keeping it.</summary>
    public CompiledSchema CompileText(string rootPath, string fullPath, string content)
    {
        lock (_lock)
        {
            return Compile(rootPath, [fullPath], content);
        }
    }

    private CompiledSchema Compile(string rootPath, IReadOnlyList<string> schemaPaths, string? content)
    {
        Compilations++;
        var resolver = new ProjectXmlResolver(rootPath);
        var set = XmlReaders.SchemaSet(resolver);
        var problems = new List<SchemaProblem>();
        set.ValidationEventHandler += (_, args) => problems.Add(ProblemOf(args.Exception, schemaPaths[0]));

        try
        {
            foreach (var path in schemaPaths)
            {
                var text = content ?? resolver.ReadText(path);
                if (DeclaresVersion11().IsMatch(text))
                {
                    return new CompiledSchema(null, null, path, resolver.Attempted.ToArray());
                }

                using var reader = XmlReaders.Schema(text, path, resolver);
                set.Add(null, reader);
            }

            if (problems.Count == 0)
            {
                set.Compile();
            }
        }
        catch (XmlSchemaException exception)
        {
            problems.Add(ProblemOf(exception, schemaPaths[0]));
        }
        catch (XmlException exception)
        {
            problems.Add(new SchemaProblem(Sentence(exception.Message), FullPathOf(exception.SourceUri) ?? schemaPaths[0], exception.LineNumber, exception.LinePosition));
        }

        var problem = problems.FirstOrDefault();
        return new CompiledSchema(problem is null ? set : null, problem, null, resolver.Attempted.ToArray());
    }

    private static SchemaProblem ProblemOf(XmlSchemaException exception, string fallback) =>
        new(Sentence(exception.Message), FullPathOf(exception.SourceUri) ?? fallback, exception.LineNumber, exception.LinePosition);

    private static string? FullPathOf(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? Path.GetFullPath(parsed.LocalPath) : null;

    private static string Sentence(string message) => TrailingPosition().Replace(message, "").Trim();

    [GeneratedRegex(@"\s*Line \d+, position \d+\.\s*$")]
    private static partial Regex TrailingPosition();

    /// <summary>An XSD that says it needs version 1.1, through the versioning namespace's <c>minVersion</c>.</summary>
    [GeneratedRegex("""http://www\.w3\.org/2007/XMLSchema-versioning[\s\S]*?:minVersion\s*=\s*["']1\.1["']""")]
    private static partial Regex DeclaresVersion11();

    private sealed class Entry(CompiledSchema schema)
    {
        private readonly Dictionary<string, DateTime> _written = schema.Files.ToDictionary(path => path, File.GetLastWriteTimeUtc, StringComparer.Ordinal);

        public CompiledSchema Schema { get; } = schema;

        public DateTimeOffset LastUsed { get; set; }

        public bool Changed() => _written.Any(pair => File.GetLastWriteTimeUtc(pair.Key) != pair.Value);
    }
}
