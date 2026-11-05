using System.Text.RegularExpressions;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// DISL reads a specification's JSON in its loader and nowhere else (spec 025, FR-079 and SC-019): no code line of
/// <c>EtAlii.Adp.Specification.Disl</c> outside <c>Loading/</c> names a <c>System.Text.Json</c> reading or one of the
/// helpers that make one.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it counts</b> is what the M6 review counted: a line naming <c>TryGetProperty</c>, <c>GetProperty</c>,
/// <c>JsonElement</c>, <c>ValueKind</c>, an <c>Enumerate…</c> or a <c>DislJson</c> helper, widened to the shared
/// helpers those forwarded to (<c>JsonProperties</c>, <c>JsonScalars</c>), to <c>JsonDocument</c> and to a
/// <c>using System.Text.Json</c>, so that a reading cannot come back under another name. Comment lines are left out:
/// a remark may say what System.Text.Json threw. <c>JsonPointer</c> is not a reading, only the place a diagnostic
/// names, and is allowed.
/// </para>
/// <para>
/// <b>Where a new reading goes</b>: into a reader under <c>Loading/</c>, as a member of the record the derivation,
/// the behaviour or the model reads while a diagram runs.
/// </para>
/// <para>
/// <b>Seen to fail</b> (spec 025, T305): before the task it failed naming 82 lines, the 58 the M6 review listed, the
/// <c>DislJson.Pointer</c> lines beside them and the <c>using</c>s; once they had moved, reading a type's label as
/// <c>JsonProperties.String(type.Json, "label")</c> again in <c>ToolboxDerivation</c> failed it naming exactly that line.
/// </para>
/// </remarks>
public class RawJsonOutsideLoadingTests
{
    private static readonly Regex RawJson = new(
        @"\b(TryGetProperty|GetProperty|JsonElement|ValueKind|Enumerate[A-Z]\w*|DislJson|JsonProperties|JsonScalars|JsonDocument)\b|\busing\s+System\.Text\.Json\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] SkippedFolders = ["bin", "obj"];

    [Fact]
    public void NoCodeOutsideTheLoaderReadsTheSpecificationsJson()
    {
        // Arrange.
        var project = RepositoryPaths.Under("src", "backend", "EtAlii.Adp.Specification.Disl");
        var loading = Path.Combine(project, "Loading") + Path.DirectorySeparatorChar;
        var files = SourceFiles(project).Where(file => !file.StartsWith(loading, StringComparison.Ordinal)).ToList();

        // Act.
        var lines = new List<string>();
        foreach (var file in files)
        {
            var number = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal) || !RawJson.IsMatch(line)) continue;
                lines.Add($"{Path.GetRelativePath(project, file)}:{number}: {line.Trim()}");
            }
        }

        // Assert.
        Assert.NotEmpty(files);
        Assert.True(lines.Count == 0, $"{lines.Count} line(s) outside Loading/ read JSON; read it in a reader under Loading/ instead:\n" + string.Join("\n", lines));
    }

    private static IEnumerable<string> SourceFiles(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs")) yield return file;
        foreach (var child in Directory.EnumerateDirectories(folder))
        {
            if (SkippedFolders.Contains(Path.GetFileName(child))) continue;
            foreach (var file in SourceFiles(child)) yield return file;
        }
    }
}
