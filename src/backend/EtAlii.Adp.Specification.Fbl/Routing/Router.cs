using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;

namespace EtAlii.Adp.Specification.Fbl.Routing;

/// <summary>
/// Routing (FBL §12.3) and offering readings (FBL §9.4). The router returns every candidate and
/// never chooses between several on the caller's behalf.
/// </summary>
public static class Router
{
    /// <summary>The part of a body <c>suggest</c> looks at (FBL §12.1).</summary>
    private const int SuggestBytes = 64 * 1024;

    /// <summary>
    /// The bindings a bare file routes to (FBL §12.3): those whose <c>names</c> match the file name or
    /// whose <c>extensions</c> include its extension, ignoring case; never a <c>registrationOnly</c>
    /// binding; and a binding with a marker only when the marker matches, which FBL §12.1 calls the
    /// mark a file needs before the binding claims it.
    /// </summary>
    public static IReadOnlyList<FblBinding> Candidates(string fileName, byte[] bytes, IEnumerable<FblBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(bindings);
        var name = Path.GetFileName(fileName);
        var extension = Path.GetExtension(name);
        var candidates = new List<FblBinding>();
        foreach (var binding in bindings)
        {
            var claims = binding.Claims;
            if (claims.RegistrationOnly) continue;
            var named = claims.Names.Any(glob => Glob.IsMatch(glob, name, Glob.PlatformIgnoresCase));
            var extended = extension.Length > 0 && claims.Extensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
            if (!named && !extended) continue;
            if (claims.Marker is { } marker && !MarkerEvaluator.Matches(marker, bytes)) continue;
            if (claims is { Shared: true, Marker: null }) continue;
            candidates.Add(binding);
        }
        return candidates;
    }

    /// <summary>
    /// The readings a binding offers for a body (FBL §9.4): each origin in <c>claims.origins</c> order,
    /// those whose reading's <c>suggest</c> matches the body first.
    /// </summary>
    public static IReadOnlyList<string> Readings(FblBinding binding, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var origins = binding.Claims.Origins;
        var suggested = origins.Where(o => binding.Claims.Readings.TryGetValue(o, out var reading) && Contains(bytes, reading.Suggest)).ToList();
        return suggested.Concat(origins.Except(suggested)).ToList();
    }

    /// <summary>The reading a bare file opens as (FBL §12.3): the one marked <c>bare</c>, else the binding's only origin, else none.</summary>
    public static string? Bare(FblBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var bare = binding.Claims.Readings.FirstOrDefault(r => r.Value.Bare).Key;
        return bare ?? (binding.Claims.Origins.Count == 1 ? binding.Claims.Origins[0] : null);
    }

    /// <summary>Whether a reading's <c>suggest</c> matches the body (FBL §9.4).</summary>
    public static bool SuggestsReading(FblBinding binding, string origin, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return binding.Claims.Readings.TryGetValue(origin, out var reading) && Contains(bytes, reading.Suggest);
    }

    private static bool Contains(byte[] bytes, IReadOnlyList<string> substrings)
    {
        if (substrings.Count == 0) return false;
        var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, SuggestBytes));
        return substrings.Any(s => head.Contains(s, StringComparison.Ordinal));
    }
}
