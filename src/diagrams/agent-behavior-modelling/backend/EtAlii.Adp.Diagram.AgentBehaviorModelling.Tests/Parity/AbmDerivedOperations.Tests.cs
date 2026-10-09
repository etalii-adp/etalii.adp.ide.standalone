using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests.Parity;

/// <summary>
/// The edits run through the DISL definition write what the hand-written ones wrote (runtime plan step
/// S19b), byte for byte, and refuse what they refused in the same words: for every document of the
/// parity corpus, every node, every kind and every pair of nodes.
/// </summary>
public class AbmDerivedOperationsTests
{
    public static TheoryData<string> Documents() => [.. Texts().Select(document => document.Name)];

    private static IEnumerable<(string Name, string Text)> Texts() => AbmDisl.Corpus().Append(("inline/forest", AbmDislModelTests.Forest));

    private static string TextOf(string name) => Texts().Single(document => document.Name == name).Text;

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryAdd_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var text = TextOf(name);
        var model = AbmBody.Parse(text).Model;

        // Act and assert: as a last child, and placed, under every node and as a root.
        foreach (var kind in AbmNodeKinds.All.Select(kind => kind.Id))
        {
            foreach (var parent in model.Nodes)
            {
                Same(text, $"add {kind} under {parent.Id}",
                    document => HandWrittenAbmOperations.AddChild(document, document.Model.NodeOf(parent.Id)!, kind),
                    document => Derived(document, "addChild", parent.Id, new Dictionary<string, object?> { ["kind"] = AbmEdits.TypeOf(kind) }));
                Same(text, $"add {kind} first under {parent.Id}",
                    document => HandWrittenAbmOperations.AddHere(document, document.Model.NodeOf(parent.Id)!, 0, kind),
                    document => AbmDefinition.Apply(document, Here(document, kind), change => change is Specification.Fbl.Planning.ModelChange.Add add ? add with { ParentId = parent.Id, Index = 0 } : change));
            }

            Same(text, $"add {kind} as a root",
                document => HandWrittenAbmOperations.AddHere(document, null, 0, kind),
                document => AbmDefinition.Apply(document, Here(document, kind), change => change is Specification.Fbl.Planning.ModelChange.Add add ? add with { Index = 0 } : change));
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryRemoveRetypeAndNotes_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var text = TextOf(name);
        var model = AbmBody.Parse(text).Model;

        // Act and assert.
        foreach (var node in model.Nodes)
        {
            Same(text, $"remove {node.Id}",
                document => HandWrittenAbmOperations.Remove(document, document.Model.NodeOf(node.Id)!),
                document => AbmDefinition.Apply(document, DeletionPolicy.Changes(AbmDefinition.Specification, Element(document, node.Id), nested: true)));
            foreach (var notes in new[] { "", "One line.", "First.\n\nSecond." })
            {
                Same(text, $"notes of {node.Id}: {notes}",
                    document => HandWrittenAbmOperations.Notes(document, document.Model.NodeOf(node.Id)!, notes),
                    document => Derived(document, "editNotes", node.Id, new Dictionary<string, object?> { ["notes"] = notes }));
            }

            foreach (var kind in AbmNodeKinds.All.Select(kind => kind.Id).Where(kind => kind != node.Kind))
            {
                Same(text, $"retype {node.Id} to {kind}",
                    document => HandWrittenAbmOperations.Retype(document, document.Model.NodeOf(node.Id)!, kind),
                    document => AbmDefinition.Apply(document, RetypePolicy.Change(AbmDefinition.Specification, Element(document, node.Id), AbmEdits.TypeOf(kind), AbmDefinition.Env)));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryParentLine_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var text = TextOf(name);
        var model = AbmBody.Parse(text).Model;

        // Act and assert.
        foreach (var parent in model.Nodes)
        {
            foreach (var child in model.Nodes)
            {
                Same(text, $"line from {parent.Id} to {child.Id}",
                    document => HandWrittenAbmOperations.Connect(document, document.Model.NodeOf(parent.Id)!, document.Model.NodeOf(child.Id)!),
                    document => AbmDefinition.Apply(document, OperationInterpreter.Connect(
                        AbmDefinition.Specification, "Child", document.Disl.Diagram, Element(document, parent.Id), Element(document, child.Id), AbmDefinition.NewIds, AbmDefinition.Env)));
            }
        }
    }

    /// <summary>Runs both edits on a copy of <paramref name="text"/> each, and asserts the same outcome.</summary>
    private static void Same(string text, string what, Func<AbmBody, AbmEdit> handWritten, Func<AbmBody, AbmEdit> derived)
    {
        var expected = AbmBody.Parse(text);
        var actual = AbmBody.Parse(text);
        var expectedEdit = handWritten(expected);
        var actualEdit = derived(actual);
        Assert.True(expectedEdit == actualEdit, $"{what}: expected {expectedEdit.Refusal ?? "applied"}, actual {actualEdit.Refusal ?? "applied"}");
        Assert.True(expected.Text == actual.Text, $"{what}:\nexpected\n{expected.Text}\nactual\n{actual.Text}");
    }

    private static DislElement Element(AbmBody document, string id) => AbmDefinition.ElementOf(document.Disl.Diagram, id)!;

    private static AbmEdit Derived(AbmBody document, string operation, string id, Dictionary<string, object?> parameters) =>
        AbmDefinition.Apply(document, OperationInterpreter.Run(
            AbmDefinition.Specification, operation, document.Disl.Diagram, Element(document, id), AbmDefinition.NewIds, new DislInvocation(Parameters: parameters), AbmDefinition.Env));

    private static DislTransaction Here(AbmBody document, string kind) =>
        OperationInterpreter.Run(
            AbmDefinition.Specification, "addHere", document.Disl.Diagram, null, AbmDefinition.NewIds,
            new DislInvocation(Parameters: new Dictionary<string, object?> { ["kind"] = AbmEdits.TypeOf(kind) }), AbmDefinition.Env);
}
