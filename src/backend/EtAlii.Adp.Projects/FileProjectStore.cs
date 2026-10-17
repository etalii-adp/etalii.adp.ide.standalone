using System.Text.Json;
using EtAlii.Adp.Files;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Projects;

/// <summary>
/// Per-user project list persisted as a plain JSON file at
/// {appDataRoot}/EtAlii.Adp/users/{userId}/projects.json, per tech.md's
/// file-based (no database) storage philosophy. ProjectRecord (and its
/// nested PathRecord) is serialized directly - no separate DTO - relying on
/// ShortGuid's own JsonConverter (ShortGuidJsonConverter) for its Id.
/// </summary>
/// <remarks>
/// A user's list is read and parsed once and kept until it changes: every request that names a
/// project asks for it, so parsing the file each time is most of what resolving a project costs
/// (code-structure FR-062). The store's own writes drop the kept list; a change made to the file
/// by anything else is noticed by its time and size, which are compared on every read. Add and
/// Remove read, change and write the list under one lock, so two at once cannot both start from
/// the same list and lose one another's change.
/// </remarks>
public sealed class FileProjectStore : IProjectStore
{
    private static readonly ILogger _logger = Log.ForContext<FileProjectStore>();

    private readonly string _appDataRoot;

    private readonly Lock _gate = new();
    private readonly Dictionary<ShortGuid, CachedList> _cache = [];

    public FileProjectStore(string appDataRoot)
    {
        _appDataRoot = appDataRoot;
    }

    public IReadOnlyList<ProjectRecord> List(ShortGuid userId)
    {
        lock (_gate)
        {
            return Read(userId);
        }
    }

    public ProjectRecord Add(ShortGuid userId, string name, PathRecord path)
    {
        var folderPath = path.Segments.AbsolutePath();
        if (!Directory.Exists(folderPath))
        {
            throw new InvalidProjectPathException($"Folder not found: {folderPath}");
        }

        var resolvedName = string.IsNullOrWhiteSpace(name) ? new DirectoryInfo(folderPath).Name : name.Trim();
        var record = new ProjectRecord(ShortGuid.NewShortGuid(), resolvedName, path);
        lock (_gate)
        {
            Write(userId, [.. Read(userId), record]);
        }
        return record;
    }

    public void Remove(ShortGuid userId, ShortGuid projectId)
    {
        lock (_gate)
        {
            Write(userId, [.. Read(userId).Where(p => p.Id != projectId)]);
        }
    }

    private string GetFilePath(ShortGuid userId) =>
        IoPath.Combine(_appDataRoot, "EtAlii.Adp", "users", userId.ToString(), "projects.json");

    /// <summary>The user's list, from the kept copy while the file is unchanged. Called under <see cref="_gate"/>.</summary>
    private IReadOnlyList<ProjectRecord> Read(ShortGuid userId)
    {
        var filePath = GetFilePath(userId);
        var file = new FileInfo(filePath);
        if (!file.Exists)
        {
            // Ordinary for a user who has not added a project yet, so not worth a warning.
            _logger.Verbose("No project list yet for {UserId} at {StorePath}", userId, filePath);
            _cache.Remove(userId);
            return [];
        }

        if (_cache.TryGetValue(userId, out var cached) && cached.LastWriteTimeUtc == file.LastWriteTimeUtc && cached.Length == file.Length)
        {
            return cached.Projects;
        }

        var json = File.ReadAllText(filePath);
        try
        {
            ProjectRecord[] projects = [.. JsonSerializer.Deserialize<List<ProjectRecord>>(json) ?? []];
            _cache[userId] = new CachedList(file.LastWriteTimeUtc, file.Length, projects);
            return projects;
        }
        catch (JsonException exception)
        {
            // Left to propagate as before - the point of the log is that the file on disk,
            // not the request, is what is broken, which the caller's error will not say.
            _logger.Error(exception, "The project list at {StorePath} is not valid JSON", filePath);
            throw;
        }
    }

    private void Write(ShortGuid userId, IReadOnlyList<ProjectRecord> projects)
    {
        var filePath = GetFilePath(userId);
        Directory.CreateDirectory(IoPath.GetDirectoryName(filePath)!);

        var json = JsonSerializer.Serialize(projects, new JsonSerializerOptions { WriteIndented = true });
        _cache.Remove(userId);
        AdpFileWriter.Save(filePath, json);
        _logger.Debug("Wrote {Count} projects for {UserId} to {StorePath}", projects.Count, userId, filePath);
    }

    /// <summary>A user's parsed list, with the time and size of the file it was read from.</summary>
    private sealed record CachedList(DateTime LastWriteTimeUtc, long Length, ProjectRecord[] Projects);
}
