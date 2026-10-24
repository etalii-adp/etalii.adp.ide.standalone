using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// Each example's layout, to the last rounded digit, as it was recorded: every box and every band.
/// </summary>
/// <remarks>
/// The other layout tests hold properties - columns, gaps, bands that run level - which several
/// layouts satisfy at once. This one holds the layout itself, so that a change meant to leave the
/// layout alone (spec 025, T241) is seen to. A change meant to move it records the new digests here.
/// </remarks>
public sealed class SankeyLayoutRecordedTests
{
    public static TheoryData<string, string> Recorded => new()
    {
        { SankeyExamples.BrightwaterCoffee, "C41C5A357A49B5C6" },
        { SankeyExamples.UkEnergy, "24A959E9C6730666" },
        { SankeyExamples.RecentGraduates, "783A5C79B0A4AF4A" },
    };

    [Theory]
    [MemberData(nameof(Recorded))]
    public void AnExample_IsLaidOutAsRecorded(string path, string digest)
    {
        // Act.
        var layout = SankeyLayout.Of(SankeyReader.Parse(File.ReadAllText(path)));
        var described = Describe(layout);

        // Assert.
        Assert.True(digest == Digest(described), $"{Path.GetFileName(path)} is laid out as {Digest(described)}:\n{described}");
    }

    private static string Describe(SankeyLayout layout)
    {
        var text = new StringBuilder();
        foreach (var node in layout.Nodes)
        {
            var box = layout.Boxes[node.Id];
            text.AppendLine(CultureInfo.InvariantCulture, $"node {node.Id} {layout.Columns[node.Id]} {box.X:R} {box.Y:R} {box.Width:R} {box.Height:R}");
        }

        foreach (var flow in layout.Flows)
        {
            var band = layout.Bands[flow.Id];
            text.AppendLine(CultureInfo.InvariantCulture, $"band {flow.Id} {band.Thickness:R} {band.SourceAt:R} {band.TargetAt:R} {band.Backward}");
        }

        return text.ToString();
    }

    private static string Digest(string described) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(described)))[..16];
}
