using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests.Parity;

/// <summary>
/// The user functions of the bundled behavior model definition equal the code they describe, over the
/// parity corpus and over every kind: <c>keyword</c> is <see cref="AbmNodeKinds.KeywordOf"/>,
/// <c>startingLabel</c> is <see cref="AddAbmNodeCommandHandler.StartingLabel"/>, and <c>shorten</c>,
/// through the no-keyword finding it builds, is the parser's own.
/// </summary>
public class AbmDislFunctionsTests
{
    [Fact]
    public void EveryKind_HasOneType()
    {
        Assert.Equal(AbmNodeKinds.All.Select(kind => kind.Id).Order(StringComparer.Ordinal), AbmDisl.TypeOfKind.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Keyword_IsKeywordOf_OverTheCorpus()
    {
        var nodes = 0;
        foreach (var (name, text) in AbmDisl.Corpus())
        {
            var model = AbmDisl.Parse(text);
            var diagram = AbmDisl.DiagramOf(model);
            foreach (var (node, element) in model.Nodes.Zip(diagram.Nodes))
            {
                Assert.True(node.Keyword == (string?)AbmDisl.Evaluate("keyword(n)", new Dictionary<string, object?> { ["n"] = element }), $"{name} {node.Id}: {node.Keyword}");
                nodes++;
            }
        }
        Assert.True(nodes >= 25, $"Only {nodes} nodes were compared.");
    }

    [Fact]
    public void Keyword_IsKeywordOf_ForEveryKindAndCount()
    {
        foreach (var kind in AbmNodeKinds.All)
        {
            foreach (var count in new[] { 0, 1, 2, 3, 10, 999999 })
            {
                var node = new AbmNode("1", kind.Id, "Label", kind.Id == AbmNodeKinds.Retry ? count : 0, "", true, 0, null, 0, 0, 2, '-', "", null, []);
                var element = AbmDisl.NodeOf(new DislDiagram(AbmDisl.Specification), node, "1");
                Assert.Equal(node.Keyword, AbmDisl.Evaluate("keyword(n)", new Dictionary<string, object?> { ["n"] = element }));
            }
        }
    }

    [Fact]
    public void StartingLabel_IsTheLabelANewNodeIsGiven()
    {
        foreach (var kind in AbmNodeKinds.All)
        {
            Assert.Equal(AddAbmNodeCommandHandler.StartingLabel(kind.Id), AbmDisl.Evaluate("startingLabel(t)", new Dictionary<string, object?> { ["t"] = AbmDisl.TypeOfKind[kind.Id] }));
        }
    }

    /// <summary>
    /// The no-keyword finding's message, built on <c>shorten</c>, is the parser's, for every label length
    /// either side of the cut and for every label without a keyword in the corpus. The labels stay in
    /// the Basic Multilingual Plane: the code counts UTF-16 units and CEL counts code points, which
    /// differ beyond it.
    /// </summary>
    [Fact]
    public void Shorten_CutsTheNoKeywordFindingAsTheParserDoes()
    {
        var labels = Enumerable.Range(1, 90).Select(length => string.Concat(Enumerable.Range(0, length).Select(index => "abcdéfgh—jk"[index % 11])))
            .Append(new string('x', 57) + " yz and further")
            .ToList();
        var compared = 0;
        foreach (var text in labels.Select(label => $"## Behavior\n- {label}\n").Concat(AbmDisl.Corpus().Select(document => document.Text)))
        {
            var model = AbmDisl.Parse(text);
            var diagram = AbmDisl.DiagramOf(model);
            var expected = model.Problems.Where(problem => problem.RuleId == AbmRuleIds.NoKeyword).Select(problem => problem.Message).ToList();
            var actual = diagram.Nodes.Where(node => node.IsA("Do") && node.ValueOf("implicit") is true)
                .Select(node => (string)AbmDisl.EvaluateAt("/constraints/rules/0/message/cel", new Dictionary<string, object?> { ["self"] = node, ["diagram"] = diagram, ["env"] = null })!)
                .ToList();
            Assert.Equal(expected, actual);
            compared += expected.Count;
        }
        Assert.True(compared >= 91, $"Only {compared} findings were compared.");
    }
}
