using EtAlii.Adp.Backend.Diagrams;
using Google.Protobuf;
using Xunit;

namespace EtAlii.Adp.C4.Tests;

/// <summary>
/// What a connection actually receives: elements at their measured sizes, relationships as
/// their own elements, boundaries, and the view's title, legend and problems
/// (c4-diagrams Requirements 4, 9.7, 9.8, 10.8).
/// </summary>
public class C4ElementMapperTests
{
    private static readonly C4ElementMapper Mapper = new(C4Metrics.Default, new C4LayoutSidecar());

    private const string Sample = """
        workspace "Big Bank" {
            model {
                u = person "Customer" "A retail banking customer."
                s = softwareSystem "Internet Banking" "Lets customers view accounts." {
                    web = container "Web App" "Serves pages." "React"
                    db = container "Database" "Stores accounts." "PostgreSQL" "Database"
                    web -> db "Reads from and writes to" "SQL/TCP"
                }
                mainframe = softwareSystem "Mainframe" "The core system." "External"
                u -> web "Views accounts using" "HTTPS"
                s -> mainframe "Gets account information from" "XML/HTTPS"
            }
            views {
                systemContext s "context" {
                    include *
                }
                container s "containers" {
                    include *
                }
            }
        }
        """;

    private static (C4Workspace Workspace, C4View View) Load(string key, string dsl = Sample)
    {
        var workspace = C4Parser.Parse(C4Document.Parse(dsl));
        return (workspace, workspace.FindView(key)!);
    }

    private static IReadOnlyList<DiagramElement> Visible(string key, string dsl = Sample)
    {
        var (workspace, view) = Load(key, dsl);
        return Mapper.Visible(workspace, view, DiagramViewport.Unbounded);
    }

    private static C4ElementPayload PayloadOf(DiagramElement element) =>
        C4ElementPayload.Parser.ParseFrom(element.Payload.Span);

    [Fact]
    public void AContextView_DeliversItsPeopleAndSystems_AndNoContainers()
    {
        var elements = Visible("context");

        var nodes = elements.Where(e => e.Type == C4ElementMapper.NodeType).ToArray();
        Assert.Equal(["mainframe", "s", "u"], nodes.Select(n => n.Id).Order());
    }

    [Fact]
    public void EveryNode_CarriesItsMeasuredBox_SoTheCanvasNeverGuesses()
    {
        var nodes = Visible("context").Where(e => e.Type == C4ElementMapper.NodeType);

        Assert.All(nodes, node =>
        {
            var payload = PayloadOf(node);
            Assert.True(payload.Width > 0, "an element arrived with no measured width");
            Assert.True(payload.Height > 0, "an element arrived with no measured height");
        });
    }

    [Fact]
    public void ANode_CarriesTheThreeLinesC4Asks_ForNameTypeAndDescription()
    {
        var web = Visible("containers").Single(e => e.Id == "web");

        var payload = PayloadOf(web);
        Assert.Equal("Web App", payload.Name);
        Assert.Equal("[Container: React]", payload.TypeLine);
        Assert.Equal("Serves pages.", payload.Description);
    }

    [Fact]
    public void AnExternalSystem_IsFlaggedAndMuted()
    {
        var mainframe = PayloadOf(Visible("context").Single(e => e.Id == "mainframe"));

        Assert.True(mainframe.External);
        Assert.Equal(C4Theme.ExternalBackground, mainframe.Style.Background);
    }

    [Theory]
    [InlineData("u", C4Theme.PersonBackground, "Person")]
    [InlineData("s", C4Theme.SoftwareSystemBackground, "RoundedBox")]
    public void TheDefaultTheme_IsTheOneTheReferenceDiagramsShow(string id, string background, string shape)
    {
        var payload = PayloadOf(Visible("context").Single(e => e.Id == id));

        Assert.Equal(background, payload.Style.Background);
        Assert.Equal(shape, payload.Style.Shape);
    }

    [Fact]
    public void AComponentsText_IsBlack_BecauseItsFillIsTooLightForWhite()
    {
        var dsl = """
            workspace {
                model {
                    s = softwareSystem "S" "desc" {
                        c = container "C" "desc" "Kotlin" {
                            comp = component "Comp" "desc" "Kotlin"
                        }
                    }
                }
                views {
                    component c "components" {
                        include *
                    }
                }
            }
            """;

        var payload = PayloadOf(Visible("components", dsl).Single(e => e.Id == "comp"));

        Assert.Equal(C4Theme.ComponentBackground, payload.Style.Background);
        Assert.Equal(C4Theme.DarkText, payload.Style.Color);
    }

    [Fact]
    public void ADataStore_IsACylinder()
    {
        var db = PayloadOf(Visible("containers").Single(e => e.Id == "db"));

        Assert.Equal("Cylinder", db.Style.Shape);
    }

    [Fact]
    public void ADocumentsOwnStyles_OverrideTheDefaultTheme()
    {
        // Requirement 4.8: the palette is ADP's default, not something a document can be wrong
        // about - C4 is explicitly notation independent.
        var dsl = Sample.Replace("""
                container s "containers" {
                    include *
                }
        """, """
                container s "containers" {
                    include *
                }
                styles {
                    element "Person" {
                        background #ff0000
                    }
                }
        """, StringComparison.Ordinal);

        var workspace = C4Parser.Parse(C4Document.Parse(dsl));
        Assert.Contains(workspace.Styles, style => style.Tag == "Person");
    }

    [Fact]
    public void RelationshipsTravel_AsTheirOwnElements_WithLabelAndTechnology()
    {
        var relationships = Visible("containers").Where(e => e.Type == C4ElementMapper.RelationshipType).ToArray();

        Assert.NotEmpty(relationships);
        var payload = C4RelationshipPayload.Parser.ParseFrom(
            relationships.Single(r => C4RelationshipPayload.Parser.ParseFrom(r.Payload.Span).SourceId == "web").Payload.Span);
        Assert.Equal("db", payload.DestinationId);
        Assert.Equal("Reads from and writes to", payload.Description);
        Assert.Equal("SQL/TCP", payload.Technology);
        // Both ends carry their measured boxes, so the canvas anchors the line on real edges.
        Assert.True(payload.SourceWidth > 0 && payload.DestinationWidth > 0);
    }

    [Fact]
    public void AContextView_ElevatesAContainersRelationship_ToTheSystemThatContainsIt()
    {
        // Found by the manual pass. The API talks to the Mainframe; at the system level that
        // is Internet Banking talking to the Mainframe. Without elevating it, a context diagram
        // shows the systems it depends on and no lines to them - which says there is no
        // dependency, and is worse than showing nothing.
        var relationships = Visible("context")
            .Where(e => e.Type == C4ElementMapper.RelationshipType)
            .Select(e => C4RelationshipPayload.Parser.ParseFrom(e.Payload.Span))
            .ToArray();

        Assert.Contains(relationships, r => r.SourceId == "s" && r.DestinationId == "mainframe");
        Assert.Contains(relationships, r => r.SourceId == "u" && r.DestinationId == "s");
    }

    [Fact]
    public void AnElevatedRelationship_IsDrawnOnce_HoweverManyCollapseOntoIt()
    {
        // Two containers of one system both talking to the same external system is one line at
        // the system level, not two on top of each other.
        var dsl = """
            workspace {
                model {
                    s = softwareSystem "S" "desc" {
                        a = container "A" "desc" "Tech"
                        b = container "B" "desc" "Tech"
                    }
                    ext = softwareSystem "Ext" "desc" "External"
                    a -> ext "Calls" "HTTPS"
                    b -> ext "Calls" "HTTPS"
                }
                views {
                    systemContext s "context" {
                        include *
                    }
                }
            }
            """;

        var relationships = Visible("context", dsl).Where(e => e.Type == C4ElementMapper.RelationshipType).ToArray();

        Assert.Single(relationships);
    }

    [Fact]
    public void ARelationshipBetweenTwoContainersOfOneSystem_IsNotDrawnOnTheContextView()
    {
        // Both ends elevate to the same system, which is that system talking to itself - a
        // detail the context view has deliberately zoomed out past.
        var relationships = Visible("context")
            .Where(e => e.Type == C4ElementMapper.RelationshipType)
            .Select(e => C4RelationshipPayload.Parser.ParseFrom(e.Payload.Span))
            .ToArray();

        Assert.DoesNotContain(relationships, r => r.SourceId == "s" && r.DestinationId == "s");
    }

    [Fact]
    public void AContainerView_DrawsTheSystemAsItsBoundary_AndNotAlsoAsABox()
    {
        // Found by the manual pass: the system in scope was drawn as a box inside its own
        // boundary. On a container diagram the system *is* the boundary.
        var nodes = Visible("containers").Where(e => e.Type == C4ElementMapper.NodeType).Select(e => e.Id).ToArray();

        Assert.DoesNotContain("s", nodes);
        Assert.Contains("web", nodes);
        Assert.Contains(Visible("containers"), e => e.Type == C4ElementMapper.BoundaryType);
    }

    [Fact]
    public void AContextView_StillDrawsItsOwnSystem_BecauseThereItIsThePrimaryElement()
    {
        var nodes = Visible("context").Where(e => e.Type == C4ElementMapper.NodeType).Select(e => e.Id).ToArray();

        Assert.Contains("s", nodes);
    }

    [Fact]
    public void AContainerView_DrawsABoundary()
    {
        var boundaries = Visible("containers").Where(e => e.Type == C4ElementMapper.BoundaryType).ToArray();

        var boundary = Assert.Single(boundaries);
        var payload = C4BoundaryPayload.Parser.ParseFrom(boundary.Payload.Span);
        Assert.Equal("Internet Banking", payload.Name);
        Assert.Equal("Software System", payload.Kind);
    }

    // ---- the furniture every C4 diagram carries -----------------------------------------

    [Fact]
    public void EveryView_CarriesATitleInC4sOwnWording()
    {
        var view = C4ViewPayload.Parser.ParseFrom(
            Visible("context").Single(e => e.Type == C4ElementMapper.ViewType).Payload.Span);

        Assert.Equal("System Context diagram for Internet Banking", view.Title);
    }

    [Theory]
    [InlineData(C4ViewKind.SystemContext, "System Context diagram for S")]
    [InlineData(C4ViewKind.Container, "Container diagram for S")]
    [InlineData(C4ViewKind.Component, "Component diagram for S")]
    public void TheTitleNamesTheDiagramTypeAndItsScope(C4ViewKind kind, string expected)
    {
        var workspace = new C4Workspace("W", [new C4Element("s", C4ElementKind.SoftwareSystem, "S", "d", "", [], null, 1)], [], [], [], []);
        var view = new C4View(kind, "k", "s", null, null, true, [], [], null, [], 1);

        Assert.Equal(expected, C4ElementMapper.TitleOf(workspace, view));
    }

    [Fact]
    public void ATitleTheDocumentDeclares_Wins()
    {
        var workspace = new C4Workspace("W", [], [], [], [], []);
        var view = new C4View(C4ViewKind.SystemContext, "k", null, null, "Our own title", true, [], [], null, [], 1);

        Assert.Equal("Our own title", C4ElementMapper.TitleOf(workspace, view));
    }

    [Fact]
    public void EveryView_CarriesAKeyDescribingTheNotationActuallyInUse()
    {
        var view = C4ViewPayload.Parser.ParseFrom(
            Visible("context").Single(e => e.Type == C4ElementMapper.ViewType).Payload.Span);

        Assert.NotEmpty(view.Legend);
        Assert.Contains(view.Legend, entry => entry.Label == "Person");
        Assert.Contains(view.Legend, entry => entry.Label == "Software System");
        // The external system is called out separately, because scope is what the muting says.
        Assert.Contains(view.Legend, entry => entry.Label.Contains("external", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheViewCarriesNoProblemsOfItsOwn_BecauseCoreAlreadyPushesThem()
    {
        // C4's rules report through IDiagramValidator, and core pushes the project's problems
        // to every connection with an element-id location of their own. Carrying a second copy
        // on the view payload would be a second thing to keep in step - and the canvas would
        // eventually show one set while the errors panel showed another.
        var dsl = Sample.Replace("person \"Customer\" \"A retail banking customer.\"", "person \"Customer\"", StringComparison.Ordinal);

        var view = C4ViewPayload.Parser.ParseFrom(
            Visible("context", dsl).Single(e => e.Type == C4ElementMapper.ViewType).Payload.Span);

        Assert.Equal("System Context diagram for Internet Banking", view.Title);
        Assert.NotEmpty(view.Legend);
        Assert.DoesNotContain(
            C4ViewPayload.Descriptor.Fields.InFieldNumberOrder().Select(field => field.Name),
            name => name.Contains("problem", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheRulesStillReachTheUser_ThroughCoresValidatorSeam()
    {
        // The same missing description, reported the one way it should be.
        var dsl = Sample.Replace("person \"Customer\" \"A retail banking customer.\"", "person \"Customer\"", StringComparison.Ordinal);

        var problems = C4RuleSet.Validate(C4Parser.Parse(C4Document.Parse(dsl)));

        var problem = Assert.Single(problems, p => p.RuleId == C4RuleSet.Rules.MissingDescription);
        Assert.Equal(new EtAlii.Adp.Diagram.DiagramProblemLocation.ElementId("u"), problem.Location);
    }

    // ---- the viewport --------------------------------------------------------------------

    [Fact]
    public void ATightViewport_StillDeliversTheFarEndOfALineThatLeavesIt()
    {
        // The rule the mindmap's missing connectors taught: an on-screen element brings whatever
        // its lines reach, or the line has nothing to anchor to.
        var (workspace, view) = Load("containers");
        var layout = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default);
        var web = layout.Boxes["web"];
        var tight = new DiagramViewport(web.X, web.Y, web.Right, web.Bottom);

        var ids = Mapper.Visible(workspace, view, tight).Select(e => e.Id).ToHashSet();

        Assert.Contains("web", ids);
        Assert.Contains("db", ids);
    }

    [Fact]
    public void AViewportFarOffTheDiagram_DeliversNoNodes()
    {
        var (workspace, view) = Load("context");

        var elements = Mapper.Visible(workspace, view, new DiagramViewport(100000, 100000, 200000, 200000));

        Assert.Empty(elements.Where(e => e.Type == C4ElementMapper.NodeType));
        // ...but the view's own furniture still arrives, so the tab is not blank.
        Assert.Contains(elements, e => e.Type == C4ElementMapper.ViewType);
    }
}
