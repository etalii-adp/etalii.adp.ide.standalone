using System.Text.Json;
using Serilog;

namespace EtAlii.Adp.C4;

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
/// An optimisation, never a dependency (Requirement 3.6). A missing, unreadable or nonsense
/// sidecar means the computed layout stands - a diagram that will not open because its cosmetic
/// file is corrupt would be a poor trade.
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

    /// <summary>One element's authored position, in canvas units.</summary>
    public sealed record Position(double X, double Y);

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
    public IReadOnlyDictionary<string, Position> Read(string bodyPath, string viewKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(viewKey);

        var all = ReadAll(bodyPath);
        return all.TryGetValue(viewKey, out var positions) ? positions : new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Records one element's position on one view, leaving every other view's alone.</summary>
    public void Write(string bodyPath, string viewKey, string elementId, Position position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(viewKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        ArgumentNullException.ThrowIfNull(position);

        var all = new Dictionary<string, Dictionary<string, Position>>(ReadAll(bodyPath).ToDictionary(
            pair => pair.Key,
            pair => new Dictionary<string, Position>(pair.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        if (!all.TryGetValue(viewKey, out var view))
        {
            view = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase);
            all[viewKey] = view;
        }

        view[elementId] = position;

        try
        {
            File.WriteAllText(PathFor(bodyPath), JsonSerializer.Serialize(all, Options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Losing an arrangement is a nuisance; failing the edit that caused it would be
            // worse, and the computed layout still shows a correct diagram.
            _logger.Warning(exception, "Could not write the layout sidecar beside {BodyPath}", bodyPath);
        }
    }

    /// <summary>Forgets every authored position for one view - what "reset layout" would do.</summary>
    public void Clear(string bodyPath, string viewKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);
        ArgumentNullException.ThrowIfNull(viewKey);

        var all = ReadAll(bodyPath).Where(pair => !pair.Key.Equals(viewKey, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        try
        {
            if (all.Count == 0)
            {
                if (File.Exists(PathFor(bodyPath)))
                {
                    File.Delete(PathFor(bodyPath));
                }

                return;
            }

            File.WriteAllText(PathFor(bodyPath), JsonSerializer.Serialize(all, Options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not clear the layout sidecar beside {BodyPath}", bodyPath);
        }
    }

    private static Dictionary<string, IReadOnlyDictionary<string, Position>> ReadAll(string bodyPath)
    {
        var empty = new Dictionary<string, IReadOnlyDictionary<string, Position>>(StringComparer.OrdinalIgnoreCase);
        var path = PathFor(bodyPath);
        if (!File.Exists(path))
        {
            return empty;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Position>>>(File.ReadAllText(path), Options);
            if (parsed is null)
            {
                return empty;
            }

            return parsed.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<string, Position>)new Dictionary<string, Position>(pair.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Requirement 3.6: the sidecar is an optimisation. A corrupt one costs the
            // arrangement, not the diagram.
            _logger.Warning(exception, "Ignoring an unreadable layout sidecar beside {BodyPath}", bodyPath);
            return empty;
        }
    }
}
