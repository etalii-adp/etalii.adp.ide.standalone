using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Routing;

namespace EtAlii.Adp.Specification.Fbl.Plugins;

/// <summary>
/// A folder subject read by a persistence plugin (FBL §10, §11.2): the folder qualifies by the
/// binding's <c>recognise</c>, its files are selected by the file rules and <c>ignore</c> in ordinal
/// order of relative path and never through a reparse point (<see cref="FolderSubject"/>), and all of
/// them are handed to the plugin in one <c>read</c>. A folder that fails <c>recognise</c> or is gone
/// is an unreadable body (FBL §10.3); a selected file that cannot be opened is DISL's
/// <c>std.unparseable</c> at that file, and the rest is read; without the plugin the folder opens read-only with DISL's
/// <c>std.pluginMissing</c>, as <see cref="PluginBody"/> does (FBL §15.1).
/// <para>
/// While it is open, the host watches what <see cref="Dependencies"/> names and passes every changed
/// path to <see cref="Notify"/>; the library opens no watcher. Once no change has arrived for the
/// binding's <c>settle</c> milliseconds the folder is read again, once however many files changed,
/// and <see cref="Changed"/> is raised. A burst that touched only the registration raises it without
/// reading the folder (FBL §10.3).
/// </para>
/// </summary>
public sealed class FolderBody : IDisposable
{
    /// <summary>The key of a finding's <see cref="Finding.Detail"/> naming the element it is about, DISL's <c>subject</c> (FBL §11.4).</summary>
    public const string SubjectDetail = "subject";

    private static readonly StringComparer Paths = Glob.PlatformIgnoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison = Glob.PlatformIgnoresCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly IPersistencePlugin? _plugin;
    private readonly FblOptions _options;
    private readonly string? _registration;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private PluginReadResult _last;
    private IReadOnlyList<PluginFile> _files = [];
    private IReadOnlyList<string> _unopened = [];
    private IReadOnlyList<string> _watched = [];
    private ITimer? _timer;
    private bool _folderChanged;
    private bool _registrationChanged;
    private bool _disposed;

    private FolderBody(string folder, FblBinding binding, IPersistencePlugin? plugin, FblOptions options, string? registration, TimeProvider time)
    {
        Folder = folder;
        Binding = binding;
        _options = options;
        _registration = registration;
        _time = time;
        _plugin = plugin is not null && plugin.Id == binding.Plugin!.Plugin ? plugin : null;
        _last = ReadNow();
    }

    /// <summary>
    /// Raised once a burst of changes has settled: after the folder was read again, or, when the burst
    /// touched only the registration, without a read, so that every open reading shows what was written there.
    /// </summary>
    public event EventHandler<FolderBodyChange>? Changed;

    /// <summary>The folder's full path.</summary>
    private string Folder { get; }

    private FblBinding Binding { get; }

    public FblModel Model => new(_last.Elements, _last.Findings, _last.Unreadable);

    /// <summary>Read-only without the plugin, when the folder is unreadable, or when the binding is read-only.</summary>
    public bool IsReadOnly => _plugin is null || _last.Unreadable || Binding.ReadOnly is not null;

    /// <summary>
    /// Opens a folder subject whose binding names a plugin. <paramref name="plugin"/> is the one the
    /// caller has installed, or null; a plugin with another id counts as missing. <paramref name="registration"/>
    /// is the path of the folder's registration, whether or not it exists yet, and <paramref name="time"/>
    /// the clock of the settle delay, the system's unless a test passes another.
    /// </summary>
    public static FolderBody Open(string folder, FblBinding binding, IPersistencePlugin? plugin, FblOptions? options = null, string? registration = null, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(binding);
        if (!binding.Body.IsFolder) throw new ArgumentException($"The binding '{binding.Name}' reads a file, not a folder.", nameof(binding));
        return binding.Plugin is null
            ? throw new ArgumentException($"The binding '{binding.Name}' is read by its declared rules, not by a plugin.", nameof(binding))
            : new FolderBody(Path.GetFullPath(folder), binding, plugin, options ?? new FblOptions(), registration is null ? null : Path.GetFullPath(registration), time ?? TimeProvider.System);
    }

    /// <summary>Reads a folder once, for a caller that keeps nothing open: nothing is watched or pending afterwards.</summary>
    public static FblModel ReadOnce(string folder, FblBinding binding, IPersistencePlugin? plugin, FblOptions? options = null)
    {
        using var body = Open(folder, binding, plugin, options);
        return body.Model;
    }

    /// <summary>Reads the folder again, now (FBL §10.3).</summary>
    public void Reread() => _last = ReadNow();

    /// <summary>
    /// The full paths the reading depends on, for the host to watch (FBL §10.3, §11.2): the files read
    /// and those selected that could not be opened, the folders the file rules' globs could add files to,
    /// what the plugin's <c>watch</c> adds, and the registration. In ordinal order, each once.
    /// </summary>
    public IReadOnlyList<string> Dependencies
    {
        get
        {
            var paths = new List<string>();
            paths.AddRange(_files.Select(file => Full(file.RelativePath)));
            paths.AddRange(_unopened.Select(Full));
            paths.AddRange(GlobFolders());
            paths.AddRange(_watched);
            if (_registration is not null) paths.Add(_registration);
            return paths.Distinct(Paths).Order(StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>
    /// Takes one path the host saw change: created, changed, deleted or renamed, file or folder. A path
    /// the reading does not depend on is ignored; any other starts, or starts again, the settle delay.
    /// </summary>
    public void Notify(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Path.GetFullPath(path, Folder);
        var registration = _registration is not null && Paths.Equals(full, _registration);
        if (!registration && !Concerns(full)) return;
        lock (_gate)
        {
            if (_disposed) return;
            if (registration) _registrationChanged = true;
            else _folderChanged = true;
            _timer ??= _time.CreateTimer(_ => Settled(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Change(TimeSpan.FromMilliseconds(Binding.Body.Settle), Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Stops the settle delay: a change notified before is never read, and one notified after is ignored.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Settled()
    {
        bool folder, registration;
        lock (_gate)
        {
            if (_disposed) return;
            (folder, registration) = (_folderChanged, _registrationChanged);
            (_folderChanged, _registrationChanged) = (false, false);
        }
        if (folder) Reread();
        if (folder || registration) Changed?.Invoke(this, new FolderBodyChange(folder, registration));
    }

    /// <summary>
    /// Whether a change at <paramref name="full"/> can change the reading: a file read, a folder holding
    /// one, a path a file rule or <c>recognise</c> selects or a folder that could hold one, or a path
    /// the plugin's <c>watch</c> named or one under it.
    /// </summary>
    private bool Concerns(string full)
    {
        if (_watched.Any(watched => Paths.Equals(full, watched) || full.StartsWith(watched + Path.DirectorySeparatorChar, PathComparison))) return true;
        var relative = Path.GetRelativePath(Folder, full);
        if (relative == ".") return true;
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return false;
        relative = relative.Replace(Path.DirectorySeparatorChar, '/');
        if (_files.Any(file => Paths.Equals(file.RelativePath, relative) || file.RelativePath.StartsWith(relative + "/", PathComparison))) return true;
        var caseless = Glob.PlatformIgnoresCase;
        if (Binding.Body.Ignore.Any(glob => Glob.IsMatch(glob, relative, caseless))) return false;
        return Globs().Any(glob => Glob.IsMatch(glob, relative, caseless) || CouldHold(glob, relative, caseless));
    }

    /// <summary>The globs whose matches change the reading: the file rules' and <c>recognise</c>'s.</summary>
    private IEnumerable<string> Globs() =>
        Binding.Body.Files.Select(rule => rule.Glob).Concat(Binding.Body.RecogniseAll).Concat(Binding.Body.RecogniseAny).Concat(Binding.Body.RecogniseNone);

    /// <summary>Whether a folder at <paramref name="relative"/> could hold a path <paramref name="glob"/> matches.</summary>
    private static bool CouldHold(string glob, string relative, bool caseless)
    {
        var pattern = glob.Split('/');
        var path = relative.Split('/');
        for (var i = 0; i < path.Length; i++)
        {
            if (i >= pattern.Length) return false;
            if (pattern[i] == "**") return true;
            if (i == pattern.Length - 1 || !Glob.IsMatch(pattern[i], path[i], caseless)) return false;
        }
        return true;
    }

    /// <summary>
    /// The existing folders a file rule's glob could add a file to: the folder itself, and every folder
    /// below it that could hold a match, never through a reparse point and never an ignored one.
    /// </summary>
    private IEnumerable<string> GlobFolders()
    {
        var found = new List<string> { Folder };
        if (!Directory.Exists(Folder)) return found;
        var caseless = Glob.PlatformIgnoresCase;
        var globs = Binding.Body.Files.Select(rule => rule.Glob).ToList();
        try
        {
            Walk(Folder, "");
        }
        catch (DirectoryNotFoundException)
        {
            // Removed while walked: the change that removed it is notified, and read after it settles.
        }
        return found;

        void Walk(string directory, string prefix)
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateDirectories())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                var relative = prefix + entry.Name;
                if (Binding.Body.Ignore.Any(glob => Glob.IsMatch(glob, relative, caseless))) continue;
                if (!globs.Any(glob => CouldHold(glob, relative, caseless))) continue;
                found.Add(entry.FullName);
                Walk(entry.FullName, relative + "/");
            }
        }
    }

    private string Full(string relative) => Path.Combine(Folder, relative.Replace('/', Path.DirectorySeparatorChar));

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
        _unopened = [];
        _watched = [];
        var plugin = _plugin;
        var files = new List<PluginFile>();
        var unopened = new List<Finding>();
        try
        {
            if (!FolderSubject.Recognise(Binding, Folder)) return Unreadable(Directory.Exists(Folder) ? "The folder is not one this binding recognises, so it is not read." : "The folder no longer exists, so it is not read.");
            if (plugin is null) return new PluginReadResult([], [new Finding(FindingCodes.PluginMissing, FindingSeverity.Warning, MissingReason, null)], false);
            foreach (var file in FolderSubject.Files(Binding, Folder))
            {
                if (Load(file) is { } loaded) files.Add(loaded);
                else unopened.Add(Unopened(file.RelativePath));
            }
        }
        catch (DirectoryNotFoundException)
        {
            return Unreadable("The folder no longer exists, so it is not read.");
        }

        _files = files;
        _unopened = [.. unopened.Select(finding => finding.Location!.File)];
        var entries = _options.SupplementaryEntries?.Invoke(Folder).Order(StringComparer.Ordinal).ToList() ?? [];
        var result = plugin.Read(new PluginReadRequest(_files) { SupplementaryEntries = entries, Args = Binding.Plugin!.Args });
        _watched = plugin.Watch(result).Select(path => Path.GetFullPath(path, Folder)).ToList();
        return result with { Findings = [.. unopened, .. Located(result)] };
    }

    /// <summary>
    /// Reads at the sharing the repository's own reader uses, so a concurrent save is never refused; a file over
    /// the limit is not read at all. Null when the file cannot be opened or read: removed since it was
    /// selected, held by a process that shares nothing, or not permitted (FBL §10.3).
    /// </summary>
    private PluginFile? Load(FolderFile file)
    {
        try
        {
            using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > _options.MaxBodyBytes) return new PluginFile(file.RelativePath, []) { IsOversized = true };
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return new PluginFile(file.RelativePath, buffer.ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A selected file that could not be opened (FBL §10.3): DISL's <c>std.unparseable</c> at that file, at no
    /// line, since none of it was read. The sentence names no path of the machine, as an exception's would.
    /// </summary>
    private static Finding Unopened(string relativePath) =>
        new(FindingCodes.Unparseable, FindingSeverity.Error, "The file could not be opened, so it is not read.", new SourceLocation(relativePath, 0, 0, 0));

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

/// <summary>What a settled burst of changes changed: the folder, which was read again, the registration, or both.</summary>
public sealed record FolderBodyChange(bool FolderRead, bool Registration);
