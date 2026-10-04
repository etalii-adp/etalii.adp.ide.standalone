using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.RealFiles;

/// <summary>One vendored binding tried on the real files: which files it takes, and how many there were when the suite was written.</summary>
internal sealed record CorpusBinding(string Key, string Document, string Binding, int Minimum, Func<string, byte[], bool> Selects);

/// <summary>
/// The real files of this repository (Requirement 11.1): everything under <c>src/</c> except
/// <c>node_modules</c>, <c>bin</c>, <c>obj</c> and this project's vendored folder, enumerated at run
/// time so a file added later is covered without a change here. Each binding's enumeration asserts a
/// minimum count, so a broken enumeration fails rather than passing on zero files.
/// </summary>
internal static class RealFileCorpus
{
    private static readonly string[] _excludedFolders = ["node_modules", "bin", "obj"];

    /// <summary>The declared file bindings and the files each claims, with the counts of Requirement 11.1.</summary>
    public static readonly IReadOnlyList<CorpusBinding> Declared =
    [
        new("timeline", "timeline.fbl", "timeline", 15, (path, _) => Extension(path, ".tml")),
        new("causal-loop", "causal-loop-diagram.fbl", "cld", 4, (path, _) => Extension(path, ".cld")),
        new("mindmap", "mindmap.fbl", "freeplane", 5, (path, _) => Extension(path, ".mm")),
        new("structurizr", "structurizr.fbl", "workspace", 16, (path, _) => Extension(path, ".dsl")),
        new("databricks-job", "databricks-job.fbl", "job", 4, (path, bytes) => (Extension(path, ".yml") || Extension(path, ".yaml")) && Contains(bytes, "task_key")),
        new("databricks-pipeline", "databricks-pipeline.fbl", "settings", 2, (path, bytes) => Extension(path, ".json") && Contains(bytes, "\"libraries\"")),
    ];

    /// <summary>Registrations: every <c>.adp</c> under <c>src/</c>.</summary>
    public const int MinimumRegistrations = 181;

    /// <summary>Folders holding a <c>Chart.yaml</c>.</summary>
    public const int MinimumCharts = 13;

    /// <summary>Turtle and N-Triples files.</summary>
    public const int MinimumTurtle = 33;

    private static readonly Lazy<IReadOnlyList<string>> _files = new(Enumerate);

    private static readonly Dictionary<string, FblBinding> _bindings = new(StringComparer.Ordinal);

    /// <summary>Every real file, relative to the repository root, <c>/</c>-separated, in ordinal order.</summary>
    public static IReadOnlyList<string> All => _files.Value;

    public static string FullPath(string relative) => Path.Combine(Repository.Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public static CorpusBinding Find(string key) => Declared.Single(b => b.Key == key);

    /// <summary>The files a declared binding takes.</summary>
    public static IReadOnlyList<string> Files(CorpusBinding binding) =>
        All.Where(path => binding.Selects(path, Head(path))).ToList();

    /// <summary>The <c>(binding, file)</c> pairs of every declared binding, for a theory.</summary>
    public static TheoryData<string, string> Pairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var binding in Declared)
        {
            foreach (var file in Files(binding)) data.Add(binding.Key, file);
        }
        return data;
    }

    public static TheoryData<string> Keys()
    {
        var data = new TheoryData<string>();
        foreach (var binding in Declared) data.Add(binding.Key);
        return data;
    }

    /// <summary>A vendored binding by its document and name, loaded once and checked for load errors.</summary>
    public static FblBinding Binding(string document, string name)
    {
        lock (_bindings)
        {
            var key = document + "#" + name;
            if (_bindings.TryGetValue(key, out var binding)) return binding;
            var problems = FblDocumentLoader.Load(Path.Combine(Repository.Conformance, document), out var loaded);
            Assert.DoesNotContain(problems, p => p.Severity == ProblemSeverity.Error);
            foreach ((string bindingName, FblBinding value) in loaded!.Bindings) _bindings[document + "#" + bindingName] = value;
            return _bindings[key];
        }
    }

    public static FblBinding Binding(CorpusBinding binding) => Binding(binding.Document, binding.Binding);

    /// <summary>Every vendored binding, with the document it is in.</summary>
    public static IReadOnlyList<(string Document, FblBinding Binding)> AllBindings()
    {
        var all = new List<(string, FblBinding)>();
        foreach (var path in Directory.GetFiles(Repository.Conformance, "*.fbl").Order(StringComparer.Ordinal))
        {
            var document = Path.GetFileName(path);
            var problems = FblDocumentLoader.Load(path, out var loaded);
            Assert.DoesNotContain(problems, p => p.Severity == ProblemSeverity.Error);
            foreach (var name in loaded!.Bindings.Keys) all.Add((document, Binding(document, name)));
        }
        return all;
    }

    /// <summary>The options a real file is read with: its path for findings and the natural ids the fixtures use.</summary>
    public static FblOptions Options(FblBinding binding, string relative, IReadOnlyDictionary<string, string>? headers = null) => new()
    {
        FileName = relative,
        DeriveId = NaturalIds.For(binding.Name),
        RegistrationHeaders = headers ?? new Dictionary<string, string>(),
        Resource = headers is not null && headers.TryGetValue("resource", out var resource) ? resource : null,
    };

    private static IReadOnlyList<string> Enumerate()
    {
        var root = Repository.Root;
        var vendored = Path.GetFullPath(Repository.Conformance);
        var files = new List<string>();
        Walk(new DirectoryInfo(Repository.Source));
        return files.Order(StringComparer.Ordinal).ToList();

        void Walk(DirectoryInfo folder)
        {
            foreach (var entry in folder.EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (entry is DirectoryInfo directory)
                {
                    if (_excludedFolders.Contains(directory.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (string.Equals(Path.GetFullPath(directory.FullName), vendored, StringComparison.Ordinal)) continue;
                    Walk(directory);
                }
                else
                {
                    files.Add(Path.GetRelativePath(root, entry.FullName).Replace(Path.DirectorySeparatorChar, '/'));
                }
            }
        }
    }

    private static bool Extension(string path, string extension) => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(byte[] bytes, string text) => Encoding.UTF8.GetString(bytes).Contains(text, StringComparison.Ordinal);

    /// <summary>The bytes a content selector looks at; only files whose extension could match are read.</summary>
    private static byte[] Head(string path) =>
        Extension(path, ".yml") || Extension(path, ".yaml") || Extension(path, ".json") ? File.ReadAllBytes(FullPath(path)) : [];
}
