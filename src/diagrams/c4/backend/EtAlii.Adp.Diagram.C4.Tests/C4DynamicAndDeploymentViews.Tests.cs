using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The two view kinds that drew nothing useful: a dynamic view drew an empty canvas, because its
/// interactions' participants never became members, and a deployment view drew every node,
/// instance and infrastructure node in one flat row of blank cards with no lines between them
/// (found by the DISL definition work, 2026-09-30).
/// </summary>
public class C4DynamicAndDeploymentViewsTests
{
    private static readonly C4ElementMapper Mapper = new(C4Metrics.Default, new C4LayoutSidecar());

    private static (C4Workspace Workspace, C4View View) LoadFixture(string name)
    {
        var workspace = C4Parser.Parse(C4Document.Parse(File.ReadAllText(IoPath.Combine("Fixtures", name))));
        return (workspace, workspace.Views[0]);
    }

    private static C4Element InstanceOf(C4Workspace workspace, string containerId, string nodeId) =>
        workspace.Elements.Single(element => element.ReferencedId == containerId && element.ParentId == nodeId);

    private static bool Encloses(C4Box outer, C4Box inner) =>
        inner.X > outer.X && inner.Y > outer.Y && inner.Right < outer.Right && inner.Bottom < outer.Bottom;

    [Fact]
    public void ADynamicView_ShowsWhatTakesPartInItsInteractions_WithoutAnInclude()
    {
        // Arrange.
        (C4Workspace workspace, C4View view) = LoadFixture("dynamic-interactions.dsl");
        Assert.Empty(view.Includes);
        Assert.False(view.IncludesEverything);

        // Act.
        var layout = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default);

        // Assert.
        Assert.Equal(["api", "db", "u", "web"], layout.Boxes.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ADynamicView_DeliversItsInteractions_NumberedInOrder()
    {
        // Arrange.
        (C4Workspace workspace, C4View view) = LoadFixture("dynamic-interactions.dsl");

        // Act.
        var relationships = Mapper.Visible(workspace, view, DiagramViewport.Unbounded)
            .Where(element => element.Type == C4ElementMapper.RelationshipType)
            .Select(element => C4RelationshipPayload.Parser.ParseFrom(element.Payload.Span))
            .ToArray();

        // Assert.
        Assert.Equal(
            ["1 u->web", "2 web->api", "3 api->db", "4 api->web"],
            relationships.Select(r => $"{r.InteractionOrder} {r.SourceId}->{r.DestinationId}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ADynamicViewWhoseInteractionsNameNothingDeclared_IsReportedEmpty()
    {
        // Arrange.
        var dsl = """
            workspace {
                model {
                    s = softwareSystem "System" "Does it."
                }
                views {
                    dynamic s "flow" {
                        ghost -> phantom "Talks to"
                    }
                }
            }
            """;

        // Act.
        var problems = C4RuleSet.Validate(C4Parser.Parse(C4Document.Parse(dsl)));

        // Assert.
        Assert.Contains(problems, problem => problem.RuleId == C4Rules.EmptyView);
    }

    [Fact]
    public void ADeploymentView_DrawsEachNodeAroundWhatItHosts_ToAnyDepth()
    {
        // Arrange.
        (C4Workspace workspace, C4View view) = LoadFixture("deployment-nested.dsl");
        var web = InstanceOf(workspace, "web", "node");
        var primaryDb = InstanceOf(workspace, "db", "primary");

        // Act.
        var layout = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default);
        var boundary = layout.Boundaries.ToDictionary(b => b.Id, b => b.Box);

        // Assert, from the inside out: the instance sits in its node, the node in the cluster,
        // the cluster in the region.
        Assert.True(Encloses(boundary["boundary:node"], layout.Boxes[web.Id]), "the web instance is not inside its Fargate node");
        Assert.True(Encloses(boundary["boundary:cluster"], boundary["boundary:node"]), "the Fargate node is not inside its cluster");
        Assert.True(Encloses(boundary["boundary:region"], boundary["boundary:cluster"]), "the cluster is not inside its region");
        Assert.True(Encloses(boundary["boundary:primary"], layout.Boxes[primaryDb.Id]), "the database instance is not inside its primary node");
        Assert.True(Encloses(boundary["boundary:region"], layout.Boxes["dns"]), "the infrastructure node is not inside its region");

        // A node drawn as a boundary is not also drawn as a card underneath what it hosts.
        Assert.DoesNotContain("region", layout.Boxes.Keys);
    }

    [Fact]
    public void ADeploymentView_LaysOutWithoutOverlaps()
    {
        // Arrange.
        (C4Workspace workspace, C4View view) = LoadFixture("deployment-nested.dsl");

        // Act.
        var boxes = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default).Boxes.Values.ToArray();

        // Assert.
        for (var i = 0; i < boxes.Length; i++)
        {
            for (var j = i + 1; j < boxes.Length; j++)
            {
                Assert.False(boxes[i].Overlaps(boxes[j]), $"{boxes[i]} overlaps {boxes[j]}");
            }
        }
    }

    [Fact]
    public void ADeploymentView_BoundariesComeOutermostFirst_SoTheInnerOnesDrawOnTop()
    {
        // Arrange.
        (C4Workspace workspace, C4View view) = LoadFixture("deployment-nested.dsl");

        // Act.
        var ids = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default).Boundaries.Select(b => b.Id).ToList();

        // Assert.
        Assert.True(ids.IndexOf("boundary:region") < ids.IndexOf("boundary:cluster"));
        Assert.True(ids.IndexOf("boundary:cluster") < ids.IndexOf("boundary:node"));
    }

    [Fact]
    public void AContainerInstance_ShowsTheContainerItInstantiates()
    {
        // Arrange.
        (C4Workspace workspace, C4View view) = LoadFixture("deployment-nested.dsl");
        var web = InstanceOf(workspace, "web", "node");

        // Act.
        var node = Mapper.Visible(workspace, view, DiagramViewport.Unbounded).Single(element => element.Id == web.Id);
        var payload = C4ElementPayload.Parser.ParseFrom(node.Payload.Span);

        // Assert.
        Assert.Equal("Web App", payload.Name);
        Assert.Equal("[Container: React]", payload.TypeLine);
    }

    private const string Deployed = """
        workspace {
            model {
                s = softwareSystem "System" "Does it." {
                    web = container "Web App" "Serves pages." "React"
                    api = container "API" "Answers." "Kotlin"
                    db = container "Database" "Stores." "PostgreSQL"
                    web -> api "Posts to" "JSON/HTTPS"
                    api -> db "Writes" "SQL/TCP"
                }
                production = deploymentEnvironment "Production" {
                    region = deploymentNode "eu-west-1" "A region." "Region" {
                        dns = infrastructureNode "Route 53" "Resolves." "DNS"
                        lb = infrastructureNode "Load Balancer" "Balances." "ELB"
                        node = deploymentNode "Service" "Runs it." "Fargate" {
                            containerInstance web
                            containerInstance api
                        }
                        primary = deploymentNode "Primary" "Writes." "PostgreSQL" {
                            containerInstance db
                        }
                        replica = deploymentNode "Replica" "Reads." "PostgreSQL" {
                            containerInstance db
                        }
                        dns -> lb "Forwards to" "HTTPS"
                    }
                }
            }
            views {
                deployment s "Production" "production" {
                    include *
                }
            }
        }
        """;

    [Fact]
    public void ADeploymentView_DrawsContainerRelationships_BetweenTheirInstances()
    {
        // Arrange.
        var workspace = C4Parser.Parse(C4Document.Parse(Deployed));
        var view = workspace.Views[0];
        var web = InstanceOf(workspace, "web", "node").Id;
        var api = InstanceOf(workspace, "api", "node").Id;
        var primary = InstanceOf(workspace, "db", "primary").Id;
        var replica = InstanceOf(workspace, "db", "replica").Id;

        // Act.
        var lines = Mapper.Visible(workspace, view, DiagramViewport.Unbounded)
            .Where(element => element.Type == C4ElementMapper.RelationshipType)
            .Select(element => C4RelationshipPayload.Parser.ParseFrom(element.Payload.Span))
            .Select(r => (r.SourceId, r.DestinationId))
            .ToArray();

        // Assert: web -> api, and api -> the database on both nodes it runs on, besides the one
        // the infrastructure nodes declare themselves.
        Assert.Contains((web, api), lines);
        Assert.Contains((api, primary), lines);
        Assert.Contains((api, replica), lines);
        Assert.Contains(("dns", "lb"), lines);
    }
}
