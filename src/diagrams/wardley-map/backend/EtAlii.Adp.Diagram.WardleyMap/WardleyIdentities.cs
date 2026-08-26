using System.Text.Json;
using EtAlii.Adp.Backend.Hierarchy;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Where ADP keeps the identities the `.owm` format does not provide. A sidecar rather than the
/// `.owm` itself, because that file belongs to another ecosystem and ADP's own data has no
/// place in it - tech.md's rule, Requirement 3.7, and the precedent
/// <see cref="C4LayoutSidecar"/> set for `.dsl`.
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
    public void Write(string bodyPath, IReadOnlyList<WardleyIdentityEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(entries);

        var path = PathFor(bodyPath);
        if (entries.Count == 0)
        {
            Remove(bodyPath);
            return;
        }

        var folder = IoPath.GetDirectoryName(path);
        var directory = folder is { Length: > 0 } ? folder : ".";
        var temporary = IoPath.Combine(
            directory,
            $"{AdpFileWriter.TempPrefix}{Guid.NewGuid():N}{AdpFileWriter.TempExtension}");

        try
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporary, JsonSerializer.Serialize(entries, Options));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Losing the sidecar costs identities on the next open, which re-derives them.
            // Failing the edit that caused this would cost the user's actual work.
            _logger.Warning(exception, "Could not write the identity sidecar beside {BodyPath}", bodyPath);
            TryDelete(temporary);
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
