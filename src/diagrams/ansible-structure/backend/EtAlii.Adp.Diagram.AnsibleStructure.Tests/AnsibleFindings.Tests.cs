using EtAlii.Adp.Specification.Disl;
using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The stand-in that hands the derived findings to the host, against each case its gaps name
/// (ansible-structure-disl-fbl, task 6): the order of findings about relations (gap 8), the sentence of a
/// file that does not parse (gap 6), and a folder that is not there (finding B5).
/// </summary>
public class AnsibleFindingsTests
{
    private static readonly DislDiagram Model =
        AnsibleModels.ModelOf(new AnsibleProjectReader().Read(IoPath.GetFullPath(IoPath.Combine("Fixtures", "broken"))));

    private static DislFinding About(string code, string elementId) =>
        new(code, code, "error", code, [elementId], 3, "playbooks/deploy.yml");

    [Fact]
    public void FindingsAboutRelations_AreInRelationOrder_WhicheverRuleRaisedThem()
    {
        // Arrange.
        var first = Model.Relations[0].Id;
        var second = Model.Relations[1].Id;

        // Act.
        var problems = AnsibleFindings.For(Model, [About("b.second", second), About("a.first", first)]);

        // Assert.
        Assert.Equal(["a.first", "b.second"], problems.Select(problem => problem.RuleId));
    }

    [Fact]
    public void AFindingAboutANode_KeepsItsPlace_BetweenRunsAboutRelations()
    {
        // Arrange.
        var first = Model.Relations[0].Id;
        var second = Model.Relations[1].Id;
        var node = Model.Nodes[0].Id;

        // Act.
        var problems = AnsibleFindings.For(Model, [About("one", second), About("two", node), About("three", first)]);

        // Assert.
        Assert.Equal(["one", "two", "three"], problems.Select(problem => problem.RuleId));
    }

    [Fact]
    public void AFileThatDoesNotParse_IsNamedInItsSentence_AtItsLine()
    {
        // Arrange.
        var finding = new DislFinding("ansible.unreadable-yaml", DislReaderFinding.Unparseable, "error", "Duplicate key hosts", [], 3, "repeated.yml");

        // Act.
        var problem = Assert.Single(AnsibleFindings.For(Model, [finding]));

        // Assert.
        Assert.Equal("'repeated.yml' is not readable YAML: Duplicate key hosts", problem.Message);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Equal("ansible.unreadable-yaml", problem.RuleId);
        Assert.Equal(new DiagramProblemFileLocation("repeated.yml", 3), problem.Location);
    }

    [Fact]
    public void AFileThatDoesNotParse_ButNamesNoFile_IsNotPassedOn()
    {
        // Arrange.
        var finding = new DislFinding("ansible.unreadable-yaml", DislReaderFinding.Unparseable, "error", "The folder is not there.", [], null);

        // Act and assert.
        Assert.Empty(AnsibleFindings.For(Model, [finding]));
    }

    [Fact]
    public void AFindingWithoutALine_IsAtItsFile_AtLineZero()
    {
        // Arrange.
        var finding = new DislFinding("ansible.empty-role", "emptyRole", "warning", "empty", [Model.Nodes[0].Id], null, "roles/hollow");

        // Act.
        var problem = Assert.Single(AnsibleFindings.For(Model, [finding]));

        // Assert.
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Equal(new DiagramProblemFileLocation("roles/hollow"), problem.Location);
    }
}
