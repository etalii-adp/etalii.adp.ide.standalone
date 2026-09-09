using EtAlii.Adp.Diagram;
using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// The naming rule a newly added element gets, which the user chose over a fixed placeholder:
/// continue whatever pattern the siblings are already in.
/// </summary>
/// <remarks>
/// Worth this many cases because it GUESSES, and a guess that lands wrong is a name the user
/// has to clear before typing rather than one they can overwrite. The cases below are the
/// shapes the shipped mindmaps actually contain plus the ones that would embarrass it.
/// </remarks>
public class SiblingNamingTests
{
    [Fact]
    public void ContinuesANumberedRun()
    {
        Assert.Equal("Phase 3", SiblingNaming.NextName(["Phase 1", "Phase 2"]));
    }

    [Fact]
    public void CountsFromTheHighestRatherThanTheCount()
    {
        // Delete "Phase 2" and add again: a count would hand back "Phase 3", which already
        // exists, and the uniqueness pass would then quietly rename it "Phase 3 2".
        Assert.Equal("Phase 4", SiblingNaming.NextName(["Phase 1", "Phase 3"]));
    }

    [Fact]
    public void TakesTheStemMostSiblingsAgreeOn()
    {
        // One stray numbered name must not decide the answer for four others.
        Assert.Equal("Step 5", SiblingNaming.NextName(["Step 1", "Step 2", "Step 3", "Step 4", "Draft 9"]));
    }

    [Fact]
    public void FollowsASharedLastWord()
    {
        // The shipped rendering-engine map: "Measure pass", "Arrange pass" under Layout.
        Assert.Equal("New pass", SiblingNaming.NextName(["Measure pass", "Arrange pass"]));
    }

    [Fact]
    public void NeedsTwoSiblingsToCallSomethingAPattern()
    {
        // One sibling ending in "pass" is a coincidence, not a convention - so this falls all
        // the way through to the fallback rather than inventing "New pass" from a sample of one.
        Assert.Equal("Node", SiblingNaming.NextName(["Measure pass"]));
    }

    [Fact]
    public void FallsBackWhenTheSiblingsShowNoPatternAtAll()
    {
        // The shipped map's top level: "Scene graph", "Layout", "Rendering pipeline".
        Assert.Equal("Node", SiblingNaming.NextName(["Scene graph", "Layout", "Rendering pipeline"]));
    }

    [Fact]
    public void NamesTheFirstChildOfAChildlessNode()
    {
        Assert.Equal("Node", SiblingNaming.NextName([]));
    }

    [Fact]
    public void NeverCollidesWithASiblingThatAlreadyHasTheName()
    {
        // Add twice without renaming. The second must not be a duplicate: two nodes with one
        // name are indistinguishable in the tree and to every test that looks one up by text.
        Assert.Equal("Node 2", SiblingNaming.NextName(["Scene graph", "Node"]));
        Assert.Equal("Node 3", SiblingNaming.NextName(["Scene graph", "Node", "Node 2"]));
    }

    [Fact]
    public void DoesNotCollideOnADerivedName()
    {
        // The shared-last-word branch can collide too, and takes the same uniqueness pass.
        Assert.Equal("New pass 2", SiblingNaming.NextName(["Measure pass", "Arrange pass", "New pass"]));
    }

    [Fact]
    public void TreatsCaseAsTheSameStemAndKeepsTheAuthorsSpelling()
    {
        // "phase 2" is the same run as "Phase 1", and the spelling copied is the highest
        // numbered sibling's, being the one the author most recently chose.
        Assert.Equal("phase 3", SiblingNaming.NextName(["Phase 1", "phase 2"]));
    }

    [Fact]
    public void IgnoresBlankSiblings()
    {
        // An empty node text is legal in a mindmap (Requirement 7.6), and must not be read as
        // a sibling with a pattern - nor make the numbered branch throw.
        Assert.Equal("Phase 3", SiblingNaming.NextName(["Phase 1", "Phase 2", "", "   "]));
    }

    [Fact]
    public void SurvivesANumberNoIntegerCouldHold()
    {
        // A node someone named after a long identifier. The regex caps the digits it will read
        // rather than letting long.Parse throw on a forty-digit "number".
        var siblings = new[] { "Build 99999999999999999999999999999999999999" };
        var name = SiblingNaming.NextName(siblings);
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.DoesNotContain(name, siblings);
    }

    [Fact]
    public void TakesTheCallersFallbackWhenItHasOne()
    {
        // A module whose things are not "nodes" says so; the timeline calls them elements.
        Assert.Equal("New element", SiblingNaming.NextName(["Discovery", "Build"], "New element"));
    }
}
