using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>
/// Each example's layout as it was recorded: every node, every group frame and every lane.
/// </summary>
/// <remarks>
/// The other layout tests hold properties - left to right, no overlap, groups around their members -
/// which several layouts satisfy at once. This one holds the layout itself, so that a change meant to
/// leave the layout alone (spec 025, T241) is seen to. A change meant to move it records the new
/// digests here.
/// </remarks>
public sealed class SupplyChainLayoutRecordedTests
{
    public static TheoryData<string, string> Recorded => new()
    {
        { SupplyChainExamples.Automotive, "CCF94134EA95039E" },
        { SupplyChainExamples.GpuMemory, "2CC15D22AF5E5B23" },
    };

    [Theory]
    [MemberData(nameof(Recorded))]
    public void AnExample_IsLaidOutAsRecorded(string path, string digest)
    {
        // Act.
        var layout = SupplyChainLayout.Of(SupplyChainReading.Parse(File.ReadAllText(path)));
        var described = Describe(layout);

        // Assert.
        Assert.True(digest == Digest(described), $"{Path.GetFileName(path)} is laid out as {Digest(described)}:\n{described}");
    }

    // Each line ends in \n, never Environment.NewLine: a digest above must be one number on every platform.
    private static string Describe(SupplyChainLayout layout)
    {
        var text = new StringBuilder();
        foreach (var node in layout.Nodes)
        {
            var box = layout.NodeBoxes[node.Id];
            text.Append(CultureInfo.InvariantCulture, $"node {node.Id} {box.X:R} {box.Y:R} {box.Width:R} {box.Height:R}\n");
        }

        foreach (var group in layout.Groups.Where(group => layout.GroupBoxes.ContainsKey(group.Id)))
        {
            var box = layout.GroupBoxes[group.Id];
            text.Append(CultureInfo.InvariantCulture, $"group {group.Id} {box.X:R} {box.Y:R} {box.Width:R} {box.Height:R}\n");
        }

        foreach (var flow in layout.Flows.Where(flow => layout.Lanes.ContainsKey(flow.Id)))
        {
            text.Append(CultureInfo.InvariantCulture, $"lane {flow.Id} {string.Join(" ", layout.Lanes[flow.Id].Select(point => FormattableString.Invariant($"{point.X:R},{point.Y:R}")))}\n");
        }

        return text.ToString();
    }

    private static string Digest(string described) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(described)))[..16];
}
