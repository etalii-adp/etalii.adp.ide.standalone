using System.Text.Json;
using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using Serilog;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Where ADP keeps the positions a user arranged by hand. A sidecar rather than the `.dsl`,
/// because the DSL is a format another ecosystem owns and ADP's own data has no place in it -
/// tech.md's rule, and c4-diagrams Requirement 3.5.
/// </summary>
/// <remarks>
/// <para>
/// Per model document, keyed by view: positions belong to a view, and a view belongs to a
/// document, so one file beside the `.dsl` holds them all.
/// </para>
/// <para>
/// On <b>opening</b>, an optimisation, never a dependency (Requirement 3.6): a missing,
/// unreadable or nonsense sidecar means the computed layout stands - a diagram that will not
/// open because its cosmetic file is corrupt would be a poor trade. That requirement covers
/// the open path only. The <b>modify</b> path holds the opposite discipline: every update is
/// read-modify-write over the whole file, so proceeding after a read that was merely refused
/// this instant would persist an empty arrangement over every other view's - the positions
/// Requirement 8.3 says survive reopening. A refused read therefore refuses the modify.
/// </para>
/// </remarks>
public sealed class C4LayoutSidecar
{
    private static readonly ILogger _logger = Log.ForContext<C4LayoutSidecar>();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>The sidecar beside <paramref name="bodyPath"/> - the same base name, `.layout.json`.</summary>
    public static string PathFor(string bodyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);

        var directory = System.IO.Path.GetDirectoryName(bodyPath) ?? "";
        var name = System.IO.Path.GetFileNameWithoutExtension(bodyPath);
        return System.IO.Path.Combine(directory, name + ".layout.json");
    }

    /// <summary>
    /// The authored positions for <paramref name="viewKey"/>, or none. Anything that cannot be
    /// read yields none rather than throwing.
    /// </summary>
    public IReadOnlyDictionary<string, C4SidecarPosition> Read(string bodyPath, string viewKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(viewKey);

        var all = ReadAll(bodyPath);
        return all.TryGetValue(viewKey, out var positions) ? positions : new Dictionary<string, C4SidecarPosition>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Records one element's position on one view, leaving every other view's alone.</summary>
    public string Write(string bodyPath, string viewKey, string elementId, C4SidecarPosition position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(viewKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        ArgumentNullException.ThrowIfNull(position);

        if (!TryReadAllForModify(bodyPath, out var all))
        {
            // False here means the sidecar exists and could not be read - a missing one
            // reads as an empty layout and succeeds - so the change is not going to land.
            return Unreadable(bodyPath);
        }

        if (!all.TryGetValue(viewKey, out var view))
        {
            view = new Dictionary<string, C4SidecarPosition>(StringComparer.OrdinalIgnoreCase);
            all[viewKey] = view;
        }

        view[elementId] = position;
        return Persist(bodyPath, all);
    }

    /// <summary>Forgets one element's authored position, handing it back to the layout.</summary>
    public string Remove(string bodyPath, string viewKey, string elementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(viewKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);

        if (!TryReadAllForModify(bodyPath, out var all))
        {
            // False here means the sidecar exists and could not be read - a missing one
            // reads as an empty layout and succeeds - so the change is not going to land.
            return Unreadable(bodyPath);
        }

        foreach (var view in all.Values)
        {
            view.Remove(elementId);
        }

        return Persist(bodyPath, all);
    }

    /// <summary>Forgets every authored position for one view - what "reset layout" would do.</summary>
    public string Clear(string bodyPath, string viewKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(viewKey);

        if (!TryReadAllForModify(bodyPath, out var all))
        {
            // False here means the sidecar exists and could not be read - a missing one
            // reads as an empty layout and succeeds - so the change is not going to land.
            return Unreadable(bodyPath);
        }

        all.Remove(viewKey);

        if (all.Count == 0)
        {
            try
            {
                if (File.Exists(PathFor(bodyPath)))
                {
                    File.Delete(PathFor(bodyPath));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(exception, "Could not clear the layout sidecar beside {BodyPath}", bodyPath);
                return Unreadable(bodyPath);
            }

            return "";
        }

        return Persist(bodyPath, all);
    }

    /// <summary>
    /// The whole sidecar for the open path. Requirement 3.6 governs here: anything that cannot
    /// be read - absent, locked or nonsense alike - yields none, so the diagram still opens on
    /// the computed layout.
    /// </summary>
    private static Dictionary<string, IReadOnlyDictionary<string, C4SidecarPosition>> ReadAll(string bodyPath)
    {
        var empty = new Dictionary<string, IReadOnlyDictionary<string, C4SidecarPosition>>(StringComparer.OrdinalIgnoreCase);
        var path = PathFor(bodyPath);
        if (!File.Exists(path))
        {
            return empty;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, C4SidecarPosition>>>(File.ReadAllText(path), Options);
            if (parsed is null)
            {
                return empty;
            }

            return parsed.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<string, C4SidecarPosition>)new Dictionary<string, C4SidecarPosition>(pair.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Warning(exception, "Ignoring an unreadable layout sidecar beside {BodyPath}", bodyPath);
            return empty;
        }
    }

    /// <summary>
    /// The whole sidecar for a modify, distinguishing the two ways a read fails. False - do
    /// not write - when the file is there but could not be read this instant (a lock, a
    /// permission): its arrangements still exist, and rewriting from the nothing that was read
    /// would destroy them; the refused caller loses one drag's position, not every view's.
    /// True with empty content when the file is absent or its content is already nonsense
    /// (JsonException): proceeding then costs nothing that still exists, which keeps a corrupt
    /// sidecar self-healing on the next drag.
    /// </summary>
    private static bool TryReadAllForModify(string bodyPath, out Dictionary<string, Dictionary<string, C4SidecarPosition>> all)
    {
        all = new Dictionary<string, Dictionary<string, C4SidecarPosition>>(StringComparer.OrdinalIgnoreCase);
        var path = PathFor(bodyPath);
        if (!File.Exists(path))
        {
            return true;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Not updating the layout sidecar beside {BodyPath}: it could not be read right now", bodyPath);
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, C4SidecarPosition>>>(text, Options);
            if (parsed is not null)
            {
                foreach (var pair in parsed)
                {
                    all[pair.Key] = new Dictionary<string, C4SidecarPosition>(pair.Value, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (JsonException exception)
        {
            _logger.Warning(exception, "Replacing the unparseable layout sidecar beside {BodyPath}", bodyPath);
            all.Clear();
        }

        return true;
    }

    /// <summary>
    /// Temp-then-move in the same folder, as every other write in ADP: a reader sees the old
    /// arrangement or the new one, never a half-written file. The scratch name matches the
    /// pattern the hierarchy watcher already ignores.
    /// </summary>
    /// <summary>
    /// Writes the sidecar, answering <c>""</c> when it landed and a sentence when it did not.
    /// </summary>
    /// <remarks>
    /// Reported rather than swallowed. Losing an arrangement is a nuisance and failing the
    /// edit that caused it would be worse - the document is written and the computed layout
    /// still draws a correct diagram - but saying nothing meant a drag silently did not
    /// persist, and the user found out by reopening. The caller succeeds AND passes this on.
    /// </remarks>
    /// <summary>What the caller says when the layout could not be updated at all.</summary>
    private static string Unreadable(string bodyPath) =>
        $"The layout beside {System.IO.Path.GetFileName(bodyPath)} could not be updated, so positions may not be as expected when the diagram is reopened.";

    private static string Persist(string bodyPath, Dictionary<string, Dictionary<string, C4SidecarPosition>> all)
    {
        var path = PathFor(bodyPath);
        try
        {
            AdpFileWriter.Save(path, JsonSerializer.Serialize(all, Options));
            return "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not write the layout sidecar beside {BodyPath}", bodyPath);

            // AdpFileWriter.Save removes its own scratch file before the failure surfaces.
            return $"The new position could not be saved beside {System.IO.Path.GetFileName(bodyPath)}, so it will not be there when the diagram is reopened.";
        }
    }

}
