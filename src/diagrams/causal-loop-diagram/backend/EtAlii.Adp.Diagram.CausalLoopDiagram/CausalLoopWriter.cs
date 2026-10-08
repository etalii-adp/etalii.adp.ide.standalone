using System.Globalization;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// Every edit this module makes to a <c>.cld</c>, as a splice into the lines the parser read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Splices, never reserialization.</b> Each operation rewrites the one line its statement
/// occupies, or appends one, and leaves every other byte as the author wrote it - comments,
/// blank lines, spacing and order all survive. A writer that rebuilt the file from the model
/// would be correct about the content and wrong about everything else, and the whole point of a
/// line-oriented format is that adding a link is a one-line diff.
/// </para>
/// <para>
/// <b>Every operation refuses before it splices.</b> A refusal returns its sentence and touches
/// nothing, so a rejected edit cannot leave the document half-changed.
/// </para>
/// </remarks>
public static class CausalLoopWriter
{
    /// <summary>What a gesture is told when the thing it names is not in the document.</summary>
    public const string NoSuchVariable = "No variable of that name is in this diagram, so there is nothing to change.";

    /// <inheritdoc cref="NoSuchVariable" />
    public const string NoSuchLink = "No link between those two variables is in this diagram, so there is nothing to change.";

    /// <inheritdoc cref="NoSuchVariable" />
    public const string NoSuchLoop = "No loop of that identifier is in this diagram, so there is nothing to change.";

    /// <summary>A name that would collide with one already stated.</summary>
    private const string AlreadyDeclared = "A variable of that name is already declared in this diagram.";

    /// <summary>A name the format cannot round-trip.</summary>
    public const string UnusableName = "A name needs at least one character and no whitespace, because a statement is read as words on one line.";

    // ---- variables -------------------------------------------------------------------------

    /// <summary>
    /// A free (id, label) pair for a variable nobody named - what the toolbox drop and the
    /// canvas "Add variable" reach for now that neither asks the user to type one.
    /// </summary>
    /// <remarks>
    /// The id is whitespace-free because a statement is read as words on one line; the label is
    /// the same number worn as a word a reader recognises. Both are stable once assigned - a
    /// link refers to the id - so renaming is editing the label, never the id.
    /// </remarks>
    public static (string Id, string Label) NextVariableName(CausalLoopModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        for (var number = 1; number < 100000; number++)
        {
            var id = string.Create(CultureInfo.InvariantCulture, $"variable{number}");
            if (!model.Declares(id))
            {
                return (id, string.Create(CultureInfo.InvariantCulture, $"Variable {number}"));
            }
        }

        return ("variable", "Variable");
    }

    /// <summary>
    /// States a link and, in the same edit, claims every feedback loop the new link closes that
    /// no statement already names - the loop a two-variable pair forms, and the longer loop a new
    /// edge completes through variables already linked (causal-loop editing Requirement).
    /// </summary>
    /// <remarks>
    /// One document edit, so a link and the loops it creates undo together as the one gesture the
    /// user made. Only loops whose polarity the arrows actually decide are claimed, and each is
    /// named for that polarity - R for reinforcing, B for balancing - so an auto-claimed loop
    /// never lands already disagreeing with itself. A cycle a link states no polarity around is
    /// left for the author to claim, the way the validator leaves it: "unknown" is not "none".
    /// </remarks>
    public static string AddLinkAndClaimLoops(
        CausalLoopDocument document, CausalLoopModel model, string from, string to, CausalLoopPolarity polarity)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        if (!model.Declares(from) || !model.Declares(to))
        {
            return NoSuchVariable;
        }

        if (Find(model, from, to) is not null)
        {
            return "That link is already stated in this diagram.";
        }

        var newLink = new CausalLoopLink(from, to, polarity, false, false, null, "", new LineRange(0, 0));
        var claims = NewLoopClaims(model, newLink);

        var statements = new List<string> { LinkStatement(newLink) };
        statements.AddRange(claims.Select(claim =>
            LoopStatement(new CausalLoopLoop(claim.Identifier, "", claim.Variables, new LineRange(0, 0)))));

        document.Insert(AfterLast(document, model.Links.Select(l => l.Lines)), statements);
        return "";
    }

    /// <summary>
    /// The loops the new link closes that the document does not already claim, each with a fresh
    /// identifier named for the polarity the arrows give it. Computed on the model as it will be
    /// once the link is added, so a cycle that exists only because of the new edge is seen.
    /// </summary>
    private static IReadOnlyList<(string Identifier, IReadOnlyList<string> Variables)> NewLoopClaims(
        CausalLoopModel model, CausalLoopLink newLink)
    {
        var withLink = model with { Links = [.. model.Links, newLink] };
        var cycles = CycleFinder.Find(withLink).Cycles;

        var seen = model.Loops.Select(loop => CycleFinder.CanonicalSignature(loop.Variables)).ToHashSet(StringComparer.Ordinal);
        var reinforcing = 0;
        var balancing = 0;
        foreach (var loop in model.Loops)
        {
            _ = NextNumber(loop.Identifier, 'R', ref reinforcing);
            _ = NextNumber(loop.Identifier, 'B', ref balancing);
        }

        var claims = new List<(string Identifier, IReadOnlyList<string> Variables)>();
        foreach (var cycle in cycles)
        {
            if (!ClosesEdge(cycle, newLink.From, newLink.To))
            {
                continue;
            }

            var signature = CycleFinder.CanonicalSignature(cycle);
            if (!seen.Add(signature))
            {
                continue;
            }

            var identifier = LoopPolarity.Of(withLink, cycle) switch
            {
                LoopPolarityResult.Reinforcing => string.Create(CultureInfo.InvariantCulture, $"R{++reinforcing}"),
                LoopPolarityResult.Balancing => string.Create(CultureInfo.InvariantCulture, $"B{++balancing}"),
                _ => "",
            };
            if (identifier.Length == 0)
            {
                continue; // undecidable: the author claims it, not the tool
            }

            claims.Add((identifier, cycle));
        }

        return claims;
    }

    /// <summary>Whether a cycle listed in order runs along the directed edge <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static bool ClosesEdge(IReadOnlyList<string> cycle, string from, string to)
    {
        for (var index = 0; index < cycle.Count; index++)
        {
            var next = (index + 1) % cycle.Count;
            if (string.Equals(cycle[index], from, StringComparison.Ordinal) &&
                string.Equals(cycle[next], to, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tracks the highest number already used behind an identifier's prefix, so a fresh claim does not collide.</summary>
    private static int NextNumber(string identifier, char prefix, ref int highest)
    {
        if (identifier.Length > 1 &&
            char.ToUpperInvariant(identifier[0]) == prefix &&
            int.TryParse(identifier.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            highest = Math.Max(highest, number);
        }

        return highest;
    }

    /// <summary>Declares a new variable, appended after the last statement of its kind.</summary>
    public static string AddVariable(CausalLoopDocument document, CausalLoopModel model, string id, string label)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        if (!IsUsableName(id))
        {
            return UnusableName;
        }

        if (model.Declares(id))
        {
            return AlreadyDeclared;
        }

        document.Insert(AfterLast(document, model.Variables.Select(v => v.Lines)), [VariableStatement(id, label)]);
        return "";
    }

    /// <summary>Renames a variable, and every link and loop that refers to it.</summary>
    /// <remarks>
    /// The references move with the name deliberately. A rename that left them behind would turn
    /// every link through the variable into a dangling one, which the validator would then report
    /// as a defect the user did not make.
    /// </remarks>
    public static string RenameVariable(CausalLoopDocument document, CausalLoopModel model, string id, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        if (!IsUsableName(name))
        {
            return UnusableName;
        }

        var variable = model.Variables.FirstOrDefault(candidate => candidate.Id == id);
        if (variable is null)
        {
            return NoSuchVariable;
        }

        if (model.Declares(name))
        {
            return AlreadyDeclared;
        }

        // Bottom-most first, so an earlier splice cannot move a later line's index.
        var edits = new List<(LineRange Lines, string Text)>
        {
            (variable.Lines, VariableStatement(name, variable.Label)),
        };

        foreach (var link in model.Links.Where(link => link.From == id || link.To == id))
        {
            edits.Add((link.Lines, LinkStatement(link with
            {
                From = link.From == id ? name : link.From,
                To = link.To == id ? name : link.To,
            })));
        }

        foreach (var loop in model.Loops.Where(loop => loop.Variables.Contains(id, StringComparer.Ordinal)))
        {
            edits.Add((loop.Lines, LoopStatement(loop with
            {
                Variables = [.. loop.Variables.Select(member => member == id ? name : member)],
            })));
        }

        foreach ((LineRange lines, string text) in edits.OrderByDescending(edit => edit.Lines.Start))
        {
            document.Replace(lines, [text]);
        }

        return "";
    }

    /// <summary>Sets what a reader sees on a variable, leaving the identifier alone.</summary>
    /// <remarks>
    /// The label and the identifier are edited separately on purpose. The identifier is what
    /// links and loops refer to, so changing it moves references (see
    /// <see cref="RenameVariable"/>); the label is prose nothing refers to, so changing it is a
    /// one-line edit that can hold spaces and punctuation a name cannot.
    /// </remarks>
    public static string SetVariableLabel(
        CausalLoopDocument document, CausalLoopModel model, string id, string label)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        var variable = model.Variables.FirstOrDefault(candidate => candidate.Id == id);
        if (variable is null)
        {
            return NoSuchVariable;
        }

        document.Replace(variable.Lines, [VariableStatement(id, label)]);
        return "";
    }

    /// <summary>Removes a variable and everything that refers to it.</summary>
    public static string RemoveVariable(CausalLoopDocument document, CausalLoopModel model, string id)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        var variable = model.Variables.FirstOrDefault(candidate => candidate.Id == id);
        if (variable is null)
        {
            return NoSuchVariable;
        }

        // A link to a removed variable would dangle and a loop through it would name a path the
        // arrows no longer form, so both go with it. The count is stated before this runs.
        var lines = new List<LineRange> { variable.Lines };
        lines.AddRange(model.Links.Where(link => link.From == id || link.To == id).Select(link => link.Lines));
        lines.AddRange(model.Loops.Where(loop => loop.Variables.Contains(id, StringComparer.Ordinal)).Select(loop => loop.Lines));

        foreach (var range in lines.OrderByDescending(range => range.Start))
        {
            document.Remove(range);
        }

        return "";
    }

    /// <summary>How many statements <see cref="RemoveVariable"/> would take.</summary>
    public static int CountVariableRemoval(CausalLoopModel model, string id)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!model.Declares(id))
        {
            return 0;
        }

        return 1
            + model.Links.Count(link => link.From == id || link.To == id)
            + model.Loops.Count(loop => loop.Variables.Contains(id, StringComparer.Ordinal));
    }

    // ---- links -----------------------------------------------------------------------------

    /// <summary>States a causal link between two declared variables.</summary>
    public static string AddLink(
        CausalLoopDocument document, CausalLoopModel model, string from, string to, CausalLoopPolarity polarity)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        if (!model.Declares(from) || !model.Declares(to))
        {
            return NoSuchVariable;
        }

        if (Find(model, from, to) is not null)
        {
            return "That link is already stated in this diagram.";
        }

        var link = new CausalLoopLink(from, to, polarity, false, false, null, "", new LineRange(0, 0));
        document.Insert(AfterLast(document, model.Links.Select(l => l.Lines)), [LinkStatement(link)]);
        return "";
    }

    /// <summary>Changes what a link asserts.</summary>
    public static string SetLinkPolarity(
        CausalLoopDocument document, CausalLoopModel model, string from, string to, CausalLoopPolarity polarity) =>
        Rewrite(document, model, from, to, link => link with { Polarity = polarity });

    /// <summary>Marks a link's effect as delayed, or unmarks it.</summary>
    public static string SetLinkDelay(
        CausalLoopDocument document, CausalLoopModel model, string from, string to, bool delayed) =>
        Rewrite(document, model, from, to, link => link with { Delayed = delayed });

    /// <summary>
    /// Bows a link's arc to the other side of its chord, or puts it back.
    /// </summary>
    /// <remarks>
    /// Which side an arc bows to is normally this module's own decision, taken consistently from
    /// the direction of travel so that a two-variable loop draws as an ellipse rather than as one
    /// line with an arrowhead at each end. That rule is right almost always and occasionally
    /// unreadable - a link whose automatic side crosses another. This is the author overriding it
    /// for one link, and it is stated in the document so it survives a reopen.
    /// </remarks>
    public static string SetLinkCurvature(
        CausalLoopDocument document, CausalLoopModel model, string from, string to, bool flipped) =>
        Rewrite(document, model, from, to, link => link with { Flipped = flipped });

    /// <summary>Sets a link's weight, or clears it when <paramref name="weight"/> is null.</summary>
    public static string SetLinkWeight(
        CausalLoopDocument document, CausalLoopModel model, string from, string to, double? weight) =>
        Rewrite(document, model, from, to, link => link with { Weight = weight });

    /// <summary>Sets a link's label.</summary>
    public static string SetLinkLabel(
        CausalLoopDocument document, CausalLoopModel model, string from, string to, string label) =>
        Rewrite(document, model, from, to, link => link with { Label = label });

    /// <summary>Withdraws a link. Loops through it are left alone: a loop is a claim of its own.</summary>
    public static string RemoveLink(CausalLoopDocument document, CausalLoopModel model, string from, string to)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        var link = Find(model, from, to);
        if (link is null)
        {
            return NoSuchLink;
        }

        document.Remove(link.Lines);
        return "";
    }

    // ---- loops -----------------------------------------------------------------------------

    /// <summary>Claims a feedback loop through the named variables.</summary>
    public static string AddLoop(
        CausalLoopDocument document, CausalLoopModel model, string identifier, string name, IReadOnlyList<string> variables)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(variables);

        if (!IsUsableName(identifier))
        {
            return UnusableName;
        }

        if (model.Loops.Any(loop => loop.Identifier == identifier))
        {
            return "A loop of that identifier is already stated in this diagram.";
        }

        if (variables.Count < 1)
        {
            return "A loop runs through at least one variable.";
        }

        if (variables.FirstOrDefault(variable => !model.Declares(variable)) is { } missing)
        {
            return $"'{missing}' is not declared in this diagram, so a loop cannot run through it.";
        }

        var loop = new CausalLoopLoop(identifier, name, variables, new LineRange(0, 0));
        document.Insert(AfterLast(document, model.Loops.Select(l => l.Lines)), [LoopStatement(loop)]);
        return "";
    }

    /// <summary>Renames a loop, without touching what it runs through.</summary>
    public static string SetLoopName(CausalLoopDocument document, CausalLoopModel model, string identifier, string name) =>
        RewriteLoop(document, model, identifier, loop => loop with { Name = name });

    /// <summary>Restates a loop's identifier, without touching what it runs through.</summary>
    /// <remarks>
    /// The identifier is the author's claim about the loop's polarity, so this is how an author
    /// accepts the arithmetic when the two disagree - or insists on their own reading. The
    /// disagreement is never resolved for them (Requirement 3.3); this only lets them resolve it.
    /// </remarks>
    public static string SetLoopIdentifier(
        CausalLoopDocument document, CausalLoopModel model, string identifier, string replacement)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!IsUsableName(replacement))
        {
            return UnusableName;
        }

        if (replacement != identifier && model.Loops.Any(loop => loop.Identifier == replacement))
        {
            return "A loop of that identifier is already stated in this diagram.";
        }

        return RewriteLoop(document, model, identifier, loop => loop with { Identifier = replacement });
    }

    /// <summary>Changes the cycle a loop claims.</summary>
    public static string SetLoopMembership(
        CausalLoopDocument document, CausalLoopModel model, string identifier, IReadOnlyList<string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        if (variables.Count < 1)
        {
            return "A loop runs through at least one variable.";
        }

        if (variables.FirstOrDefault(variable => !model.Declares(variable)) is { } missing)
        {
            return $"'{missing}' is not declared in this diagram, so a loop cannot run through it.";
        }

        return RewriteLoop(document, model, identifier, loop => loop with { Variables = variables });
    }

    /// <summary>
    /// Withdraws a loop's claim - and nothing else.
    /// </summary>
    /// <remarks>
    /// The links it named survive. A link belongs to the diagram; a loop is a claim about a path
    /// through it, and withdrawing the claim does not withdraw the causality. Removing them too
    /// would delete assertions the user never asked to lose.
    /// </remarks>
    public static string RemoveLoop(CausalLoopDocument document, CausalLoopModel model, string identifier)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        var loop = model.Loops.FirstOrDefault(candidate => candidate.Identifier == identifier);
        if (loop is null)
        {
            return NoSuchLoop;
        }

        document.Remove(loop.Lines);
        return "";
    }

    // ---- statements ------------------------------------------------------------------------

    private static string VariableStatement(string id, string label) =>
        label.Length > 0 ? $"variable {id} \"{label}\"" : $"variable {id}";

    private static string LinkStatement(CausalLoopLink link)
    {
        var statement = $"link {link.From} -> {link.To}";

        if (link.Polarity != CausalLoopPolarity.Unstated)
        {
            statement += link.Polarity == CausalLoopPolarity.Positive ? " +" : " -";
        }

        if (link.Delayed)
        {
            statement += " delayed";
        }

        if (link.Flipped)
        {
            statement += " flipped";
        }

        if (link.Weight is { } weight)
        {
            statement += $" weight={weight.ToString(CultureInfo.InvariantCulture)}";
        }

        if (link.Label.Length > 0)
        {
            statement += $" \"{link.Label}\"";
        }

        return statement;
    }

    private static string LoopStatement(CausalLoopLoop loop) =>
        $"loop {loop.Identifier} \"{loop.Name}\" {string.Join(" ", loop.Variables)}";

    // ---- shared mechanics ------------------------------------------------------------------

    private static CausalLoopLink? Find(CausalLoopModel model, string from, string to) =>
        model.Links.FirstOrDefault(link =>
            string.Equals(link.From, from, StringComparison.Ordinal)
            && string.Equals(link.To, to, StringComparison.Ordinal));

    private static string Rewrite(
        CausalLoopDocument document,
        CausalLoopModel model,
        string from,
        string to,
        Func<CausalLoopLink, CausalLoopLink> change)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        var link = Find(model, from, to);
        if (link is null)
        {
            return NoSuchLink;
        }

        document.Replace(link.Lines, [LinkStatement(change(link))]);
        return "";
    }

    private static string RewriteLoop(
        CausalLoopDocument document,
        CausalLoopModel model,
        string identifier,
        Func<CausalLoopLoop, CausalLoopLoop> change)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        var loop = model.Loops.FirstOrDefault(candidate => candidate.Identifier == identifier);
        if (loop is null)
        {
            return NoSuchLoop;
        }

        document.Replace(loop.Lines, [LoopStatement(change(loop))]);
        return "";
    }

    /// <summary>
    /// Where a new statement of a kind goes: on the line after the last one of that kind, so
    /// variables stay with variables and links with links. An empty document appends at the end.
    /// </summary>
    private static int AfterLast(CausalLoopDocument document, IEnumerable<LineRange> existing)
    {
        var last = existing.Select(range => (int?)range.End).Max();
        return last is { } line ? line + 1 : document.Lines.Count;
    }

    /// <summary>A name the line-oriented format can round-trip: one word, at least one character.</summary>
    private static bool IsUsableName(string name) =>
        name.Length > 0 && !name.Any(char.IsWhiteSpace) && !name.Contains('"', StringComparison.Ordinal);
}
