using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Tests;

public class PathTruncatorTests
{
    private static readonly string Sep = IoPath.DirectorySeparatorChar.ToString();

    [Fact]
    public void Truncate_WithEmptySegments_ReturnsEmptyString()
    {
        // Arrange, act and assert.
        Assert.Equal(string.Empty, PathTruncator.Truncate(Array.Empty<string>()));
    }

    [Fact]
    public void Truncate_WhenFullPathFitsWithinMaxLength_ReturnsItUnchanged()
    {
        // Arrange.
        var segments = new[] { "C:", "git", "project" };
        var full = string.Join(Sep, segments);

        // Act.
        var result = PathTruncator.Truncate(segments, maxLength: full.Length);

        // Assert.
        Assert.Equal(full, result);
    }

    [Fact]
    public void Truncate_WhenFullPathExceedsMaxLength_DropsMiddleSegmentsAndKeepsFirstAndLast()
    {
        // Arrange.
        var segments = new[]
        {
            "C:", "Users", "developer", "source", "repos", "EtAlii.Adp", "src", "backend", "deeply-nested-project-folder",
        };

        // Act.
        var result = PathTruncator.Truncate(segments, maxLength: 40);

        // Assert.
        Assert.True(result.Length <= 40);
        Assert.StartsWith("C:" + Sep, result);
        Assert.EndsWith(Sep + "deeply-nested-project-folder", result);
        Assert.Contains("...", result);
    }

    [Fact]
    public void Truncate_PrefersTheSplitThatKeepsTheEllipsisClosestToCenter_EvenWhenGrowingTheFrontFirstWouldAlsoFit()
    {
        // Arrange.
        // The middle segment is made long enough that no split can ever include it, so
        // the only real choice is how much of {A, BBBBB} vs {D, E} to keep. Growing the
        // front greedily would settle on {A, BBBBB} + ... + {E} (a 7/1 char imbalance),
        // but {A} + ... + {D, E} keeps the same number of segments (3) with the head and
        // tail nearly equal (1/3) - the ellipsis lands much closer to the middle, which is
        // what this asserts.
        var segments = new[] { "A", "BBBBB", "middle-filler-segment", "D", "E" };

        // Act.
        var result = PathTruncator.Truncate(segments, maxLength: 13);

        // Assert.
        Assert.Equal($"A{Sep}...{Sep}D{Sep}E", result);
    }

    [Fact]
    public void Truncate_WithTwoSegmentsTooLongToFit_FallsBackToCharacterTruncation()
    {
        // Arrange.
        var segments = new[] { "C:", new string('a', 60) };

        // Act.
        var result = PathTruncator.Truncate(segments, maxLength: 20);

        // Assert.
        Assert.Equal(20, result.Length);
        Assert.Contains("...", result);
    }

    [Fact]
    public void Truncate_WithSingleLongSegment_FallsBackToCharacterTruncation()
    {
        // Arrange.
        var segments = new[] { new string('a', 100) };

        // Act.
        var result = PathTruncator.Truncate(segments, maxLength: 20);

        // Assert.
        Assert.Equal(20, result.Length);
        Assert.StartsWith("aaaa", result);
        Assert.EndsWith("aaaa", result);
        Assert.Contains("...", result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Truncate_WithMaxLengthAtOrBelowEllipsisLength_NeverThrowsAndRespectsBudget(int maxLength)
    {
        // Arrange.
        var segments = new[] { "C:", "Users", "developer", "a-long-project-name" };

        // Act.
        var result = PathTruncator.Truncate(segments, maxLength);

        // Assert.
        Assert.True(result.Length <= maxLength);
    }

    [Fact]
    public void Truncate_NeverExceedsMaxLength_AcrossASpreadOfSegmentCounts()
    {
        // Arrange.
        for (var segmentCount = 1; segmentCount <= 10; segmentCount++)
        {
            var segments = Enumerable.Range(0, segmentCount).Select(i => $"segment-{i}").ToArray();

        // Act and assert, step by step.
            for (var maxLength = 5; maxLength <= 60; maxLength += 5)
            {
                var result = PathTruncator.Truncate(segments, maxLength);
                Assert.True(
                    result.Length <= maxLength,
                    $"segments={segmentCount}, maxLength={maxLength}, result='{result}' ({result.Length} chars)");
            }
        }
    }
}
