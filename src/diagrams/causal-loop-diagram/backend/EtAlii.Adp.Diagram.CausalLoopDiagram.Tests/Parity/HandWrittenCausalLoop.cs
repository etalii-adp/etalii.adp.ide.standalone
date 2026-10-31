using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// The palette, the context menus and the property rows as the module wrote them by hand before they were
/// derived from the DISL definition (causal-loop-disl-fbl task 5), and the dialogs, validations, row changes and
/// commits the providers computed by hand before they ran the definition (task 11), kept verbatim as the oracle
/// the derived ones are compared with, record for record.
/// </summary>
/// <remarks>
/// Copied from the hand-written <c>CausalLoopToolboxProvider.Items</c>, <c>CausalLoopContextActionProvider.DiscoverAsync</c> and
/// <c>CausalLoopContextPropertyProvider.DescribeAsync</c> after their body checks, and from the action provider's
/// <c>RunAsync</c>, <c>Validate</c> and <c>CommitCommand</c> and the property provider's <c>Change</c>, with the ids those
/// classes kept private written out. It goes with the parser (task 6).
/// </remarks>
internal static class HandWrittenCausalLoop
{
    private const string RenameLoopActionId = "causal-loop.rename-loop";
    private const string LinkToProperty = "causal-loop.link-to";
    private const string LinkLabelProperty = "causal-loop.link-label";
    private const string LoopNameProperty = "causal-loop.loop-name";

    private const string IdentityGroup = "Identity";
    private const string CausalityGroup = "Causality";
    private const string FeedbackGroup = "Feedback";

    private const string EndsAreTheIdentity =
        "A link is identified by the two variables it joins. Draw a new link on the canvas rather than changing an end here.";

    private const string ComputedFromTheArrows =
        "Counted from the polarity of the links around the cycle. Change an arrow to change this.";

    private const string CycleSeparator = " → ";

    /// <summary>The palette, as the hand-written <c>CausalLoopToolboxProvider.Items</c> listed it.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox { get; } =
    [
        new(
            "causal-loop.toolbox.variable",
            "Variable",
            "mdi-circle-outline",
            "Drop on the canvas to declare a quantity the system has.",
            CausalLoopContextActionProvider.AddVariableActionId),
        new(
            "causal-loop.toolbox.link",
            "Causal link",
            "mdi-arrow-right-thin",
            "Drop on a variable to state that it affects another. It arrives stating the same direction; change it to the opposite from the link's own menu.",
            CausalLoopContextActionProvider.AddLinkActionId),
        new(
            "causal-loop.toolbox.loop",
            "Feedback loop",
            "mdi-sync",
            "Drop on a variable to claim the loop that runs through it. Whether it reinforces or balances is counted from the links rather than taken from the label.",
            CausalLoopContextActionProvider.AddLoopActionId),
    ];

    /// <summary>The context menu of <paramref name="elementId"/>, as <c>DiscoverAsync</c> answered it after its body check.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string? elementId)
    {
        if (!entry.IsUsable)
        {
            return [];
        }

        if (CausalLoopSelection.RelationOf(elementId) is not null)
        {
            return [new ContextActionGroupDefinition(
                [new ContextActionDefinition(CausalLoopContextActionProvider.ConnectActionId, "Add link", "mdi-arrow-right-thin")])];
        }

        if (CausalLoopSelection.IsPlacement(elementId))
        {
            var arrangeable = entry.Model.Variables.Count > 1;

            return [new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(CausalLoopContextActionProvider.AddVariableActionId, "Add variable", "mdi-plus-circle-outline"),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.ArrangeActionId, "Arrange diagram", "mdi-graph-outline", null,
                    arrangeable,
                    "There is nothing to arrange until this diagram has two variables."),
            ])];
        }

        if (CausalLoopSelection.VariableOf(elementId) is { } variable)
        {
            return VariableActions(entry.Model, variable);
        }

        if (CausalLoopSelection.LinkOf(elementId) is { } ends)
        {
            return LinkActions(entry.Model, ends.From, ends.To);
        }

        if (CausalLoopSelection.LoopOf(elementId) is { } loop)
        {
            return LoopActions(entry.Model, loop);
        }

        return [];
    }

    /// <summary>The property rows of <paramref name="elementId"/>, as <c>DescribeAsync</c> answered them after its body check.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string? elementId)
    {
        if (!entry.IsUsable)
        {
            return [];
        }

        var model = entry.Model;

        if (VariableOf(model, elementId) is { } variable)
        {
            return
            [
                new ContextPropertyDefinition(CausalLoopContextPropertyProvider.VariableLabelProperty, "Label", variable.Label, Group: IdentityGroup),
                new ContextPropertyDefinition(CausalLoopContextPropertyProvider.VariableIdProperty, "Identifier", variable.Id, Group: IdentityGroup),
                new ContextPropertyDefinition(CausalLoopContextPropertyProvider.VariableStartProperty, "Starting level", WordOf(entry.Document.Text, variable.Id, "start") ?? "0.5", Group: "Flow page"),
                new ContextPropertyDefinition(CausalLoopContextPropertyProvider.VariablePushProperty, "Outside push", WordOf(entry.Document.Text, variable.Id, "push") ?? "0", Group: "Flow page"),
            ];
        }

        if (LinkOf(model, elementId) is { } link)
        {
            return
            [
                new ContextPropertyDefinition(
                    CausalLoopContextPropertyProvider.LinkFromProperty, "Cause (from)", link.From,
                    ReadOnlyReason: EndsAreTheIdentity, Group: IdentityGroup),
                new ContextPropertyDefinition(
                    LinkToProperty, "Effect (to)", link.To,
                    ReadOnlyReason: EndsAreTheIdentity, Group: IdentityGroup),
                new ContextPropertyDefinition(LinkLabelProperty, "Note", link.Label, Group: IdentityGroup),
                new ContextPropertyDefinition(
                    CausalLoopContextPropertyProvider.LinkPolarityProperty, "Polarity", NameOf(link.Polarity),
                    ContextPropertyEditor.Choice, Group: CausalityGroup,
                    Candidates: [NameOf(CausalLoopPolarity.Positive), NameOf(CausalLoopPolarity.Negative), NameOf(CausalLoopPolarity.Unstated)]),
                new ContextPropertyDefinition(
                    CausalLoopContextPropertyProvider.LinkDelayedProperty, "Delayed", link.Delayed ? "true" : "false",
                    ContextPropertyEditor.Toggle, Group: CausalityGroup),
                new ContextPropertyDefinition(
                    CausalLoopContextPropertyProvider.LinkWeightProperty, "Weight", WeightText(link.Weight), Group: CausalityGroup),
            ];
        }

        if (LoopOf(model, elementId) is { } loop)
        {
            var computed = LoopPolarity.Of(model, loop.Variables);
            var stated = loop.ClaimsReinforcing;
            var disagrees = stated is not null
                && computed is LoopPolarityResult.Reinforcing or LoopPolarityResult.Balancing
                && stated != (computed == LoopPolarityResult.Reinforcing);

            List<ContextPropertyDefinition> rows =
            [
                new(
                    CausalLoopContextPropertyProvider.LoopIdentifierProperty, "Identifier", loop.Identifier, Group: IdentityGroup),
                new(LoopNameProperty, "Name", loop.Name, Group: IdentityGroup),
                new(
                    CausalLoopContextPropertyProvider.LoopComputedProperty, "Computed polarity", NameOf(computed),
                    ReadOnlyReason: ComputedFromTheArrows, Group: FeedbackGroup),
            ];

            if (disagrees)
            {
                rows.Add(new ContextPropertyDefinition(
                    CausalLoopContextPropertyProvider.LoopStatedProperty,
                    "Stated polarity",
                    stated == true ? "reinforcing" : "balancing",
                    ReadOnlyReason: $"'{loop.Identifier}' claims this, and the arrows say {NameOf(computed)}. Neither is corrected for you: change the identifier or change an arrow.",
                    Group: FeedbackGroup));
            }

            rows.Add(new ContextPropertyDefinition(
                CausalLoopContextPropertyProvider.LoopVariablesProperty, "Runs through", CycleText(loop.Variables),
                ContextPropertyEditor.Choice, Group: FeedbackGroup,
                Candidates: [.. MembershipChoices(model, loop)]));

            return rows;
        }

        return [];
    }

    private static IReadOnlyList<ContextActionGroupDefinition> VariableActions(CausalLoopModel model, string variable)
    {
        var known = model.Declares(variable);
        var reason = known ? "" : CausalLoopWriter.NoSuchVariable;
        var count = CausalLoopWriter.CountVariableRemoval(model, variable);

        return
        [
            new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(CausalLoopContextActionProvider.AddLinkActionId, "Add link from here…", "mdi-arrow-right-thin", null, known, reason),
                new ContextActionDefinition(CausalLoopContextActionProvider.AddLoopActionId, "Claim a loop from here…", "mdi-sync", null, known, reason),
                new ContextActionDefinition(CausalLoopContextActionProvider.RenameVariableActionId, "Rename…", "mdi-rename-outline", null, known, reason),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.RemoveVariableActionId,
                    count > 1 ? $"Remove variable (with {count - 1} references)" : "Remove variable",
                    "mdi-delete-outline",
                    new ContextShortcutDefinition("Delete"),
                    known,
                    reason),
            ]),
        ];
    }

    private static IReadOnlyList<ContextActionGroupDefinition> LinkActions(CausalLoopModel model, string from, string to)
    {
        var link = model.Links.FirstOrDefault(candidate => candidate.From == from && candidate.To == to);
        var known = link is not null;
        var reason = known ? "" : CausalLoopWriter.NoSuchLink;

        return
        [
            new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.MakePositiveActionId, "Same direction (+)", "mdi-plus", null,
                    known && link!.Polarity != CausalLoopPolarity.Positive,
                    known ? "This link already states +." : reason),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.MakeNegativeActionId, "Opposite direction (−)", "mdi-minus", null,
                    known && link!.Polarity != CausalLoopPolarity.Negative,
                    known ? "This link already states −." : reason),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.ToggleDelayActionId,
                    known && link!.Delayed ? "Not delayed" : "Delayed",
                    "mdi-timer-sand", null, known, reason),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.FlipCurvatureActionId,
                    known && link!.Flipped ? "Curve back the other way" : "Flip the curve",
                    "mdi-vector-curve", null, known, reason),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.RemoveLinkActionId, "Remove link", "mdi-delete-outline",
                    new ContextShortcutDefinition("Delete"), known, reason),
            ]),
        ];
    }

    private static IReadOnlyList<ContextActionGroupDefinition> LoopActions(CausalLoopModel model, string identifier)
    {
        var known = model.Loops.Any(loop => loop.Identifier == identifier);
        var reason = known ? "" : CausalLoopWriter.NoSuchLoop;

        return
        [
            new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(RenameLoopActionId, "Rename loop…", "mdi-rename-outline", null, known, reason),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.RemoveLoopActionId, "Remove loop", "mdi-delete-outline",
                    new ContextShortcutDefinition("Delete"), known, reason),
            ]),
        ];
    }

    private static CausalLoopVariable? VariableOf(CausalLoopModel model, string? elementId) =>
        CausalLoopSelection.VariableOf(elementId) is { } id
            ? model.Variables.FirstOrDefault(variable => variable.Id == id)
            : null;

    private static CausalLoopLink? LinkOf(CausalLoopModel model, string? elementId) =>
        CausalLoopSelection.LinkOf(elementId) is { } ends
            ? model.Links.FirstOrDefault(link => link.From == ends.From && link.To == ends.To)
            : null;

    private static CausalLoopLoop? LoopOf(CausalLoopModel model, string? elementId) =>
        CausalLoopSelection.LoopOf(elementId) is { } identifier
            ? model.Loops.FirstOrDefault(loop => loop.Identifier == identifier)
            : null;

    private static IEnumerable<string> MembershipChoices(CausalLoopModel model, CausalLoopLoop loop)
    {
        yield return CycleText(loop.Variables);

        var claimed = model.Loops
            .Select(other => CycleFinder.CanonicalSignature(other.Variables))
            .ToHashSet(StringComparer.Ordinal);
        var members = loop.Variables.ToHashSet(StringComparer.Ordinal);

        foreach (var cycle in CycleFinder.Find(model).Cycles)
        {
            if (cycle.Any(members.Contains) && !claimed.Contains(CycleFinder.CanonicalSignature(cycle)))
            {
                yield return CycleText(cycle);
            }
        }
    }

    private static string CycleText(IReadOnlyList<string> variables) => string.Join(CycleSeparator, variables);

    /// <summary>The dialog <paramref name="actionId"/> asked for on <paramref name="elementId"/>, as <c>RunAsync</c> asked it; null for an action that asked none.</summary>
    public static ContextInputRequest? Dialog(CausalLoopModel model, string actionId, string elementId) => actionId switch
    {
        CausalLoopContextActionProvider.RenameVariableActionId => new ContextInputRequest(
            "Rename variable", "mdi-rename-outline", "Name", LabelOf(model, elementId), "Apply", elementId),
        CausalLoopContextActionProvider.AddLinkActionId => new ContextInputRequest("Add link", "mdi-arrow-right-thin", "To variable", "", "Apply"),
        CausalLoopContextActionProvider.AddLoopActionId => new ContextInputRequest("Claim a loop", "mdi-sync", "Identifier", NextLoopIdentifier(model), "Apply"),
        RenameLoopActionId => new ContextInputRequest(
            "Rename loop", "mdi-rename-outline", "Name", model.Loops.FirstOrDefault(loop => loop.Id == elementId)?.Name ?? "", "Apply"),
        _ => null,
    };

    /// <summary>Why <paramref name="value"/> was rejected for <paramref name="actionId"/>, as <c>Validate</c> rejected it; null when it was accepted.</summary>
    public static string? Validation(CausalLoopModel model, string actionId, string value) => actionId switch
    {
        // The label is written quoted, so it may hold spaces but not a quote or a line break,
        // and it must not be empty - an empty name leaves nothing to read on the variable.
        CausalLoopContextActionProvider.RenameVariableActionId when value.Length == 0 || value.Contains('"', StringComparison.Ordinal) || value.Any(char.IsControl) =>
            "A name needs at least one character, and cannot contain a quote or a line break.",
        CausalLoopContextActionProvider.AddLinkActionId when !model.Declares(value) =>
            $"'{value}' is not a variable in this diagram.",
        CausalLoopContextActionProvider.AddLoopActionId when model.Loops.Any(loop => loop.Identifier == value) =>
            "A loop of that identifier is already stated in this diagram.",
        _ => null,
    };

    /// <summary>
    /// The command <paramref name="actionId"/> committed with <paramref name="value"/> on <paramref name="elementId"/>,
    /// as <c>CommitCommand</c> made it, for the entries that now run the definition's operations; null for none.
    /// </summary>
    public static ICommand? Commit(CausalLoopModel model, string body, string elementId, string actionId, string value)
    {
        var variable = CausalLoopSelection.VariableOf(elementId);
        var link = CausalLoopSelection.LinkOf(elementId);

        return actionId switch
        {
            CausalLoopContextActionProvider.RenameVariableActionId when variable is not null => new SetVariableLabelCommand(body, variable, value),
            CausalLoopContextActionProvider.AddLinkActionId when variable is not null =>
                new AddLinkCommand(body, variable, value, CausalLoopPolarity.Positive),
            CausalLoopContextActionProvider.ConnectActionId when CausalLoopSelection.RelationOf(elementId) is { } ends =>
                new AddLinkCommand(body, ends.From, ends.To, CausalLoopPolarity.Positive),
            CausalLoopContextActionProvider.AddLoopActionId when variable is not null =>
                new AddLoopCommand(body, value, value, LoopThrough(model, variable)),
            CausalLoopContextActionProvider.ToggleDelayActionId when link is not null =>
                new SetLinkDelayCommand(body, link.Value.From, link.Value.To, !IsDelayed(model, link.Value)),
            CausalLoopContextActionProvider.FlipCurvatureActionId when link is not null =>
                new SetLinkCurvatureCommand(body, link.Value.From, link.Value.To, !IsFlipped(model, link.Value)),
            RenameLoopActionId when CausalLoopSelection.LoopOf(elementId) is { } loop => new SetLoopNameCommand(body, loop, value),
            _ => null,
        };
    }

    /// <summary>The value of the word <paramref name="key"/> on the line that declares <paramref name="variable"/>; null for none.</summary>
    private static string? WordOf(string text, string variable, string key) =>
        text.Split('\n')
            .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(words => words.Length >= 2 && words[0] == "variable" && words[1] == variable)
            .SelectMany(words => words)
            .FirstOrDefault(word => word.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..];

    /// <summary>What setting <paramref name="propertyId"/> to <paramref name="value"/> on <paramref name="elementId"/> was, as <c>Change</c> answered it.</summary>
    public static DiagramPropertyChange Change(CausalLoopModel model, string body, string elementId, string propertyId, string value)
    {
        // Refused before any command is made, so the message names the value the user typed
        // rather than reporting a command failure - and so nothing unreadable is ever written.
        if (propertyId == CausalLoopContextPropertyProvider.LinkWeightProperty && ParseWeight(value) is null && value.Trim().Length > 0)
        {
            return DiagramPropertyChange.Refused(
                $"'{value}' is not a number. A weight is this link's starting strength in a flow page; leave it empty to remove it.");
        }

        if (propertyId == CausalLoopContextPropertyProvider.LinkPolarityProperty && ParsePolarity(value) is null)
        {
            return DiagramPropertyChange.Refused($"'{value}' is not a polarity a link can state.");
        }

        if (propertyId == CausalLoopContextPropertyProvider.LoopVariablesProperty && LoopOf(model, elementId) is { } chosenFor)
        {
            if (CycleChosen(model, chosenFor, value) is null)
            {
                return DiagramPropertyChange.Refused(
                    $"'{value}' is not a cycle this loop can run through: choose one the arrows form through its variables that no other loop claims.");
            }

            // Choosing what is already there changes nothing, so it lands nothing on the undo stack.
            if (value == CycleText(chosenFor.Variables))
            {
                return DiagramPropertyChange.Unchanged;
            }
        }

        // The identifier is written as one word on the statement line, so it cannot hold
        // whitespace or a quote; the label may, because it is quoted. Refused before writing.
        if (propertyId == CausalLoopContextPropertyProvider.VariableIdProperty && (value.Length == 0 || value.Any(char.IsWhiteSpace) || value.Contains('"', StringComparison.Ordinal)))
        {
            return DiagramPropertyChange.Refused(CausalLoopEdits.UnusableName);
        }

        // Through the project's history and out through the delta stream - never written here.
        return CommandFor(model, body, elementId, propertyId, value) is { } command
            ? DiagramPropertyChange.By(command)
            : DiagramPropertyChange.Refused($"'{propertyId}' cannot be edited on this selection.");
    }

    private static ICommand? CommandFor(CausalLoopModel model, string body, string elementId, string propertyId, string value)
    {
        if (VariableOf(model, elementId) is { } variable)
        {
            return propertyId switch
            {
                CausalLoopContextPropertyProvider.VariableLabelProperty => new SetVariableLabelCommand(body, variable.Id, value),
                // The identifier is what links and loops refer to, so restating it carries them.
                CausalLoopContextPropertyProvider.VariableIdProperty => new RenameVariableCommand(body, variable.Id, value),
                _ => null,
            };
        }

        if (LinkOf(model, elementId) is { } link)
        {
            return propertyId switch
            {
                LinkLabelProperty => new SetLinkLabelCommand(body, link.From, link.To, value),
                CausalLoopContextPropertyProvider.LinkWeightProperty => new SetLinkWeightCommand(body, link.From, link.To, ParseWeight(value)),
                CausalLoopContextPropertyProvider.LinkDelayedProperty => new SetLinkDelayCommand(
                    body, link.From, link.To, string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)),
                CausalLoopContextPropertyProvider.LinkPolarityProperty when ParsePolarity(value) is { } polarity =>
                    new SetLinkPolarityCommand(body, link.From, link.To, polarity),
                _ => null,
            };
        }

        if (LoopOf(model, elementId) is { } loop)
        {
            return propertyId switch
            {
                LoopNameProperty => new SetLoopNameCommand(body, loop.Identifier, value),

                // The identifier is the claim, so setting it is how an author accepts the
                // arithmetic - or insists on their own reading.
                CausalLoopContextPropertyProvider.LoopIdentifierProperty => new SetLoopIdentifierCommand(body, loop.Identifier, value),
                CausalLoopContextPropertyProvider.LoopVariablesProperty when CycleChosen(model, loop, value) is { } cycle =>
                    new SetLoopMembershipCommand(body, loop.Identifier, cycle),
                _ => null,
            };
        }

        return null;
    }

    /// <summary>The cycle a "Runs through" value names, or null where the row did not offer it.</summary>
    private static IReadOnlyList<string>? CycleChosen(CausalLoopModel model, CausalLoopLoop loop, string value) =>
        MembershipCycles(model, loop).FirstOrDefault(choice => choice.Text == value).Cycle;

    /// <summary>
    /// What a loop may run through, each with the text the row shows for it: the loop's own cycle
    /// first, as the author wrote it, then each elementary cycle through any of its variables that
    /// no loop claims yet, in the cycle finder's stable order.
    /// </summary>
    private static List<(string Text, IReadOnlyList<string> Cycle)> MembershipCycles(CausalLoopModel model, CausalLoopLoop loop)
    {
        List<(string Text, IReadOnlyList<string> Cycle)> choices = [(CycleText(loop.Variables), loop.Variables)];

        var claimed = model.Loops
            .Select(other => CycleFinder.CanonicalSignature(other.Variables))
            .ToHashSet(StringComparer.Ordinal);
        var members = loop.Variables.ToHashSet(StringComparer.Ordinal);

        foreach (var cycle in CycleFinder.Find(model).Cycles)
        {
            if (cycle.Any(members.Contains) && !claimed.Contains(CycleFinder.CanonicalSignature(cycle)))
            {
                choices.Add((CycleText(cycle), cycle));
            }
        }

        return choices;
    }

    /// <summary>The weight a row was given, or null for none - which includes blank.</summary>
    private static double? ParseWeight(string value) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var weight)
            ? weight
            : null;

    private static CausalLoopPolarity? ParsePolarity(string value) => value.Trim().ToUpperInvariant() switch
    {
        "POSITIVE" or "+" or "S" => CausalLoopPolarity.Positive,
        "NEGATIVE" or "-" or "O" => CausalLoopPolarity.Negative,
        "UNSTATED" or "" => CausalLoopPolarity.Unstated,
        _ => null,
    };

    /// <summary>The visible name a variable id names, for seeding the inline rename with what is there.</summary>
    private static string LabelOf(CausalLoopModel model, string? elementId) =>
        CausalLoopSelection.VariableOf(elementId) is { } id
            ? model.Variables.FirstOrDefault(variable => variable.Id == id)?.Display ?? ""
            : "";

    private static bool IsDelayed(CausalLoopModel model, (string From, string To) link) =>
        model.Links.FirstOrDefault(candidate => candidate.From == link.From && candidate.To == link.To)?.Delayed == true;

    private static bool IsFlipped(CausalLoopModel model, (string From, string To) link) =>
        model.Links.FirstOrDefault(candidate => candidate.From == link.From && candidate.To == link.To)?.Flipped == true;

    /// <summary>The shortest cycle through a variable, so a claimed loop starts from something real.</summary>
    private static IReadOnlyList<string> LoopThrough(CausalLoopModel model, string variable) =>
        CycleFinder.Find(model).Cycles
            .Where(cycle => cycle.Contains(variable, StringComparer.Ordinal))
            .OrderBy(cycle => cycle.Count)
            .FirstOrDefault() ?? [variable];

    /// <summary>The next free loop identifier, so a new claim does not collide with an existing one.</summary>
    private static string NextLoopIdentifier(CausalLoopModel model)
    {
        for (var number = 1; number < 1000; number++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"R{number}");
            if (model.Loops.All(loop => loop.Identifier != candidate))
            {
                return candidate;
            }
        }

        return "R";
    }

    private static string WeightText(double? weight) =>
        weight is { } value ? value.ToString(CultureInfo.InvariantCulture) : "";

    private static string NameOf(CausalLoopPolarity polarity) => polarity switch
    {
        CausalLoopPolarity.Positive => "positive",
        CausalLoopPolarity.Negative => "negative",
        _ => "unstated",
    };

    private static string NameOf(LoopPolarityResult polarity) => polarity switch
    {
        LoopPolarityResult.Reinforcing => "reinforcing",
        LoopPolarityResult.Balancing => "balancing",
        _ => CausalLoopContextPropertyProvider.Undecidable,
    };
}
