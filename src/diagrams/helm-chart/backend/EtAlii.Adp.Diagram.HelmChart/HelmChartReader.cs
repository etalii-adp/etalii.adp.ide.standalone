using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Fbl.Routing;
using Serilog;
using YamlDotNet.RepresentationModel;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// Reads one chart folder into a <see cref="HelmChart"/>: the full Requirement 1 inventory,
/// tolerantly, from the files the folder body hands the plugin, without opening a file or writing a byte.
/// </summary>
/// <remarks>
/// <para>
/// Chart-owned YAML goes through <see cref="HelmYaml"/>; <c>templates/</c> content goes
/// through <see cref="TemplateScan"/> and is never parsed as YAML (Requirement 3.2). Files
/// and folders beyond the recognized inventory - a <c>ci/</c> folder, readmes, source code -
/// are not handed by the binding, and a handed file the reader does not use is passed over without
/// complaint: a chart living inside a larger repository is the normal case, not an error (Requirement 1.3).
/// </para>
/// <para>
/// <b>Every <c>File</c> and <c>Directory</c> call of the reader that read the disk is a lookup</b> in what it is
/// handed (helm-chart-disl-fbl, Requirement 3.2): the selected files, by relative path and bytes, and the
/// directories <see cref="HelmFolderEntries"/> adds, each a path ending in <c>/</c>. A name is looked up as
/// the file system would have found it, ignoring case where the platform does, which is how the binding's
/// globs select; the matches the reader always made ignoring case (<c>values.yaml</c>, <c>NOTES.txt</c>, the
/// YAML extensions) still do on every platform.
/// </para>
/// <para>
/// Everything handed arrives in ordinal order of its relative path, so two reads of the same folder are
/// the same model. A file handed without bytes (one larger than the host reads) costs that file's detail and
/// nothing else. There is no write path in this class, structurally - the zero-writes proof leans on that.
/// </para>
/// </remarks>
public sealed class HelmChartReader
{
    private static readonly ILogger _logger = Log.ForContext<HelmChartReader>();

    /// <summary>Reads the chart the folder body hands in <paramref name="request"/>.</summary>
    public HelmChart Read(PluginReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var handed = new Handed(request);

        ChartMetadata? metadata = null;
        TolerantYamlFailure? metadataFailure = null;
        YamlMappingNode? chartRoot = null;
        if (handed.File("Chart.yaml") is { } chartYaml)
        {
            switch (HelmYaml.Read(chartYaml, "Chart.yaml"))
            {
                case TolerantYamlDocument { Root: YamlMappingNode mapping }:
                    chartRoot = mapping;
                    metadata = ReadMetadata(mapping);
                    break;
                case TolerantYamlDocument:
                    // Parsed but empty, or not a mapping: no metadata to show, nothing to blame
                    // a line on. Validation reports the missing name/version off the null.
                    break;
                case TolerantYamlUnreadable unreadable:
                    metadataFailure = unreadable.Failure;
                    break;
            }
        }

        var legacy = string.Equals(metadata?.ApiVersion, "v1", StringComparison.OrdinalIgnoreCase);

        (IReadOnlyList<ValuesFile> values, YamlMappingNode? defaultValuesRoot) = ReadValues(handed);
        (IReadOnlyList<DependencyDeclaration> dependencies, TolerantYamlFailure? dependenciesFailure) = ReadDependencies(handed, chartRoot, legacy, defaultValuesRoot);

        return new HelmChart(
            IsChart: true,
            metadata,
            metadataFailure,
            legacy,
            values,
            ReadSchema(handed),
            ReadTemplates(handed),
            ReadCrds(handed),
            dependencies,
            ReadVendored(handed),
            ReadLock(handed, legacy),
            dependenciesFailure);
    }

    private static ChartMetadata ReadMetadata(YamlMappingNode root)
    {
        var version = Scalar(root, "version");
        return new ChartMetadata(
            Scalar(root, "name")?.Value,
            version?.Value,
            Scalar(root, "appVersion")?.Value,
            Scalar(root, "apiVersion")?.Value,
            Scalar(root, "type")?.Value is { Length: > 0 } type ? type : "application",
            Scalar(root, "description")?.Value ?? string.Empty,
            bool.TryParse(Scalar(root, "deprecated")?.Value, out var deprecated) && deprecated,
            Line(root),
            version is null ? 0 : Line(version));

        static YamlScalarNode? Scalar(YamlMappingNode root, string key) =>
            root.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
                ? scalar
                : null;
    }

    private static (IReadOnlyList<ValuesFile> Values, YamlMappingNode? DefaultRoot) ReadValues(Handed handed)
    {
        var result = new List<ValuesFile>();
        YamlMappingNode? defaultRoot = null;

        foreach (var file in handed.FilesIn("", name => name.StartsWith("values", Handed.Names)))
        {
            var name = file.RelativePath;
            var extension = IoPath.GetExtension(name);
            if (!string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase))
            {
                // values.schema.json matches the prefix but is not a values layer.
                continue;
            }

            var isDefault = string.Equals(name, "values.yaml", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(name, "values.yml", StringComparison.OrdinalIgnoreCase);

            switch (HelmYaml.Read(file, name))
            {
                case TolerantYamlDocument { Root: YamlMappingNode mapping }:
                    if (isDefault)
                    {
                        defaultRoot = mapping;
                    }

                    var keys = mapping.Children.Keys
                        .OfType<YamlScalarNode>()
                        .Select(key => key.Value ?? string.Empty)
                        .Where(key => key.Length > 0)
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    result.Add(new ValuesFile(name, isDefault, keys.Contains("global", StringComparer.Ordinal), keys, null));
                    break;
                case TolerantYamlDocument:
                    // Empty, or not a mapping: a values layer with nothing in it yet.
                    result.Add(new ValuesFile(name, isDefault, false, [], null));
                    break;
                case TolerantYamlUnreadable unreadable:
                    result.Add(new ValuesFile(name, isDefault, false, [], unreadable.Failure));
                    break;
            }
        }

        // The default layer first, then the overrides ordinally: the stack as it is drawn.
        return (
            [.. result.OrderBy(file => file.IsDefault ? 0 : 1).ThenBy(file => file.RelativePath, StringComparer.Ordinal)],
            defaultRoot);
    }

    private static SchemaFile? ReadSchema(Handed handed) =>
        handed.File("values.schema.json") is not null
            ? new SchemaFile("values.schema.json")
            : null;

    private static IReadOnlyList<TemplateFile> ReadTemplates(Handed handed)
    {
        var result = new List<TemplateFile>();
        foreach (var file in handed.FilesUnder("templates"))
        {
            var relative = file.RelativePath;
            var name = FileName(relative);
            var role =
                name.StartsWith('_') ? TemplateRole.Partial
                : string.Equals(name, "NOTES.txt", StringComparison.OrdinalIgnoreCase) ? TemplateRole.Notes
                : relative.StartsWith("templates/tests/", StringComparison.OrdinalIgnoreCase) ? TemplateRole.Test
                : TemplateRole.Manifest;

            result.Add(new TemplateFile(relative, role, Scan(file)));
        }

        return [.. result.OrderBy(template => template.RelativePath, StringComparer.Ordinal)];
    }

    /// <summary>A template that cannot be read yields no facts - and no exception (Requirement 3.5).</summary>
    private static TemplateFacts Scan(PluginFile file)
    {
        if (file.IsOversized)
        {
            _logger.Debug("Not scanning template {Path}: it is larger than the host reads", file.RelativePath);
            return new TemplateFacts([], [], [], []);
        }

        using var text = HelmYaml.Text(file);
        return TemplateScan.Scan(text.ReadToEnd());
    }

    private static CrdsSummary? ReadCrds(Handed handed)
    {
        if (!handed.FolderExists("crds"))
        {
            return null;
        }

        var count = 0;
        var failures = new List<TolerantYamlFailure>();
        foreach (var file in handed.FilesUnder("crds"))
        {
            var extension = IoPath.GetExtension(file.RelativePath);
            if (!string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count++;
            if (HelmYaml.Read(file, file.RelativePath) is TolerantYamlUnreadable unreadable)
            {
                failures.Add(unreadable.Failure);
            }
        }

        return new CrdsSummary(count, [.. failures.OrderBy(failure => failure.RelativePath, StringComparer.Ordinal)]);
    }

    private static (IReadOnlyList<DependencyDeclaration> Dependencies, TolerantYamlFailure? Failure) ReadDependencies(
        Handed handed, YamlMappingNode? chartRoot, bool legacy, YamlMappingNode? defaultValuesRoot)
    {
        YamlNode? list = null;
        if (legacy)
        {
            // Helm 2: dependencies live in their own file beside Chart.yaml - whose own parse
            // failure is a reportable fact, not a silent absence (Requirement 10.3).
            if (handed.File("requirements.yaml") is { } requirements)
            {
                switch (HelmYaml.Read(requirements, "requirements.yaml"))
                {
                    case TolerantYamlDocument { Root: YamlMappingNode root }:
                        root.Children.TryGetValue(new YamlScalarNode("dependencies"), out list);
                        break;
                    case TolerantYamlUnreadable unreadable:
                        return ([], unreadable.Failure);
                }
            }
        }
        else
        {
            chartRoot?.Children.TryGetValue(new YamlScalarNode("dependencies"), out list);
        }

        if (list is not YamlSequenceNode sequence)
        {
            return ([], null);
        }

        var result = new List<DependencyDeclaration>();
        foreach (var entry in sequence.Children.OfType<YamlMappingNode>())
        {
            var name = Scalar(entry, "name");
            if (name is not { Length: > 0 })
            {
                // A nameless dependency entry declares nothing recognizable.
                continue;
            }

            var condition = Scalar(entry, "condition");
            result.Add(new DependencyDeclaration(
                name,
                Scalar(entry, "alias"),
                Scalar(entry, "version"),
                Scalar(entry, "repository"),
                condition,
                Resolve(condition, defaultValuesRoot),
                Line(entry)));
        }

        return ([.. result.OrderBy(dependency => dependency.EffectiveName, StringComparer.Ordinal)], null);

        static string? Scalar(YamlMappingNode entry, string key) =>
            entry.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
                ? scalar.Value
                : null;
    }

    /// <summary>Walks a dotted condition path into the default values (Requirement 5.5).</summary>
    private static ConditionState Resolve(string? condition, YamlMappingNode? defaultValuesRoot)
    {
        if (condition is not { Length: > 0 })
        {
            return ConditionState.None;
        }

        if (defaultValuesRoot is null)
        {
            // No parsed default values to resolve against: the truth is unknowable, which is
            // not the same as the path being absent.
            return ConditionState.Unknown;
        }

        YamlNode current = defaultValuesRoot;
        foreach (var segment in condition.Split('.'))
        {
            if (current is not YamlMappingNode mapping
                || !mapping.Children.TryGetValue(new YamlScalarNode(segment), out var next))
            {
                return ConditionState.Missing;
            }

            current = next;
        }

        return current is YamlScalarNode scalar && bool.TryParse(scalar.Value, out var value)
            ? value ? ConditionState.On : ConditionState.Off
            : ConditionState.Unknown;
    }

    private static IReadOnlyList<VendoredEntry> ReadVendored(Handed handed)
    {
        var result = new List<VendoredEntry>();

        foreach (var directory in handed.Directories("charts"))
        {
            result.Add(ReadUnpacked(handed, directory));
        }

        foreach (var file in handed.FilesIn("charts", name => name.EndsWith(".tgz", Handed.Names)))
        {
            var fileName = FileName(file.RelativePath);
            result.Add(new VendoredEntry(
                StripVersionTail(fileName),
                file.RelativePath,
                Sealed: true,
                ChartName: null,
                ChartVersion: null,
                ChartType: "application",
                TemplateCount: 0,
                DeeperCount: 0,
                Failure: null));
        }

        return [.. result.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)];
    }

    /// <summary>One level deep, per Requirement 3.4: the nested chart summarized, never recursed further.</summary>
    private static VendoredEntry ReadUnpacked(Handed handed, string relative)
    {
        var entryName = FileName(relative);
        var templateCount = handed.FilesUnder($"{relative}/templates").Count();
        var deeperCount = handed.FilesIn($"{relative}/charts", _ => true).Count() + handed.Directories($"{relative}/charts").Count();

        var nestedChartYaml = $"{relative}/Chart.yaml";
        if (handed.File(nestedChartYaml) is not { } nested)
        {
            return new VendoredEntry(entryName, relative, false, null, null, "application", templateCount, deeperCount, null);
        }

        return HelmYaml.Read(nested, nested.RelativePath) switch
        {
            TolerantYamlDocument { Root: YamlMappingNode mapping } => Summarize(mapping),
            TolerantYamlUnreadable unreadable => new VendoredEntry(
                entryName, relative, false, null, null, "application", templateCount, deeperCount, unreadable.Failure),
            _ => new VendoredEntry(entryName, relative, false, null, null, "application", templateCount, deeperCount, null),
        };

        VendoredEntry Summarize(YamlMappingNode mapping)
        {
            var metadata = ReadMetadata(mapping);
            return new VendoredEntry(
                entryName, relative, false, metadata.Name, metadata.Version, metadata.ChartType, templateCount, deeperCount, null);
        }
    }

    /// <summary>The archive name without its version-and-extension tail: <c>common-2.31.4.tgz</c> matches <c>common</c>.</summary>
    internal static string StripVersionTail(string fileName)
    {
        var stem = fileName[..^".tgz".Length];
        var dash = stem.LastIndexOf('-');
        return dash > 0 && stem.Length > dash + 1 && char.IsAsciiDigit(stem[dash + 1]) ? stem[..dash] : stem;
    }

    private static LockFile? ReadLock(Handed handed, bool legacy)
    {
        var name = legacy ? "requirements.lock" : "Chart.lock";
        if (handed.File(name) is not { } file)
        {
            return null;
        }

        switch (HelmYaml.Read(file, name))
        {
            case TolerantYamlUnreadable unreadable:
                return new LockFile(name, [], unreadable.Failure);
            case TolerantYamlDocument { Root: YamlMappingNode root }
                when root.Children.TryGetValue(new YamlScalarNode("dependencies"), out var list)
                     && list is YamlSequenceNode sequence:
                var entries = new List<LockEntry>();
                foreach (var entry in sequence.Children.OfType<YamlMappingNode>())
                {
                    if (Scalar(entry, "name") is { Length: > 0 } entryName)
                    {
                        entries.Add(new LockEntry(entryName, Scalar(entry, "version"), Line(entry)));
                    }
                }

                return new LockFile(name, [.. entries.OrderBy(entry => entry.Name, StringComparer.Ordinal)], null);
            default:
                return new LockFile(name, [], null);
        }

        static string? Scalar(YamlMappingNode entry, string key) =>
            entry.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
                ? scalar.Value
                : null;
    }

    private static string FileName(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        return slash >= 0 ? relativePath[(slash + 1)..] : relativePath;
    }

    private static uint Line(YamlNode node) => (uint)Math.Max(node.Start.Line, 0);

    /// <summary>
    /// What the plugin is handed, looked up as the file system would have answered the reader: a name compared
    /// ignoring case where the platform's file system does (<see cref="Glob.PlatformIgnoresCase"/>), a folder
    /// present when a file under it was handed or a directory entry names it.
    /// </summary>
    private sealed class Handed
    {
        /// <summary>How the platform's file system compares a name, which is how the binding's globs select.</summary>
        public static readonly StringComparison Names = Glob.PlatformIgnoresCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private readonly IReadOnlyList<PluginFile> _files;
        private readonly IReadOnlyList<string> _directories;

        public Handed(PluginReadRequest request)
        {
            _files = request.Files;
            _directories = [.. request.SupplementaryEntries.Where(entry => entry.EndsWith('/')).Select(entry => entry.TrimEnd('/'))];
        }

        /// <summary>The handed file at <paramref name="relative"/>, or null.</summary>
        public PluginFile? File(string relative) =>
            _files.FirstOrDefault(file => string.Equals(file.RelativePath, relative, Names));

        /// <summary>The handed files directly in <paramref name="folder"/> (the chart folder for empty) whose name <paramref name="accept"/> takes.</summary>
        public IEnumerable<PluginFile> FilesIn(string folder, Func<string, bool> accept) =>
            _files.Where(file => ParentOf(file.RelativePath) is { } parent && string.Equals(parent, folder, Names) && accept(FileName(file.RelativePath)));

        /// <summary>The handed files anywhere under <paramref name="folder"/>.</summary>
        public IEnumerable<PluginFile> FilesUnder(string folder) =>
            _files.Where(file => file.RelativePath.StartsWith(folder + "/", Names));

        /// <summary>
        /// The directories directly in <paramref name="folder"/>, as relative paths, in ordinal order: those a directory entry
        /// names, and those a handed file lies in.
        /// </summary>
        public IEnumerable<string> Directories(string folder)
        {
            var prefix = folder + "/";
            var holding = _files
                .Where(file => file.RelativePath.StartsWith(prefix, Names) && file.RelativePath.IndexOf('/', prefix.Length) > 0)
                .Select(file => file.RelativePath[..file.RelativePath.IndexOf('/', prefix.Length)]);
            return _directories
                .Where(directory => ParentOf(directory) is { } parent && string.Equals(parent, folder, Names))
                .Concat(holding)
                .Distinct(Glob.PlatformIgnoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);
        }

        /// <summary>Whether <paramref name="folder"/> exists: a directory entry names it, or a file under it was handed.</summary>
        public bool FolderExists(string folder) =>
            _directories.Any(directory => string.Equals(directory, folder, Names)) || FilesUnder(folder).Any();

        private static string? ParentOf(string relative)
        {
            var slash = relative.LastIndexOf('/');
            return slash >= 0 ? relative[..slash] : "";
        }
    }
}
