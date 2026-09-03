using Serilog;

using YamlDotNet.RepresentationModel;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// Reads one chart folder into a <see cref="HelmChart"/>: the full Requirement 1 inventory,
/// tolerantly, without ever writing a byte.
/// </summary>
/// <remarks>
/// <para>
/// Chart-owned YAML goes through <see cref="HelmYaml"/>; <c>templates/</c> content goes
/// through <see cref="TemplateScan"/> and is never parsed as YAML (Requirement 3.2). Files
/// and folders beyond the recognized inventory - a <c>ci/</c> folder, readmes, source code -
/// are ignored without complaint: a chart living inside a larger repository is the normal
/// case, not an error (Requirement 1.3).
/// </para>
/// <para>
/// Every enumeration is sorted ordinally so two reads of the same folder are the same model,
/// and reparse points are skipped so a symlink loop cannot hang the read. There is no write
/// path in this class, structurally - the zero-writes proof leans on that.
/// </para>
/// </remarks>
public sealed class HelmChartReader
{
    private static readonly ILogger _logger = Log.ForContext<HelmChartReader>();

    private static readonly EnumerationOptions _files = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false,
    };

    /// <summary>Reads the chart rooted at <paramref name="rootPath"/>.</summary>
    public HelmChart Read(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        if (!Directory.Exists(rootPath))
        {
            // A deleted folder degrades to the same state as a folder with no chart in it.
            return HelmChart.NotAChart;
        }

        var chartYaml = IoPath.Combine(rootPath, "Chart.yaml");
        if (!File.Exists(chartYaml))
        {
            return HelmChart.NotAChart;
        }

        ChartMetadata? metadata = null;
        HelmYamlFailure? metadataFailure = null;
        YamlMappingNode? chartRoot = null;
        switch (HelmYaml.Read(chartYaml, "Chart.yaml"))
        {
            case HelmYamlDocument { Root: YamlMappingNode mapping }:
                chartRoot = mapping;
                metadata = ReadMetadata(mapping);
                break;
            case HelmYamlDocument:
                // Parsed but empty, or not a mapping: no metadata to show, nothing to blame
                // a line on. Validation reports the missing name/version off the null.
                break;
            case HelmYamlUnreadable unreadable:
                metadataFailure = unreadable.Failure;
                break;
        }

        var legacy = string.Equals(metadata?.ApiVersion, "v1", StringComparison.OrdinalIgnoreCase);

        var (values, defaultValuesRoot) = ReadValues(rootPath);
        var dependencies = ReadDependencies(rootPath, chartRoot, legacy, defaultValuesRoot);

        return new HelmChart(
            IsChart: true,
            metadata,
            metadataFailure,
            legacy,
            values,
            ReadSchema(rootPath),
            ReadTemplates(rootPath),
            ReadCrds(rootPath),
            dependencies,
            ReadVendored(rootPath),
            ReadLock(rootPath, legacy));
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

    private static (IReadOnlyList<ValuesFile> Values, YamlMappingNode? DefaultRoot) ReadValues(string rootPath)
    {
        var result = new List<ValuesFile>();
        YamlMappingNode? defaultRoot = null;

        foreach (var path in EnumerateFiles(rootPath, "values*"))
        {
            var name = IoPath.GetFileName(path);
            var extension = IoPath.GetExtension(name);
            if (!string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase))
            {
                // values.schema.json matches the prefix but is not a values layer.
                continue;
            }

            var isDefault = string.Equals(name, "values.yaml", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(name, "values.yml", StringComparison.OrdinalIgnoreCase);

            switch (HelmYaml.Read(path, name))
            {
                case HelmYamlDocument { Root: YamlMappingNode mapping }:
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
                case HelmYamlDocument:
                    // Empty, or not a mapping: a values layer with nothing in it yet.
                    result.Add(new ValuesFile(name, isDefault, false, [], null));
                    break;
                case HelmYamlUnreadable unreadable:
                    result.Add(new ValuesFile(name, isDefault, false, [], unreadable.Failure));
                    break;
            }
        }

        // The default layer first, then the overrides ordinally: the stack as it is drawn.
        return (
            [.. result.OrderBy(file => file.IsDefault ? 0 : 1).ThenBy(file => file.RelativePath, StringComparer.Ordinal)],
            defaultRoot);
    }

    private static SchemaFile? ReadSchema(string rootPath) =>
        File.Exists(IoPath.Combine(rootPath, "values.schema.json"))
            ? new SchemaFile("values.schema.json")
            : null;

    private IReadOnlyList<TemplateFile> ReadTemplates(string rootPath)
    {
        var templatesPath = IoPath.Combine(rootPath, "templates");
        if (!Directory.Exists(templatesPath))
        {
            return [];
        }

        var result = new List<TemplateFile>();
        foreach (var path in EnumerateFiles(templatesPath, "*", recurse: true))
        {
            var relative = Relative(rootPath, path);
            var name = IoPath.GetFileName(path);
            var role =
                name.StartsWith('_') ? TemplateRole.Partial
                : string.Equals(name, "NOTES.txt", StringComparison.OrdinalIgnoreCase) ? TemplateRole.Notes
                : relative.StartsWith("templates/tests/", StringComparison.OrdinalIgnoreCase) ? TemplateRole.Test
                : TemplateRole.Manifest;

            result.Add(new TemplateFile(relative, role, Scan(path)));
        }

        return [.. result.OrderBy(template => template.RelativePath, StringComparer.Ordinal)];
    }

    /// <summary>A template that cannot be read yields no facts - and no exception (Requirement 3.5).</summary>
    private static TemplateFacts Scan(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return TemplateScan.Scan(reader.ReadToEnd());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Debug(exception, "Could not scan template {Path}", path);
            return new TemplateFacts([], [], [], []);
        }
    }

    private static CrdsSummary? ReadCrds(string rootPath)
    {
        var crdsPath = IoPath.Combine(rootPath, "crds");
        if (!Directory.Exists(crdsPath))
        {
            return null;
        }

        var count = 0;
        var failures = new List<HelmYamlFailure>();
        foreach (var path in EnumerateFiles(crdsPath, "*", recurse: true))
        {
            var extension = IoPath.GetExtension(path);
            if (!string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count++;
            if (HelmYaml.Read(path, Relative(rootPath, path)) is HelmYamlUnreadable unreadable)
            {
                failures.Add(unreadable.Failure);
            }
        }

        return new CrdsSummary(count, [.. failures.OrderBy(failure => failure.RelativePath, StringComparer.Ordinal)]);
    }

    private static IReadOnlyList<DependencyDeclaration> ReadDependencies(
        string rootPath, YamlMappingNode? chartRoot, bool legacy, YamlMappingNode? defaultValuesRoot)
    {
        YamlNode? list = null;
        if (legacy)
        {
            // Helm 2: dependencies live in their own file beside Chart.yaml.
            var requirements = IoPath.Combine(rootPath, "requirements.yaml");
            if (File.Exists(requirements)
                && HelmYaml.Read(requirements, "requirements.yaml") is HelmYamlDocument { Root: YamlMappingNode root })
            {
                root.Children.TryGetValue(new YamlScalarNode("dependencies"), out list);
            }
        }
        else if (chartRoot is not null)
        {
            chartRoot.Children.TryGetValue(new YamlScalarNode("dependencies"), out list);
        }

        if (list is not YamlSequenceNode sequence)
        {
            return [];
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

        return [.. result.OrderBy(dependency => dependency.EffectiveName, StringComparer.Ordinal)];

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

    private IReadOnlyList<VendoredEntry> ReadVendored(string rootPath)
    {
        var chartsPath = IoPath.Combine(rootPath, "charts");
        if (!Directory.Exists(chartsPath))
        {
            return [];
        }

        var result = new List<VendoredEntry>();

        foreach (var directory in Directory.EnumerateDirectories(chartsPath, "*", _files))
        {
            result.Add(ReadUnpacked(rootPath, directory));
        }

        foreach (var file in EnumerateFiles(chartsPath, "*.tgz"))
        {
            var fileName = IoPath.GetFileName(file);
            result.Add(new VendoredEntry(
                StripVersionTail(fileName),
                Relative(rootPath, file),
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
    private static VendoredEntry ReadUnpacked(string rootPath, string directory)
    {
        var entryName = IoPath.GetFileName(directory);
        var relative = Relative(rootPath, directory);

        var templatesPath = IoPath.Combine(directory, "templates");
        var templateCount = Directory.Exists(templatesPath)
            ? Directory.EnumerateFiles(templatesPath, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                RecurseSubdirectories = true,
            }).Count()
            : 0;

        var deeperPath = IoPath.Combine(directory, "charts");
        var deeperCount = Directory.Exists(deeperPath)
            ? Directory.EnumerateFileSystemEntries(deeperPath).Count()
            : 0;

        var nestedChartYaml = IoPath.Combine(directory, "Chart.yaml");
        if (!File.Exists(nestedChartYaml))
        {
            return new VendoredEntry(entryName, relative, false, null, null, "application", templateCount, deeperCount, null);
        }

        return HelmYaml.Read(nestedChartYaml, Relative(rootPath, nestedChartYaml)) switch
        {
            HelmYamlDocument { Root: YamlMappingNode mapping } => Summarize(mapping),
            HelmYamlUnreadable unreadable => new VendoredEntry(
                entryName, relative, false, null, null, "application", templateCount, deeperCount, unreadable.Failure),
            _ => new VendoredEntry(entryName, relative, false, null, null, "application", templateCount, deeperCount, null),
        };

        VendoredEntry Summarize(YamlMappingNode mapping)
        {
            var nested = ReadMetadata(mapping);
            return new VendoredEntry(
                entryName, relative, false, nested.Name, nested.Version, nested.ChartType, templateCount, deeperCount, null);
        }
    }

    /// <summary>The archive name without its version-and-extension tail: <c>common-2.31.4.tgz</c> matches <c>common</c>.</summary>
    internal static string StripVersionTail(string fileName)
    {
        var stem = fileName[..^".tgz".Length];
        var dash = stem.LastIndexOf('-');
        return dash > 0 && stem.Length > dash + 1 && char.IsAsciiDigit(stem[dash + 1]) ? stem[..dash] : stem;
    }

    private static LockFile? ReadLock(string rootPath, bool legacy)
    {
        var name = legacy ? "requirements.lock" : "Chart.lock";
        var path = IoPath.Combine(rootPath, name);
        if (!File.Exists(path))
        {
            return null;
        }

        switch (HelmYaml.Read(path, name))
        {
            case HelmYamlUnreadable unreadable:
                return new LockFile(name, [], unreadable.Failure);
            case HelmYamlDocument { Root: YamlMappingNode root }
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

    private static IEnumerable<string> EnumerateFiles(string path, string pattern, bool recurse = false) =>
        Directory
            .EnumerateFiles(path, pattern, new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                RecurseSubdirectories = recurse,
            })
            .OrderBy(file => file, StringComparer.Ordinal);

    private static string Relative(string rootPath, string path) =>
        IoPath.GetRelativePath(rootPath, path).Replace(IoPath.DirectorySeparatorChar, '/');

    private static uint Line(YamlNode node) => (uint)Math.Max(node.Start.Line, 0);
}
