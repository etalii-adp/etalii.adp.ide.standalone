using System.Text.Json;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Common;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Where ADP keeps the identities the `.owm` format does not provide. A sidecar rather than the
/// `.owm` itself, because that file belongs to another ecosystem and ADP's own data has no
/// place in it - tech.md's rule, Requirement 3.7, and the precedent C4LayoutSidecar set for `.dsl`.
/// </summary>
/// <remarks>
/// <para>
/// <b>An optimisation, never a dependency</b> (Requirement 4.5). A missing, unreadable or
/// nonsense sidecar means identities are re-derived from the document; a map that would not
/// open because its bookkeeping file is corrupt would be a poor trade. Nothing here throws for
/// a bad file.
/// </para>
/// <para>
/// Why a per-element structure here, when <c>mindmap-diagram</c> Requirement 3.5 puts ADP's own
/// data in the `.adp` file and insists a mindmap stays two files: a `.mm` node carries a native
/// <c>ID</c>, so the mindmap has identities to *use*. An `.owm` element has none at all, so this
/// module stores one for every element on the map - a growing structure, which is what the
/// sidecar rule exists for.
/// </para>
/// </remarks>
public sealed class WardleyIdentities
{
    private static readonly ILogger _logger = Log.ForContext<WardleyIdentities>();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>The sidecar beside <paramref name="bodyPath"/> - the same base name, `.identities.json`.</summary>
    public static string PathFor(string bodyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);

        var directory = IoPath.GetDirectoryName(bodyPath) ?? "";
        var name = IoPath.GetFileNameWithoutExtension(bodyPath);
        return IoPath.Combine(directory, name + ".identities.json");
    }

    /// <summary>
    /// The recorded identities for <paramref name="bodyPath"/>, or none. Anything that cannot
    /// be read yields none rather than throwing.
    /// </summary>
    public IReadOnlyList<WardleyIdentityEntry> Read(string bodyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);

        var path = PathFor(bodyPath);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var entries = JsonSerializer.Deserialize<List<WardleyIdentityEntry>>(File.ReadAllText(path), Options);
            if (entries is null)
            {
                return [];
            }

            // A half-written entry is discarded rather than carried: an identity with no id, or
            // no key to match it by, cannot do the one job it exists for.
            return entries
                .Where(entry => entry is { Id.Length: > 0, Kind.Length: > 0, Key.Length: > 0 })
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Warning(exception, "Could not read the identity sidecar beside {BodyPath}; re-deriving", bodyPath);
            return [];
        }
    }

    /// <summary>
    /// Records <paramref name="entries"/> beside <paramref name="bodyPath"/>, replacing whatever
    /// was there. Written atomically, so a reader never sees a half-written sidecar.
    /// </summary>
    /// <remarks>
    /// An empty list removes the sidecar rather than leaving an empty one behind: a map with no
    /// elements has no identities to keep, and a stray file would only invite the question of
    /// what it is for.
    /// </remarks>
    public string Write(string bodyPath, IReadOnlyList<WardleyIdentityEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(entries);

        var path = PathFor(bodyPath);
        if (entries.Count == 0)
        {
            Remove(bodyPath);
            return "";
        }

        // The folder is this sidecar's precondition rather than the writer's business: a
        // scratch file has nowhere to land if the directory is not there yet.
        var folder = IoPath.GetDirectoryName(path);
        var directory = folder is { Length: > 0 } ? folder : ".";

        try
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            AdpFileWriter.Save(path, JsonSerializer.Serialize(entries, Options));
            return "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Reported rather than swallowed, though the edit still succeeds: failing it
            // would cost the user work they can see, while saying nothing let the loss pass
            // unnoticed. What is lost is recoverable but not free - Read answers an empty
            // list when the sidecar is gone and Reconcile mints fresh ids for anything it
            // does not recognise, so the map is intact and its elements are renamed
            // underneath any selection still pointing at the old ones.
            _logger.Warning(exception, "Could not write the identity sidecar beside {BodyPath}", bodyPath);

            // AdpFileWriter.Save removes its own scratch file before the failure surfaces.
            return $"The element identities beside {IoPath.GetFileName(bodyPath)} could not be saved, so this map's elements may be given new ids the next time it is opened.";
        }
    }

    /// <summary>
    /// Matches recorded identities to the elements of <paramref name="map"/>, assigning a fresh
    /// <c>ShortGuid</c> to anything unmatched and discarding any entry whose element is gone
    /// (Requirements 4.3, 4.5).
    /// </summary>
    /// <returns>
    /// The complete current set, in a stable order, ready to persist on the next save. Nothing
    /// is written here: a map opened and not edited must produce no write at all.
    /// </returns>
    public static IReadOnlyList<WardleyIdentityEntry> Reconcile(
        WardleyMap map,
        IReadOnlyList<WardleyIdentityEntry> recorded)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(recorded);

        // Keyed by kind and key together, because two elements of different kinds may share a
        // key - a component and a note can hold the same text.
        var known = new Dictionary<(string Kind, string Key), string>();
        foreach (var entry in recorded)
        {
            known.TryAdd((entry.Kind, entry.Key), entry.Id);
        }

        var reconciled = new List<WardleyIdentityEntry>();

        foreach (var component in map.Components)
        {
            Take(WardleyIdentityKind.Component, WardleyIdentityKeys.Of(component));
        }

        foreach (var link in map.Links)
        {
            Take(WardleyIdentityKind.Link, WardleyIdentityKeys.Of(link));
        }

        foreach (var pipeline in map.Pipelines)
        {
            Take(WardleyIdentityKind.Pipeline, WardleyIdentityKeys.Of(pipeline));
            foreach (var child in pipeline.Children)
            {
                Take(WardleyIdentityKind.PipelineChild, WardleyIdentityKeys.Of(pipeline, child));
            }
        }

        foreach (var note in map.Notes)
        {
            Take(WardleyIdentityKind.Note, WardleyIdentityKeys.Of(note));
        }

        foreach (var annotation in map.Annotations)
        {
            Take(WardleyIdentityKind.Annotation, WardleyIdentityKeys.Of(annotation));
        }

        foreach (var accelerator in map.Accelerators)
        {
            Take(WardleyIdentityKind.Accelerator, WardleyIdentityKeys.Of(accelerator));
        }

        foreach (var attitude in map.Attitudes)
        {
            Take(WardleyIdentityKind.Attitude, WardleyIdentityKeys.Of(attitude));
        }

        return reconciled.ToArray();

        // An element the sidecar knows keeps its id; one it does not gets a fresh one. An entry
        // no element claimed is simply never taken, which is how a stale one is discarded.
        void Take(string kind, string key)
        {
            var id = known.TryGetValue((kind, key), out var existing)
                ? existing
                : ShortGuid.NewShortGuid().ToString();

            reconciled.Add(new WardleyIdentityEntry(id, kind, key));
        }
    }

    /// <summary>Deletes the sidecar, if there is one. Used when a map has no identities left to keep.</summary>
    public void Remove(string bodyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        TryDelete(PathFor(bodyPath));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not remove {Path}", path);
        }
    }
}
