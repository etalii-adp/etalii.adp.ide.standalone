using System.Text.Json;
using System.Text.Json.Serialization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// Every row commit, dialog and validation the providers now take from the definition answers as the hand-written
/// providers did (causal-loop-disl-fbl Requirements 3.5 and 7.2): the refusal a row's value meets, or the record it
/// dispatches with the value written; the dialog an entry asks, with its title, icon, field, initial value and
/// confirming label; and the sentence a dialog's value is rejected with. For every document of the parity corpus, with
/// the values the transcript's scripts commit and more of the same kinds, each held to <see cref="HandWrittenCausalLoop"/>.
/// </summary>
/// <remarks>
/// <para>
/// A document whose variables share a name made the hand-written provider throw (finding A7); a step it threw on has
/// nothing to compare, and the converted tool answers it, which the transcript records.
/// </para>
/// <para>
/// A record is compared by its JSON, so a list in it - a loop's members - is compared by its items.
/// </para>
/// </remarks>
public class CausalLoopDerivedCommitsTests
{
    private const string Path = "commits.cld";
    private const string Nobody = "nobody";
    private const string Unknown = "parity:unknown";

    /// <summary>A record's JSON, with a weight that is not finite written as its name rather than refused.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static TheoryData<string> Documents() => [.. CausalLoopCorpus.WalkedElementByElement()];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryRowCommit_IsTheHandWrittenOne(string relative)
    {
        // Arrange.
        var text = await Text(relative);
        var model = CausalLoopBody.Parse(text).Read();
        var differences = new List<string>();
        var compared = 0;

        foreach ((string elementId, string propertyId, string value) in Rows(text, model))
        {
            // Only the hand-written provider's own throw (finding A7) is passed over; anything else fails the test.
            DiagramPropertyChange handWritten;
            try
            {
                handWritten = HandWrittenCausalLoop.Change(model, Path, elementId, propertyId, value);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var expected = Describe(handWritten);

            // Act.
            var actual = await RowAsync(text, elementId, propertyId, value);

            // Assert.
            compared++;
            if (actual != expected) differences.Add($"set {elementId} {propertyId} = '{value}'\n  hand-written {expected}\n  definition   {actual}");
        }

        Assert.True(differences.Count == 0, $"{relative}: {differences.Count} of {compared} differ\n{string.Join("\n", differences)}");
        Assert.True(compared >= 10, $"{relative}: only {compared} row commits were compared.");
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryDialog_AndWhatItRejects_IsTheHandWrittenOne(string relative)
    {
        // Arrange.
        var text = await Text(relative);
        var model = CausalLoopBody.Parse(text).Read();
        var differences = new List<string>();
        var compared = 0;
        var variables = model.Variables.Select(variable => $"variable:{variable.Id}").Distinct(StringComparer.Ordinal).Append($"variable:{Nobody}").ToList();

        foreach ((string elementId, string actionId) in variables.SelectMany(variable => new[]
                 {
                     (variable, CausalLoopContextActionProvider.RenameVariableActionId),
                     (variable, CausalLoopContextActionProvider.AddLinkActionId),
                     (variable, CausalLoopContextActionProvider.AddLoopActionId),
                 }).Concat(model.Loops.Select(loop => (loop.Id, "causal-loop.rename-loop"))))
        {
            // Act.
            var expected = HandWrittenCausalLoop.Dialog(model, actionId, elementId);
            var actual = await DialogAsync(text, elementId, actionId);

            // Assert.
            compared++;
            if (actual != expected) differences.Add($"execute {actionId} on {elementId}\n  hand-written {expected}\n  definition   {actual}");
        }

        foreach ((string elementId, string actionId, string value) in Validations(model))
        {
            // Act.
            var expected = HandWrittenCausalLoop.Validation(model, actionId, value) ?? "accepted";
            var actual = await ValidationAsync(text, elementId, actionId, value);

            // Assert.
            compared++;
            if (actual != expected) differences.Add($"validate {actionId} on {elementId} = '{value}'\n  hand-written {expected}\n  definition   {actual}");
        }

        foreach ((string elementId, string actionId, string value) in RecordCommits(model))
        {
            // Act.
            var expected = Json(HandWrittenCausalLoop.Commit(model, Path, elementId, actionId, value));
            var actual = Json(await CommitAsync(text, elementId, actionId, value));

            // Assert.
            compared++;
            if (actual != expected) differences.Add($"commit {actionId} on {elementId} = '{value}'\n  hand-written {expected}\n  definition   {actual}");
        }

        Assert.True(differences.Count == 0, $"{relative}: {differences.Count} of {compared} differ\n{string.Join("\n", differences)}");
    }

    /// <summary>The row commits of the transcript's scripts, on every element of their kind, and more values of the same kinds.</summary>
    private static IEnumerable<(string ElementId, string PropertyId, string Value)> Rows(string text, CausalLoopModel model)
    {
        var names = model.Variables.Select(variable => variable.Id).Distinct(StringComparer.Ordinal).ToList();

        foreach (var link in model.Links.Select(link => link.Id).Distinct(StringComparer.Ordinal))
        {
            yield return (link, "causal-loop.link-label", "Parity note");
            yield return (link, "causal-loop.link-label", "");
            foreach (var weight in new[] { "heavy", "1e21", "0.000001", "", " 2.5 ", "-.5", "+3E-2", "1,5", "1.", "e5" })
            {
                yield return (link, CausalLoopContextPropertyProvider.LinkWeightProperty, weight);
            }

            foreach (var polarity in new[] { "sideways", "unstated", "positive", "negative", "+", "-", "s", "O", " Positive ", "" })
            {
                yield return (link, CausalLoopContextPropertyProvider.LinkPolarityProperty, polarity);
            }

            yield return (link, CausalLoopContextPropertyProvider.LinkDelayedProperty, "true");
            yield return (link, CausalLoopContextPropertyProvider.LinkDelayedProperty, "false");
            yield return (link, CausalLoopContextPropertyProvider.LinkFromProperty, Nobody);
        }

        var entry = new CausalLoopMemoryStore { [Path] = text }.Get(Path)!;
        foreach (var loop in model.Loops)
        {
            yield return (loop.Id, "causal-loop.loop-name", "Parity name");
            yield return (loop.Id, "causal-loop.loop-name", "Has \"quote\"");
            yield return (loop.Id, CausalLoopContextPropertyProvider.LoopVariablesProperty, "nobody → nowhere");
            foreach (var candidate in Candidates(entry, loop.Id))
            {
                yield return (loop.Id, CausalLoopContextPropertyProvider.LoopVariablesProperty, candidate);
            }

            foreach (var other in model.Loops.Where(other => other != loop).Take(1))
            {
                yield return (loop.Id, CausalLoopContextPropertyProvider.LoopIdentifierProperty, other.Identifier);
            }

            yield return (loop.Id, CausalLoopContextPropertyProvider.LoopIdentifierProperty, "");
            yield return (loop.Id, CausalLoopContextPropertyProvider.LoopIdentifierProperty, "B99");
            yield return (loop.Id, CausalLoopContextPropertyProvider.LoopComputedProperty, "reinforcing");
        }

        foreach (var name in names)
        {
            var variable = $"variable:{name}";
            yield return (variable, CausalLoopContextPropertyProvider.VariableLabelProperty, "Say \"hi\"");
            yield return (variable, CausalLoopContextPropertyProvider.VariableLabelProperty, "Parity label");
            yield return (variable, CausalLoopContextPropertyProvider.VariableLabelProperty, "");
            foreach (var other in names.Where(other => other != name).Take(1))
            {
                yield return (variable, CausalLoopContextPropertyProvider.VariableIdProperty, other);
            }

            foreach (var identifier in new[] { "has space", "", "parityId", "a\"b", name, "tab\there" })
            {
                yield return (variable, CausalLoopContextPropertyProvider.VariableIdProperty, identifier);
            }
        }

        yield return (Unknown, CausalLoopContextPropertyProvider.VariableLabelProperty, "Nobody");
        yield return ($"variable:{Nobody}", CausalLoopContextPropertyProvider.VariableLabelProperty, "Nobody");
    }

    /// <summary>The validations of the transcript's scripts, on every variable, and more values of the same kinds.</summary>
    private static IEnumerable<(string ElementId, string ActionId, string Value)> Validations(CausalLoopModel model)
    {
        var names = model.Variables.Select(variable => variable.Id).Distinct(StringComparer.Ordinal).ToList();
        foreach (var name in names)
        {
            var variable = $"variable:{name}";
            foreach (var label in new[] { "Say \"hi\"", "", "Parity renamed", "line\nbreak", "tab\there", "\u007f", "\u0085", " ", " " })
            {
                yield return (variable, CausalLoopContextActionProvider.RenameVariableActionId, label);
            }

            foreach (var to in names.Append(Nobody).Append(""))
            {
                yield return (variable, CausalLoopContextActionProvider.AddLinkActionId, to);
            }

            foreach (var identifier in model.Loops.Select(loop => loop.Identifier).Append("R99").Append(""))
            {
                yield return (variable, CausalLoopContextActionProvider.AddLoopActionId, identifier);
            }
        }

        yield return ($"variable:{Nobody}", CausalLoopContextActionProvider.AddLinkActionId, Nobody);
        yield return ($"variable:{Nobody}", CausalLoopContextActionProvider.AddLoopActionId, "R99");
    }

    /// <summary>The commits that still dispatch the record they always did: a variable's inline rename and a loop's rename.</summary>
    private static IEnumerable<(string ElementId, string ActionId, string Value)> RecordCommits(CausalLoopModel model)
    {
        foreach (var name in model.Variables.Select(variable => variable.Id).Distinct(StringComparer.Ordinal))
        {
            yield return ($"variable:{name}", CausalLoopContextActionProvider.RenameVariableActionId, "Parity renamed");
            yield return ($"variable:{name}", CausalLoopContextActionProvider.RenameVariableActionId, "Say \"hi\"");
        }

        foreach (var loop in model.Loops)
        {
            yield return (loop.Id, "causal-loop.rename-loop", "Parity loop");
        }
    }

    /// <summary>The choices the hand-written "Runs through" row offered; none where it threw (finding A7).</summary>
    private static IReadOnlyList<string> Candidates(Documents.DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string loop)
    {
        try
        {
            return HandWrittenCausalLoop.Rows(entry, loop).FirstOrDefault(row => row.Id == CausalLoopContextPropertyProvider.LoopVariablesProperty)?.Candidates ?? [];
        }
        catch (ArgumentException)
        {
            return [];
        }
    }

    private static async Task<string> RowAsync(string text, string elementId, string propertyId, string value)
    {
        var history = new RecordingHistory();
        var properties = new CausalLoopContextPropertyProvider(history, new CausalLoopMemoryStore { [Path] = text });
        var result = await properties.SetAsync(Target(elementId), propertyId, value, TestContext.Current.CancellationToken);
        if (history.Take() is { } command) return $"dispatches {Json(command)}";
        return result.IsSuccess ? "unchanged" : $"refused: {result.Error}";
    }

    private static async Task<ContextInputRequest?> DialogAsync(string text, string elementId, string actionId) =>
        await Actions(text, new RecordingHistory()).ExecuteAsync(Target(elementId), actionId, TestContext.Current.CancellationToken) is ContextExecutionRequiresInput input
            ? input.Request
            : null;

    private static async Task<string> ValidationAsync(string text, string elementId, string actionId, string value)
    {
        var result = await Actions(text, new RecordingHistory()).ValidateAsync(Target(elementId), actionId, value, TestContext.Current.CancellationToken);
        return result.Valid ? "accepted" : result.Reason;
    }

    private static async Task<ICommand?> CommitAsync(string text, string elementId, string actionId, string value)
    {
        var history = new RecordingHistory();
        await Actions(text, history).CommitAsync(Target(elementId), actionId, value, "", TestContext.Current.CancellationToken);
        return history.Take();
    }

    private static CausalLoopContextActionProvider Actions(string text, RecordingHistory history) =>
        new(new CausalLoopMemoryStore { [Path] = text }, history, new DiagramViewportRegistry());

    private static ContextTarget Target(string elementId) => ContextTarget.ForElement(Path, elementId, origin: Diagram.CausalLoop.Origin);

    private static string Describe(DiagramPropertyChange change) =>
        change.Command is { } command ? $"dispatches {Json(command)}" : change.Refusal.Length > 0 ? $"refused: {change.Refusal}" : "unchanged";

    private static string Json(ICommand? command) =>
        command is null ? "nothing" : $"{command.GetType().Name} {JsonSerializer.Serialize(command, command.GetType(), JsonOptions)}";

    private static Task<string> Text(string relative) =>
        File.ReadAllTextAsync(System.IO.Path.Combine(CausalLoopCorpus.SourceFolder, relative), TestContext.Current.CancellationToken);
}
