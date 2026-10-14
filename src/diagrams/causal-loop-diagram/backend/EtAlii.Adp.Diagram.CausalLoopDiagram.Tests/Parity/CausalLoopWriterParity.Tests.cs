using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// Every command, now written by splices through the binding, means what the hand-written writer meant
/// (causal-loop-disl-fbl Requirement 5.2): for every document of the parity corpus, the command and
/// <see cref="CausalLoopWriter"/> refuse with the same sentence, or leave texts the hand-written parser
/// reads as the same diagram, and a new variable is given the same name. The bytes may differ - words no rule binds survive, new statements are
/// placed as the binding places them - but what the file states may not.
/// </summary>
public class CausalLoopWriterParityTests
{
    private const string Path = "parity.cld";

    public static TheoryData<string> Documents() => [.. CausalLoopTranscript.Corpus()];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryCommand_StatesWhatTheHandWrittenWriterStated(string relative)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(System.IO.Path.Combine(CausalLoopTranscript.SourceFolder, relative), TestContext.Current.CancellationToken);
        var model = CausalLoopParser.Parse(CausalLoopDocument.Parse(text)).Model;
        var differences = new List<string>();
        var applied = 0;
        var problems = Problems(text);

        foreach ((string name, Func<CausalLoopDocument, CausalLoopModel, string> written, Func<ICausalLoopDocumentStore, Task<CommandResult>> run) in Edits(model))
        {
            // Act.
            var document = CausalLoopDocument.Parse(text);
            var refusal = Thrown(() => written(document, model));
            var store = new CausalLoopMemoryStore { [Path] = text };
            var after = await Commanded(store, run);

            // Assert.
            var before = refusal.Length == 0 ? Stated(document.Text) : refusal.StartsWith("throws ", StringComparison.Ordinal) ? refusal : $"refused: {refusal}";
            // Where the hand-written writer wrote a line it cannot read back itself - a quoted name written
            // unquoted on a rename, which Q1 corrects - it is no reference for what the file should state.
            if (refusal.Length == 0 && Problems(document.Text) > problems) continue;
            if (!after.StartsWith("refused: ", StringComparison.Ordinal) && !after.StartsWith("throws ", StringComparison.Ordinal)) applied++;
            if (before != after) differences.Add($"{name}:\nthe hand-written writer {before}\nthe command             {after}");
        }

        Assert.Equal(CausalLoopWriter.NextVariableName(model), CausalLoopEdits.NextVariableName(model));
        Assert.True(differences.Count == 0, $"{relative}:\n{string.Join("\n\n", differences)}");
        Assert.True(applied >= 5, $"{relative}: only {applied} edits applied.");
    }

    /// <summary>What the hand-written writer returned, or the name of what it threw.</summary>
    private static string Thrown(Func<string> write)
    {
        try
        {
            return write();
        }
        catch (ArgumentException exception)
        {
            return $"throws {exception.GetType().Name}";
        }
    }

    /// <summary>What the command leaves stated, or its refusal, or the name of what it threw.</summary>
    private static async Task<string> Commanded(CausalLoopMemoryStore store, Func<ICausalLoopDocumentStore, Task<CommandResult>> run)
    {
        try
        {
            var result = await run(store);
            return result.IsSuccess ? Stated(store[Path]) : $"refused: {result.Error}";
        }
        catch (ArgumentException exception)
        {
            return $"throws {exception.GetType().Name}";
        }
    }

    private static int Problems(string text) => CausalLoopParser.Parse(CausalLoopDocument.Parse(text)).Problems.Count;

    /// <summary>
    /// What the hand-written parser reads <paramref name="text"/> as stating, without where or in which
    /// order it states it: a new statement is placed as the binding places it (B6).
    /// </summary>
    private static string Stated(string text)
    {
        var parsed = CausalLoopParser.Parse(CausalLoopDocument.Parse(text));
        var model = parsed.Model;
        return string.Join(
            "\n",
            model.Variables.Select(variable => $"variable {variable.Id} \"{variable.Label}\"")
                .Concat(model.Links.Select(link => $"link {link.From} {link.To} {link.Polarity} {link.Delayed} {link.Flipped} {link.Weight} \"{link.Label}\""))
                .Concat(model.Loops.Select(loop => $"loop {loop.Identifier} \"{loop.Name}\" {string.Join(" ", loop.Variables)}"))
                .Concat(parsed.Problems.Select(problem => $"problem {problem.Message}"))
                .Order(StringComparer.Ordinal));
    }

    /// <summary>One edit of every command, against the first elements of <paramref name="model"/>: the hand-written writer's, and the command's.</summary>
    private static IEnumerable<(string Name, Func<CausalLoopDocument, CausalLoopModel, string> Written, Func<ICausalLoopDocumentStore, Task<CommandResult>> Run)> Edits(CausalLoopModel model)
    {
        var token = TestContext.Current.CancellationToken;
        var variables = model.Variables.Select(variable => variable.Id).Distinct(StringComparer.Ordinal).ToList();
        var first = variables.FirstOrDefault() ?? "nobody";
        var second = variables.Skip(1).FirstOrDefault() ?? first;
        var link = model.Links.FirstOrDefault();
        (string from, string to) = link is null ? ("nobody", "nobody") : (link.From, link.To);
        var polarity = link?.Polarity == CausalLoopPolarity.Negative ? CausalLoopPolarity.Positive : CausalLoopPolarity.Negative;
        var loop = model.Loops.FirstOrDefault()?.Identifier ?? "R0";

        yield return ("add variable", (d, m) => CausalLoopWriter.AddVariable(d, m, "parity", "Parity"), store => new AddVariableCommandHandler(store).ExecuteAsync(new AddVariableCommand(Path, "parity", "Parity"), token));
        yield return ("variable label", (d, m) => CausalLoopWriter.SetVariableLabel(d, m, first, "Parity"), store => new SetVariableLabelCommandHandler(store).ExecuteAsync(new SetVariableLabelCommand(Path, first, "Parity"), token));
        yield return ("variable label cleared", (d, m) => CausalLoopWriter.SetVariableLabel(d, m, first, ""), store => new SetVariableLabelCommandHandler(store).ExecuteAsync(new SetVariableLabelCommand(Path, first, ""), token));
        yield return ("rename", (d, m) => CausalLoopWriter.RenameVariable(d, m, first, "parity"), store => new RenameVariableCommandHandler(store).ExecuteAsync(new RenameVariableCommand(Path, first, "parity"), token));
        yield return ("remove variable", (d, m) => CausalLoopWriter.RemoveVariable(d, m, first), store => new RemoveVariableCommandHandler(store).ExecuteAsync(new RemoveVariableCommand(Path, first), token));
        yield return ("add link", (d, m) => CausalLoopWriter.AddLinkAndClaimLoops(d, m, second, first, CausalLoopPolarity.Negative), store => new AddLinkCommandHandler(store).ExecuteAsync(new AddLinkCommand(Path, second, first, CausalLoopPolarity.Negative), token));
        yield return ("polarity", (d, m) => CausalLoopWriter.SetLinkPolarity(d, m, from, to, polarity), store => new SetLinkPolarityCommandHandler(store).ExecuteAsync(new SetLinkPolarityCommand(Path, from, to, polarity), token));
        yield return ("polarity unstated", (d, m) => CausalLoopWriter.SetLinkPolarity(d, m, from, to, CausalLoopPolarity.Unstated), store => new SetLinkPolarityCommandHandler(store).ExecuteAsync(new SetLinkPolarityCommand(Path, from, to, CausalLoopPolarity.Unstated), token));
        yield return ("delay", (d, m) => CausalLoopWriter.SetLinkDelay(d, m, from, to, link?.Delayed != true), store => new SetLinkDelayCommandHandler(store).ExecuteAsync(new SetLinkDelayCommand(Path, from, to, link?.Delayed != true), token));
        yield return ("curvature", (d, m) => CausalLoopWriter.SetLinkCurvature(d, m, from, to, link?.Flipped != true), store => new SetLinkCurvatureCommandHandler(store).ExecuteAsync(new SetLinkCurvatureCommand(Path, from, to, link?.Flipped != true), token));
        yield return ("weight", (d, m) => CausalLoopWriter.SetLinkWeight(d, m, from, to, 2.5), store => new SetLinkWeightCommandHandler(store).ExecuteAsync(new SetLinkWeightCommand(Path, from, to, 2.5), token));
        yield return ("weight cleared", (d, m) => CausalLoopWriter.SetLinkWeight(d, m, from, to, null), store => new SetLinkWeightCommandHandler(store).ExecuteAsync(new SetLinkWeightCommand(Path, from, to, null), token));
        yield return ("note", (d, m) => CausalLoopWriter.SetLinkLabel(d, m, from, to, "Parity"), store => new SetLinkLabelCommandHandler(store).ExecuteAsync(new SetLinkLabelCommand(Path, from, to, "Parity"), token));
        yield return ("remove link", (d, m) => CausalLoopWriter.RemoveLink(d, m, from, to), store => new RemoveLinkCommandHandler(store).ExecuteAsync(new RemoveLinkCommand(Path, from, to), token));
        yield return ("add loop", (d, m) => CausalLoopWriter.AddLoop(d, m, "R77", "Parity", [first]), store => new AddLoopCommandHandler(store).ExecuteAsync(new AddLoopCommand(Path, "R77", "Parity", [first]), token));
        yield return ("loop name", (d, m) => CausalLoopWriter.SetLoopName(d, m, loop, "Parity"), store => new SetLoopNameCommandHandler(store).ExecuteAsync(new SetLoopNameCommand(Path, loop, "Parity"), token));
        yield return ("loop identifier", (d, m) => CausalLoopWriter.SetLoopIdentifier(d, m, loop, "R78"), store => new SetLoopIdentifierCommandHandler(store).ExecuteAsync(new SetLoopIdentifierCommand(Path, loop, "R78"), token));
        yield return ("loop membership", (d, m) => CausalLoopWriter.SetLoopMembership(d, m, loop, [second, first]), store => new SetLoopMembershipCommandHandler(store).ExecuteAsync(new SetLoopMembershipCommand(Path, loop, [second, first]), token));
        yield return ("remove loop", (d, m) => CausalLoopWriter.RemoveLoop(d, m, loop), store => new RemoveLoopCommandHandler(store).ExecuteAsync(new RemoveLoopCommand(Path, loop), token));
    }
}
