using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.RealFiles;

/// <summary>
/// A place where a vendored binding and a real file disagree (Requirement 12): the property it
/// breaks, the binding and file, what was observed exactly, and why it is so.
/// </summary>
internal sealed record Divergence(string Property, string Binding, string File, string Observed, string Reason);

/// <summary>
/// <c>RealFiles/divergences.json</c>: every disagreement the real-file suite found, recorded instead
/// of hidden. A disagreement that is not listed fails its test; a listed one whose observation has
/// changed, or that no longer occurs, fails too, so the list cannot go stale.
/// </summary>
internal static class Divergences
{
    private static readonly Lazy<IReadOnlyList<Divergence>> _all = new(Load);

    public static IReadOnlyList<Divergence> All => _all.Value;

    private static string FilePath => Path.Combine(Repository.Root, "src", "backend", "EtAlii.Adp.Specification.Fbl.Tests", "RealFiles", "divergences.json");

    /// <summary>
    /// Checks one property on one file: <paramref name="observed"/> is null when the binding and the
    /// file agree, else the exact disagreement, which must then be listed with that observation.
    /// </summary>
    public static void Check(string property, string binding, string file, string? observed)
    {
        var listed = All.FirstOrDefault(d => d.Property == property && d.Binding == binding && d.File == file);
        if (observed is null)
        {
            Assert.True(listed is null, $"The divergence listed for {property} on {file} ({binding}) no longer occurs; remove it from divergences.json.");
            return;
        }
        Assert.True(listed is not null, $"{property} diverges on {file} ({binding}) and is not listed in divergences.json:\n{observed}");
        Assert.True(listed.Observed == observed, $"The divergence of {property} on {file} ({binding}) has changed.\nListed:   {listed.Observed}\nObserved: {observed}");
    }

    private static IReadOnlyList<Divergence> Load()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(FilePath));
        return document.RootElement.GetProperty("divergences").EnumerateArray()
            .Select(d => new Divergence(
                d.GetProperty("property").GetString()!,
                d.GetProperty("binding").GetString()!,
                d.GetProperty("file").GetString()!,
                d.GetProperty("observed").GetString()!,
                d.GetProperty("reason").GetString()!))
            .ToList();
    }
}
