using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.C4.Tests;

/// <summary>
/// ADP's parser against a realistic deployment model - the AWS-style example, where the
/// infrastructure is the point and the software model is deliberately thin.
/// </summary>
/// <remarks>
/// <c>big-bank-plc.dsl</c> exercises the static levels; this one exercises everything Requirement
/// 9 covers, which that document only touches: infrastructure nodes, four levels of nesting, an
/// instance count on a node that also has a body, and relationships declared inside a deployment
/// environment between an infrastructure node and a container instance. It too is certified valid
/// by the real Structurizr CLI, so a disagreement here is ADP's to fix.
/// </remarks>
public class AwsDeploymentTests
{
    private static readonly C4Workspace Workspace =
        C4Parser.Parse(C4Document.Parse(File.ReadAllText(IoPath.Combine("Fixtures", "aws-deployment.dsl"))));

    private static C4Element Node(string name) => Workspace.Elements.Single(element =>
        element.Kind is C4ElementKind.DeploymentNode or C4ElementKind.InfrastructureNode && element.Name == name);

    [Fact]
    public void InfrastructureNodes_AreTheirOwnKind_NotDeploymentNodes()
    {
        // Arrange, act and assert.
        // They nest in the same place and read almost the same, which is exactly why a parser
        // that treats them as one gets away with it until something asks (Requirement 9.4).
        Assert.Equal(C4ElementKind.InfrastructureNode, Node("Route 53").Kind);
        Assert.Equal(C4ElementKind.InfrastructureNode, Node("Elastic Load Balancer").Kind);
        Assert.Equal(C4ElementKind.DeploymentNode, Node("Amazon RDS").Kind);
    }

    [Fact]
    public void DeploymentNodesNest_FourDeep()
    {
        // Arrange and act.
        // Amazon Web Services > US-East-1 > Autoscaling group > Amazon EC2.
        var ec2 = Node("Amazon EC2");
        var autoscaling = Workspace.Find(ec2.ParentId!)!;
        var region = Workspace.Find(autoscaling.ParentId!)!;
        var cloud = Workspace.Find(region.ParentId!)!;

        // Assert.
        Assert.Equal("Autoscaling group", autoscaling.Name);
        Assert.Equal("US-East-1", region.Name);
        Assert.Equal("Amazon Web Services", cloud.Name);
        Assert.Null(cloud.ParentId);
    }

    [Fact]
    public void AnInstanceCountOnANodeThatAlsoHasABody_IsNotMistakenForSomethingElse()
    {
        // `deploymentNode "Amazon EC2" "" "" "" 2 {` - five arguments and then a brace. The
        // trailing count is the argument a line-based parser is most likely to mis-attribute,
        // and here there is a block after it as well.
        var ec2 = Node("Amazon EC2");

        Assert.Empty(ec2.Description);
        Assert.Empty(ec2.Technology);
        Assert.DoesNotContain("2", ec2.Tags);
        // The tags come from the nested `tags` line, not from the argument list.
        Assert.Contains("Amazon Web Services - EC2", ec2.Tags);
    }

    [Fact]
    public void TagsDeclaredInsideABlock_ReachTheElement()
    {
        // Arrange, act and assert.
        Assert.Contains("Amazon Web Services - Route 53", Node("Route 53").Tags);
        Assert.Contains("Amazon Web Services - Region", Node("US-East-1").Tags);
    }

    [Fact]
    public void ContainerInstances_ResolveToTheContainersTheyDeploy()
    {
        // Act.
        var instances = Workspace.Elements.Where(element => element.Kind == C4ElementKind.ContainerInstance).ToArray();

        // Assert.
        Assert.Equal(2, instances.Length);
        Assert.Contains(instances, instance => instance.ReferencedId == "webApplication");
        Assert.Contains(instances, instance => instance.ReferencedId == "database");
    }

    [Fact]
    public void RelationshipsDeclaredInTheEnvironment_ReachInfrastructureAndInstancesAlike()
    {
        // Arrange, act and assert.
        // Both ends are declared inside the deployment environment rather than the model, and
        // one of them is a container instance identified by its own name.
        Assert.Contains(Workspace.Relationships, r =>
            r.SourceId == "route53" && r.DestinationId == "elb" && r.Technology == "HTTPS");
        Assert.Contains(Workspace.Relationships, r =>
            r.SourceId == "elb" && r.DestinationId == "webApplicationInstance");
    }

    [Fact]
    public void TheDeploymentView_KnowsItsEnvironment_AndItsScope()
    {
        // Act.
        var view = Assert.Single(Workspace.Views);

        // Assert.
        Assert.Equal(C4ViewKind.Deployment, view.Kind);
        Assert.Equal("AmazonWebServicesDeployment", view.Key);
        Assert.Equal("Live", view.Environment);
        Assert.Equal("springPetClinic", view.ScopeId);
    }

    [Fact]
    public void TheModelIsClean_ByC4sOwnRules()
    {
        // Arrange and act.
        var problems = C4RuleSet.Validate(Workspace)
            .Where(problem => problem.RuleId != C4Rules.EmptyView)
            .ToArray();

        // Assert.
        Assert.Empty(problems.Select(problem => $"{problem.RuleId}: {problem.Message}"));
    }

    [Fact]
    public void TheDeploymentViewLaysOutWithoutOverlaps()
    {
        // Arrange.
        var layout = C4LayoutEngine.Compute(Workspace, Workspace.Views[0], C4Metrics.Default);
        var boxes = layout.Boxes.ToArray();

        // Act and assert, step by step.
        for (var i = 0; i < boxes.Length; i++)
        {
            for (var j = i + 1; j < boxes.Length; j++)
            {
                Assert.False(
                    boxes[i].Value.Overlaps(boxes[j].Value),
                    $"'{boxes[i].Key}' {boxes[i].Value} overlaps '{boxes[j].Key}' {boxes[j].Value}");
            }
        }
    }
}
