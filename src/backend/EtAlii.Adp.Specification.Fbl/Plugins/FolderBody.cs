using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Routing;

namespace EtAlii.Adp.Specification.Fbl.Plugins;

/// <summary>
/// A folder subject read by a persistence plugin (FBL §10, §11.2): the folder qualifies by the
/// binding's <c>recognise</c>, its files are selected by the file rules and <c>ignore</c> in ordinal
/// order of relative path and never through a reparse point (<see cref="FolderSubject"/>), and all of
/// them are handed to the plugin in one <c>read</c>. A folder that fails <c>recognise</c> or is gone
/// is an unreadable body (FBL §10.3); without the plugin the folder opens read-only with DISL's
/// <c>std.pluginMissing</c>, as <see cref="PluginBody"/> does (FBL §15.1).
/// </summary>
public sealed class FolderBody
{
    /// <summary>The key of a finding's <see cref="Finding.Detail"/> naming the element it is about, DISL's <c>subject</c> (FBL §11.4).</summary>
    public const string SubjectDetail = "subject";

    private readonly IPersistencePlugin? _plugin;
    private readonly FblOptions _options;
    private PluginReadResult _last;
    private IReadOnlyList<PluginFile> _files = [];

    private FolderBody(string folder, FblBinding binding, IPersistencePlugin? plugin, FblOptions options)
    {
        Folder = folder;
        Binding = binding;
        _options = options;
        _plugin = plugin is not null && plugin.Id == binding.Plugin!.Plugin ? plugin : null;
        _last = ReadNow();
    }

    /// <summary>The folder's full path.</summary>
    private string Folder { get; }

    private FblBinding Binding { get; }

    public FblModel Model => new(_last.Elements, _last.Findings, _last.Unreadable);

    /// <summary>Read-only without the plugin, when the folder is unreadable, or when the binding is read-only.</summary>
    public bool IsReadOnly => _plugin is null || _last.Unreadable || Binding.ReadOnly is not null;

    /// <summary>
    /// Opens a folder subject whose binding names a plugin. <paramref name="plugin"/> is the one the
    /// caller has installed, or null; a plugin with another id counts as missing.
    /// </summary>
    public static FolderBody Open(string folder, FblBinding binding, IPersistencePlugin? plugin, FblOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(binding);
        if (!binding.Body.IsFolder) throw new ArgumentException($"The binding '{binding.Name}' reads a file, not a folder.", nameof(binding));
        return binding.Plugin is null
            ? throw new ArgumentException($"The binding '{binding.Name}' is read by its declared rules, not by a plugin.", nameof(binding))
            : new FolderBody(Path.GetFullPath(folder), binding, plugin, options ?? new FblOptions());
    }

    /// <summary>Reads the folder again, now (FBL §10.3).</summary>
    public void Reread() => _last = ReadNow();

    /// <summary>
    /// The plan for one model change. A read-only binding's plugin is never asked to plan (FBL §11.4);
    /// a plan the plugin does make is refused too, because this host does not yet write a folder
    /// subject's files (FBL §10.4).
    /// </summary>
    public PlanResult Plan(ModelChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (_plugin is null) return new PlanResult.Refused(MissingReason);
        if (_last.Unreadable) return new PlanResult.Refused("The folder could not be read, so it is never written.");
        if (Binding.ReadOnly is { } reason) return new PlanResult.Refused(reason.Length > 0 ? reason : "This folder is read-only.");
        return _plugin.Plan(new PluginPlanRequest(_files, _last, change)) switch
        {
            PluginPlanResult.Refused refused => new PlanResult.Refused(refused.Reason),
            _ => new PlanResult.Refused("The files of a folder are not written by this host, so the change is not applied."),
        };
    }

    private string MissingReason => $"The plugin '{Binding.Plugin!.Plugin}' that reads this folder is not installed, so it is opened read-only.";

    private PluginReadResult ReadNow()
    {
        _files = [];
        var plugin = _plugin;
        try
        {
            if (!FolderSubject.Recognise(Binding, Folder)) return Unreadable(Directory.Exists(Folder) ? "The folder is not one this binding recognises, so it is not read." : "The folder no longer exists, so it is not read.");
            if (plugin is null) return new PluginReadResult([], [new Finding(FindingCodes.PluginMissing, FindingSeverity.Warning, MissingReason, null)], false);
            _files = FolderSubject.Files(Binding, Folder).Select(Load).ToList();
        }
        catch (DirectoryNotFoundException)
        {
            _files = [];
            return Unreadable("The folder no longer exists, so it is not read.");
        }

        var entries = _options.SupplementaryEntries?.Invoke(Folder).Order(StringComparer.Ordinal).ToList() ?? [];
        var result = plugin.Read(new PluginReadRequest(_files) { SupplementaryEntries = entries });
        return result with { Findings = Located(result) };
    }

    /// <summary>Reads at the sharing the repository's own reader uses, so a concurrent save is never refused; a file over the limit is not read at all.</summary>
    private PluginFile Load(FolderFile file)
    {
        using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > _options.MaxBodyBytes) return new PluginFile(file.RelativePath, []) { IsOversized = true };
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new PluginFile(file.RelativePath, buffer.ToArray());
    }

    private static PluginReadResult Unreadable(string reason) =>
        new([], [new Finding(FindingCodes.Unparseable, FindingSeverity.Error, reason, null)], true);

    /// <summary>
    /// A finding the plugin left without a file, about an element it read in one (its
    /// <see cref="SubjectDetail"/>), is located in that element's file, at the finding's line or else the element's.
    /// </summary>
    private static IReadOnlyList<Finding> Located(PluginReadResult result) =>
        result.Findings.Select(finding =>
        {
            if (finding.Location is { File.Length: > 0 }) return finding;
            if (finding.Detail?.GetValueOrDefault(SubjectDetail) is not { } subject) return finding;
            if (result.Elements.FirstOrDefault(e => e.Id == subject) is not { File.Length: > 0 } element) return finding;
            var location = finding.Location is { } given ? given with { File = element.File } : new SourceLocation(element.File, element.Line, 1, 0);
            return finding with { Location = location };
        }).ToList();
}
