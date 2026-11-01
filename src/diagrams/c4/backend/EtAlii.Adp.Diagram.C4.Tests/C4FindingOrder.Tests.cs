using EtAlii.Adp.Diagram.C4.Tests.Parity;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// <see cref="C4FindingOrder"/> gives today's order of findings, element by element, which the
/// evaluator's order by rule is not (c4-disl-fbl Requirement 3.3, gap C10). The document has a finding
/// of every code, interleaved so that the two orders differ in every group: two elements with a
/// description finding each, the second of them also disconnected; a node and an infrastructure node
/// with two findings each; two relationships unlabelled, the first also without a technology; and two
/// views of an unknown scope, one showing two elements its kind does not permit, in the reverse of the
/// order the model declares them, with an empty view between them.
/// </summary>
public class C4FindingOrderTests
{
    private const string BodyFile = "every-finding.dsl";

    private const string Dsl =
        """
        workspace "Every finding, interleaved" {
            !include first.dsl
            !include second.dsl
            model {
                p1 = person "P1"
                p2 = person "P2"
                s = softwareSystem "S" "Described." {
                    w = container "W" "Described."
                }
                loose = container "Loose" "Described." "Go"
                live = deploymentEnvironment "Live" {
                    n = deploymentNode "N" {
                        g = infrastructureNode "G"
                        wi = containerInstance w
                    }
                }
                p1 -> ghost "Dangling" "HTTPS"
                p1 -> w
                p1 -> s "" "HTTPS"
                p1 -> loose "Calls" "Go"
            }
            views {
                systemContext nowhere "lost" {
                    include loose w
                }
                container s "nothing" {
                    exclude w
                }
                systemContext gone "lost2" {
                    include w
                }
                deployment s "Live" "live" {
                    include *
                }
            }
        }
        """;

    private static C4Workspace Workspace => C4Body.Read(Dsl).Workspace;

    [Fact]
    public void TheDocument_HasAFindingOfEveryCode_AndTheEvaluatorsOrderIsNotTodays()
    {
        // Arrange.
        var workspace = Workspace;

        // Act.
        var today = C4RuleSet.Validate(workspace);
        var evaluated = C4Definitions.Evaluated(workspace, BodyFile, Diagram.SystemContext.Origin).Findings;

        // Assert.
        Assert.Equal(C4Rules.All.Order(StringComparer.Ordinal), today.Select(problem => problem.RuleId).Distinct().Order(StringComparer.Ordinal));
        Assert.NotEqual(Sentences(today), evaluated.Select(finding => $"{finding.Code} | {finding.Message}"));
    }

    [Fact]
    public void Order_IsTodays()
    {
        // Arrange.
        var workspace = Workspace;
        var evaluated = C4Definitions.Evaluated(workspace, BodyFile, Diagram.SystemContext.Origin).Findings;

        // Act.
        var ordered = C4FindingOrder.Order(evaluated, workspace);

        // Assert.
        Assert.Equal(Sentences(C4RuleSet.Validate(workspace)), ordered.Select(finding => $"{finding.Code} | {finding.Message}"));
    }

    [Fact]
    public void TheValidator_ReportsTodaysFindings_InTodaysOrder_AtTodaysLocations()
    {
        // Arrange.
        var workspace = Workspace;

        // Act.
        var derived = C4Definitions.Problems(workspace, BodyFile, Diagram.SystemContext.Origin);

        // Assert.
        Assert.Equal(C4DerivedFindingsTests.Lines(C4RuleSet.Validate(workspace)), C4DerivedFindingsTests.Lines(derived));
    }

    private static List<string> Sentences(IEnumerable<DiagramProblem> problems) => [.. problems.Select(problem => $"{problem.RuleId} | {problem.Message}")];
}
