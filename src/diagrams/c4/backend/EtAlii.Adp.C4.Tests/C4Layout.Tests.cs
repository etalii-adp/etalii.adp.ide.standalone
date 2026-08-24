using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.C4.Tests;

/// <summary>
/// Where a view's elements go, computed in the backend. The geometry guarantees matter as much
/// as the arrangement: nothing overlaps, and a boundary contains what it encloses
/// (c4-diagrams Requirements 8.1, 8.2, 8.5, 8.6).
/// </summary>
public class C4LayoutTests
{
    private static (C4Workspace Workspace, C4View View) Load(string dsl, string? viewKey = null)
    {
        var workspace = C4Parser.Parse(C4Document.Parse(dsl));
        var view = viewKey is null ? workspace.Views[0] : workspace.FindView(viewKey)!;
        return (workspace, view);
    }

    private static (C4Workspace Workspace, C4View View) LoadFixture(string name, string? viewKey = null) =>
        Load(File.ReadAllText(IoPath.Combine("Fixtures", name)), viewKey);

    private static C4Layout Compute(string dsl, string? viewKey = null)
    {
        var (workspace, view) = Load(dsl, viewKey);
        return C4LayoutEngine.Compute(workspace, view, C4Metrics.Default);
    }

    private const string Chain = """
        workspace {
            model {
                u = person "User" "Uses it."
                s = softwareSystem "System" "Does it."
                e = softwareSystem "Email" "Sends it."
                u -> s "Uses" "HTTPS"
                s -> e "Sends via" "SMTP"
            }
            views {
                systemContext s "context" {
                    include *
                }
            }
        }
        """;

    [Fact]
    public void EveryMemberOfTheView_GetsABox()
    {
        var layout = Compute(Chain);

        Assert.Equal(3, layout.Boxes.Count);
        Assert.All(layout.Boxes.Values, box => Assert.True(box.Width > 0 && box.Height > 0));
    }

    [Fact]
    public void Compute_IsDeterministic()
    {
        var first = Compute(Chain);
        var second = Compute(Chain);

        Assert.Equal(first.Boxes.OrderBy(p => p.Key).ToArray(), second.Boxes.OrderBy(p => p.Key).ToArray());
    }

    [Fact]
    public void WhatPointsAtSomething_ComesBeforeIt()
    {
        // A person ends up before the system they use, and the system before what it sends to.
        var layout = Compute(Chain);

        Assert.True(layout.Boxes["u"].Y < layout.Boxes["s"].Y, "the user should rank before the system");
        Assert.True(layout.Boxes["s"].Y < layout.Boxes["e"].Y, "the system should rank before the email system");
    }

    [Fact]
    public void NoTwoElements_Overlap()
    {
        var layout = Compute(Chain);
        var boxes = layout.Boxes.Values.ToArray();

        for (var i = 0; i < boxes.Length; i++)
        {
            for (var j = i + 1; j < boxes.Length; j++)
            {
                Assert.False(boxes[i].Overlaps(boxes[j]), $"boxes {i} and {j} overlap");
            }
        }
    }

    [Theory]
    [InlineData("minimal.dsl")]
    [InlineData("comments-everywhere.dsl")]
    [InlineData("deployment-nested.dsl")]
    [InlineData("dynamic-interactions.dsl")]
    public void NoTwoElements_OverlapOnAnyFixture(string name)
    {
        var (workspace, view) = LoadFixture(name);
        var layout = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default);
        var boxes = layout.Boxes.Values.ToArray();

        for (var i = 0; i < boxes.Length; i++)
        {
            for (var j = i + 1; j < boxes.Length; j++)
            {
                Assert.False(boxes[i].Overlaps(boxes[j]), $"{name}: two boxes overlap");
            }
        }
    }

    [Fact]
    public void ADeclaredAutoLayoutDirection_IsHonoured()
    {
        // Requirement 8.2: where the author asked for a direction, it wins over ADP's default.
        var topToBottom = Compute(Chain);
        var leftToRight = Compute(Chain.Replace("include *", "include *\n            autoLayout lr", StringComparison.Ordinal));

        Assert.True(topToBottom.Boxes["u"].Y < topToBottom.Boxes["s"].Y);
        Assert.True(leftToRight.Boxes["u"].X < leftToRight.Boxes["s"].X, "lr should rank along x");
        Assert.Equal(leftToRight.Boxes["u"].Y, leftToRight.Boxes["s"].Y, 0);
    }

    [Fact]
    public void AReversedDirection_ReadsTheSameLayoutFromTheOtherEnd()
    {
        var rightToLeft = Compute(Chain.Replace("include *", "include *\n            autoLayout rl", StringComparison.Ordinal));

        Assert.True(rightToLeft.Boxes["u"].X > rightToLeft.Boxes["s"].X, "rl should put the source on the right");
    }

    [Fact]
    public void AContainerView_DrawsABoundaryThatContainsItsContainers()
    {
        var dsl = """
            workspace {
                model {
                    u = person "User" "Uses it."
                    s = softwareSystem "System" "Does it." {
                        web = container "Web" "Serves." "React"
                        db = container "Data" "Stores." "PostgreSQL"
                        web -> db "Reads" "SQL"
                    }
                    u -> web "Visits" "HTTPS"
                }
                views {
                    container s "containers" {
                        include *
                    }
                }
            }
            """;

        var layout = Compute(dsl);

        var boundary = Assert.Single(layout.Boundaries);
        Assert.Equal("System", boundary.Name);
        Assert.Equal("Software System", boundary.Kind);
        // Requirement 8.6: it contains its members, with a margin.
        foreach (var id in new[] { "web", "db" })
        {
            var box = layout.Boxes[id];
            Assert.True(box.X > boundary.Box.X, $"{id} escapes the boundary on the left");
            Assert.True(box.Right < boundary.Box.Right, $"{id} escapes on the right");
            Assert.True(box.Y > boundary.Box.Y, $"{id} escapes above");
            Assert.True(box.Bottom < boundary.Box.Bottom, $"{id} escapes below");
        }

        // The person is outside the system, so the boundary must not swallow them.
        Assert.False(layout.Boxes["u"].Overlaps(boundary.Box) && layout.Boxes["u"].Y > boundary.Box.Y, "the external person was enclosed");
    }

    [Fact]
    public void WhatIsNotInsideTheSystem_IsNotDrawnInsideItsBoundary()
    {
        // Found by the manual pass: an external system landed inside the boundary, which says
        // it is part of the system - the opposite of what the diagram means.
        var dsl = """
            workspace {
                model {
                    u = person "User" "Uses it."
                    s = softwareSystem "System" "Does it." {
                        web = container "Web" "Serves." "React"
                        api = container "API" "Answers." "Kotlin"
                        web -> api "Calls" "JSON"
                    }
                    ext = softwareSystem "Mainframe" "The old one." "External"
                    u -> web "Visits" "HTTPS"
                    api -> ext "Calls" "XML/HTTPS"
                }
                views {
                    container s "containers" {
                        include *
                    }
                }
            }
            """;

        var layout = Compute(dsl);

        var boundary = Assert.Single(layout.Boundaries).Box;
        foreach (var id in new[] { "u", "ext" })
        {
            Assert.False(layout.Boxes[id].Overlaps(boundary), $"'{id}' is outside the system but was drawn inside its boundary");
        }

        // ...and the containers are still inside it.
        Assert.True(layout.Boxes["web"].Overlaps(boundary));
        Assert.True(layout.Boxes["api"].Overlaps(boundary));
    }

    [Fact]
    public void PushingAnOutsiderOut_DoesNotMakeItOverlapSomethingElse()
    {
        var dsl = """
            workspace {
                model {
                    u = person "User" "Uses it."
                    s = softwareSystem "System" "Does it." {
                        web = container "Web" "Serves." "React"
                        api = container "API" "Answers." "Kotlin"
                        web -> api "Calls" "JSON"
                    }
                    ext = softwareSystem "Mainframe" "The old one." "External"
                    u -> web "Visits" "HTTPS"
                    api -> ext "Calls" "XML/HTTPS"
                }
                views {
                    container s "containers" {
                        include *
                    }
                }
            }
            """;

        var boxes = Compute(dsl).Boxes.Values.ToArray();

        for (var i = 0; i < boxes.Length; i++)
        {
            for (var j = i + 1; j < boxes.Length; j++)
            {
                Assert.False(boxes[i].Overlaps(boxes[j]), "pushing an outsider out of the boundary made two elements overlap");
            }
        }
    }

    [Fact]
    public void AContextView_DrawsNoBoundary()
    {
        Assert.Empty(Compute(Chain).Boundaries);
    }

    [Fact]
    public void ACycleInTheRelationships_DoesNotHangTheLayout()
    {
        var dsl = """
            workspace {
                model {
                    a = softwareSystem "A" "desc"
                    b = softwareSystem "B" "desc"
                    a -> b "Calls" "HTTPS"
                    b -> a "Calls back" "HTTPS"
                }
                views {
                    systemLandscape "all" {
                        include *
                    }
                }
            }
            """;

        var layout = Compute(dsl);

        Assert.Equal(2, layout.Boxes.Count);
    }

    [Fact]
    public void AnEmptyView_LaysOutNothing_RatherThanThrowing()
    {
        var dsl = "workspace {\n  model {\n  }\n  views {\n    systemLandscape \"all\" {\n    }\n  }\n}\n";

        var layout = Compute(dsl);

        Assert.Empty(layout.Boxes);
        Assert.Empty(layout.Boundaries);
    }

    // ---- what the box says (Requirement 4.2) --------------------------------------------

    [Theory]
    [InlineData(C4ElementKind.Person, "", "[Person]")]
    [InlineData(C4ElementKind.SoftwareSystem, "", "[Software System]")]
    [InlineData(C4ElementKind.Container, "React", "[Container: React]")]
    [InlineData(C4ElementKind.Component, "Kotlin", "[Component: Kotlin]")]
    [InlineData(C4ElementKind.DeploymentNode, "ECS", "[Deployment Node: ECS]")]
    [InlineData(C4ElementKind.Container, "", "[Container]")]
    public void TheBracketedLine_NamesTheTypeAndTechnology(C4ElementKind kind, string technology, string expected)
    {
        var element = new C4Element("id", kind, "Name", "Description", technology, [], null, 1);

        Assert.Equal(expected, C4LayoutEngine.TypeLineOf(element));
    }

    [Fact]
    public void ALongDescription_WidensAndHeightensTheBox_ButNotWithoutLimit()
    {
        var metrics = C4Metrics.Default;
        var small = metrics.Measure("A", "[Person]", "Short.");
        var large = metrics.Measure("A", "[Person]", new string('x', 400));

        Assert.True(large.Height > small.Height, "a wrapping description should make the box taller");
        Assert.True(large.Width <= metrics.MaximumWidth, "no box may grow past the maximum width");
    }
}
