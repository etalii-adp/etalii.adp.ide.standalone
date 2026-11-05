using System.Globalization;
using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// The C4 edits as the module wrote them by line before c4-disl-fbl task 15, when each became a model change the
/// library plans into splices: the set handlers' bodies, the add and its removal, and <c>C4Placement</c>, kept
/// verbatim but for the inverse commands, which undo no longer runs, as the oracle the library's edits are compared
/// with (<see cref="C4DerivedOperationsTests"/>).
/// </summary>
internal static class HandWrittenC4Edits
{
    /// <summary><c>SetElementNameCommandHandler</c>'s edit.</summary>
    public static EditOutcome SetName(LineDocument body, C4Workspace model, string elementId, string name)
    {
        var element = model.Find(elementId);
        if (element is not null && element.Kind is C4ElementKind.ContainerInstance or C4ElementKind.SoftwareSystemInstance)
        {
            return EditOutcome.Refused(
                $"A {element.Kind.ToString().ToLowerInvariant()} has no name in C4; it stands for what it is an instance of.");
        }

        return EditElement(body, model, elementId, 0, name);
    }

    /// <summary><c>SetElementDescriptionCommandHandler</c>'s edit.</summary>
    public static EditOutcome SetDescription(LineDocument body, C4Workspace model, string elementId, string description) =>
        EditElement(body, model, elementId, 1, description);

    /// <summary><c>SetElementTechnologyCommandHandler</c>'s edit.</summary>
    public static EditOutcome SetTechnology(LineDocument body, C4Workspace model, string elementId, string technology)
    {
        var element = model.Find(elementId);
        if (element is not null && element.Kind is not (C4ElementKind.Container or C4ElementKind.Component
            or C4ElementKind.DeploymentNode or C4ElementKind.InfrastructureNode))
        {
            return EditOutcome.Refused(
                $"A {element.Kind.ToString().ToLowerInvariant()} has no technology in C4; only containers, components and infrastructure do.");
        }

        return EditElement(body, model, elementId, 2, technology);
    }

    /// <summary><c>SetRelationshipDescriptionCommandHandler</c>'s edit.</summary>
    public static EditOutcome SetRelationshipDescription(LineDocument body, C4Workspace model, string relationshipId, string description) =>
        EditRelationship(body, model, relationshipId, 0, description);

    /// <summary><c>SetRelationshipTechnologyCommandHandler</c>'s edit.</summary>
    public static EditOutcome SetRelationshipTechnology(LineDocument body, C4Workspace model, string relationshipId, string technology) =>
        EditRelationship(body, model, relationshipId, 1, technology);

    /// <summary><c>AddC4ElementCommandHandler</c>'s edit, after its name check.</summary>
    public static EditOutcome Add(LineDocument body, C4Workspace model, C4ElementKind kind, string name, string parentId, string identifier)
    {
        name = name.Trim();
        if (Refuse(model, kind, parentId) is { } refusal)
        {
            return EditOutcome.Refused(refusal);
        }

        identifier = identifier.Length > 0 ? identifier : IdentifierFor(model, name);
        if (model.Find(identifier) is not null)
        {
            return EditOutcome.Refused($"This model already has something called '{identifier}'.");
        }

        return TryInsert(body, model, kind, name, identifier, parentId, out var error)
            ? EditOutcome.Applied
            : EditOutcome.Refused(error);
    }

    /// <summary><c>RemoveC4ElementCommandHandler</c>'s edit.</summary>
    public static EditOutcome Remove(LineDocument body, C4Workspace model, string identifier, string collapseParent)
    {
        if (model.Find(identifier) is not { } element)
        {
            return EditOutcome.Refused("That element is no longer in this model.");
        }

        var parent = collapseParent.Length > 0 ? model.Find(collapseParent) : null;
        if (!TryRemove(body, element, out var error))
        {
            return EditOutcome.Refused(error);
        }

        if (parent is not null)
        {
            CollapseEmptyBlock(body, parent);
        }

        return EditOutcome.Applied;
    }

    /// <summary><c>C4EditHandler.EditElement</c>: one argument of the line the element was declared on.</summary>
    private static EditOutcome EditElement(LineDocument document, C4Workspace workspace, string elementId, int argumentIndex, string value)
    {
        var element = workspace.Find(elementId);
        if (element is null)
        {
            return EditOutcome.Refused($"'{elementId}' is not in this model any more.");
        }

        var line = document.Lines[(int)element.Line - 1].Text;
        var keyword = KeywordIndexOf(line);
        if (keyword < 0)
        {
            return EditOutcome.Refused($"Line {element.Line} does not look like an element declaration any more.");
        }

        document.ReplaceLine(element.Line, C4LineSurgery.ReplaceArgument(line, keyword, argumentIndex, value));
        return EditOutcome.Applied;
    }

    /// <summary><c>C4EditHandler.EditRelationship</c>: the same, for a relationship, whose arguments follow the destination.</summary>
    private static EditOutcome EditRelationship(LineDocument document, C4Workspace workspace, string relationshipId, int argumentIndex, string value)
    {
        var relationship = workspace.Relationships.FirstOrDefault(candidate => candidate.Id == relationshipId);
        if (relationship is null)
        {
            return EditOutcome.Refused("That relationship is not in this model any more.");
        }

        var line = document.Lines[(int)relationship.Line - 1].Text;
        var tokens = C4LineSurgery.SplitWithSpans(line);
        var arrow = -1;
        for (var index = 0; index < tokens.Count; index++)
        {
            if (!tokens[index].Quoted && tokens[index].Value == "->")
            {
                arrow = index;
                break;
            }
        }

        if (arrow < 0 || arrow + 1 >= tokens.Count)
        {
            return EditOutcome.Refused($"Line {relationship.Line} does not look like a relationship any more.");
        }

        document.ReplaceLine(relationship.Line, C4LineSurgery.ReplaceArgument(line, arrow + 1, argumentIndex, value));
        return EditOutcome.Applied;
    }

    private static int KeywordIndexOf(string line)
    {
        var tokens = C4LineSurgery.SplitWithSpans(line);
        if (tokens is [_, { Value: "=" }, _, ..])
        {
            return 2;
        }

        return tokens.Count >= 1 ? 0 : -1;
    }

    /// <summary>Why this kind cannot go there, or null when it can.</summary>
    public static string? Refuse(C4Workspace workspace, C4ElementKind kind, string parentId)
    {
        var parent = parentId.Length == 0 ? null : workspace.Find(parentId);
        if (parentId.Length > 0 && parent is null)
        {
            return "The element it would go inside is no longer in this model.";
        }

        return kind switch
        {
            // The two things that stand on their own. Dropping one onto something else is a
            // gesture that means nothing rather than one that is forbidden, so it is taken as
            // "at the top" rather than refused.
            C4ElementKind.Person or C4ElementKind.SoftwareSystem => null,

            C4ElementKind.Container when parent is null =>
                "A container lives inside a software system. Drop it on one.",
            C4ElementKind.Container when parent.Kind != C4ElementKind.SoftwareSystem =>
                $"A container lives inside a software system, not inside a {Spell(parent.Kind)}.",

            C4ElementKind.Component when parent is null =>
                "A component lives inside a container. Drop it on one.",
            C4ElementKind.Component when parent.Kind != C4ElementKind.Container =>
                $"A component lives inside a container, not inside a {Spell(parent.Kind)}.",

            C4ElementKind.Container or C4ElementKind.Component => null,

            // Deployment nodes, infrastructure nodes and instances live in a deployment
            // environment, which is a different piece of surgery and a different gesture.
            _ => $"Adding a {Spell(kind)} from the toolbox is not supported yet.",
        };
    }

    /// <summary>
    /// Writes the declaration, opening a block on the parent if it has none.
    /// </summary>
    private static bool TryInsert(
        LineDocument document,
        C4Workspace workspace,
        C4ElementKind kind,
        string name,
        string identifier,
        string parentId,
        out string error)
    {
        error = "";
        var parent = parentId.Length == 0 ? null : workspace.Find(parentId);

        if (parent is null)
        {
            var endOfModel = EndOfBlock(document, line =>
                line.Code.Equals("model {", StringComparison.OrdinalIgnoreCase)
                || line.Code.StartsWith("model {", StringComparison.OrdinalIgnoreCase));
            if (endOfModel is null)
            {
                error = "This document has no model block to add an element to.";
                return false;
            }

            document.InsertLine(endOfModel.Value, Declare(kind, name, identifier, indent: 8));
            return true;
        }

        // C4Line is a value type, so FirstOrDefault hands back a blank line rather than null.
        // Lifted to a nullable so "no such line" is distinguishable from "line zero".
        var parentLine = document.CodeLines()
            .Where(line => line.Number == parent.Line)
            .Select(line => (C4Line?)line)
            .FirstOrDefault();
        if (parentLine is not { } declaration)
        {
            error = "The element it would go inside is no longer in this model.";
            return false;
        }

        var indent = IndentOf(declaration.Text) + 4;
        if (declaration.Code.EndsWith('{'))
        {
            // Already has a block: the new line goes just above its closing brace.
            var close = EndOfBlock(document, line => line.Number == parent.Line);
            if (close is null)
            {
                error = "That element's block is not closed, so nothing can be added to it.";
                return false;
            }

            document.InsertLine(close.Value, Declare(kind, name, identifier, indent));
            return true;
        }

        // No block yet. The declaration line gains a brace, the child goes under it, and a
        // closing brace follows - three edits that together are still only the lines this
        // change touches (Requirement 3.2).
        document.ReplaceLine(declaration.Number, declaration.Text.TrimEnd() + " {");
        document.InsertLine(declaration.Number + 1, Declare(kind, name, identifier, indent));
        document.InsertLine(declaration.Number + 2, new string(' ', IndentOf(declaration.Text)) + "}");
        return true;
    }

    /// <summary>
    /// Closes a block this module opened, putting the parent back on one line.
    /// </summary>
    /// <remarks>
    /// The other half of the surgery above. Undoing an add that gave a container its first
    /// braces has to take the braces away too - leaving <c>container "Web" … { }</c> behind
    /// would mean undo did not restore what was there, which is the one thing undo is for.
    /// Leaves the block as it is if anything at all is left inside, so a concurrent edit is never
    /// swallowed: an empty block parses and round-trips, so nobody has to be told it stayed.
    /// </remarks>
    private static void CollapseEmptyBlock(LineDocument document, C4Element parent)
    {
        var open = document.CodeLines()
            .Where(line => line.Number == parent.Line && line.Code.EndsWith('{'))
            .Select(line => (C4Line?)line)
            .FirstOrDefault();
        if (open is not { } declaration)
        {
            return;
        }

        var close = EndOfBlock(document, line => line.Number == parent.Line);
        if (close is null)
        {
            return;
        }

        // Any text at all, not just code. `IsBlank` is true for a line carrying only a comment,
        // so asking it here would let this delete a comment the user wrote inside the block -
        // exactly the thing Requirement 3.3 forbids and that line surgery exists to avoid.
        // Refusing to collapse is the safe answer: it leaves an empty block, which parses and
        // round-trips, where the alternative loses somebody's writing.
        var hasContent = document.CodeLines()
            .Any(line => line.Number > declaration.Number && line.Number < close.Value && line.Text.Trim().Length > 0);
        if (hasContent)
        {
            return;
        }

        document.RemoveLines(declaration.Number + 1, close.Value);
        document.ReplaceLine(declaration.Number, declaration.Text.TrimEnd().TrimEnd('{').TrimEnd());
    }

    /// <summary>
    /// Removes an element declared on one line. Undoing an add is the only caller, so an element
    /// with a block of its own is refused rather than taking its children with it.
    /// </summary>
    private static bool TryRemove(LineDocument document, C4Element element, out string error)
    {
        error = "";
        var line = document.CodeLines()
            .Where(candidate => candidate.Number == element.Line)
            .Select(candidate => (C4Line?)candidate)
            .FirstOrDefault();
        if (line is not { } declaration)
        {
            error = "That element is no longer in this model.";
            return false;
        }

        if (declaration.Code.EndsWith('{'))
        {
            error = "That element has things inside it, so removing it here would take them too.";
            return false;
        }

        document.RemoveLines(declaration.Number, declaration.Number);
        return true;
    }

    /// <summary>An identifier nothing in the model is using yet, derived from the name.</summary>
    public static string IdentifierFor(C4Workspace workspace, string name)
    {
        var builder = new StringBuilder();
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(builder.Length == 0 ? char.ToLowerInvariant(character) : character);
            }
        }

        var candidate = builder.Length == 0 ? "element" : builder.ToString();
        if (char.IsDigit(candidate[0]))
        {
            candidate = "_" + candidate;
        }

        if (workspace.Find(candidate) is null)
        {
            return candidate;
        }

        // `web`, `web2`, `web3`. Numbered rather than randomised, because an identifier is what
        // a human reads in the diff.
        for (var suffix = 2; ; suffix++)
        {
            var numbered = candidate + suffix.ToString(CultureInfo.InvariantCulture);
            if (workspace.Find(numbered) is null)
            {
                return numbered;
            }
        }
    }

    /// <summary>One declaration line: `identifier = keyword "Name" "description" ["technology"]`.</summary>
    private static string Declare(C4ElementKind kind, string name, string identifier, int indent)
    {
        var keyword = kind switch
        {
            C4ElementKind.Person => "person",
            C4ElementKind.SoftwareSystem => "softwareSystem",
            C4ElementKind.Container => "container",
            _ => "component",
        };

        // A description every element needs, and a technology only what C4 gives one to. Both
        // are placeholders the user replaces - written rather than left out, because an element
        // with no description is a warning the moment it appears, and greeting someone with a
        // problem they did not cause is a poor introduction.
        var declaration = $"{new string(' ', indent)}{identifier} = {keyword} \"{Escape(name)}\" \"Describe {Escape(name)}.\"";
        return kind is C4ElementKind.Container or C4ElementKind.Component
            ? declaration + " \"Technology\""
            : declaration;
    }

    /// <summary>The line the closing brace of a block sits on, given how to find the line that opens it.</summary>
    private static uint? EndOfBlock(LineDocument document, Func<C4Line, bool> opens)
    {
        var depth = 0;
        var inside = false;
        foreach (var line in document.CodeLines())
        {
            if (!inside)
            {
                if (!opens(line) || !line.Code.EndsWith('{'))
                {
                    continue;
                }

                inside = true;
                depth = 1;
                continue;
            }

            depth += line.Code.Count(character => character == '{') - line.Code.Count(character => character == '}');
            if (depth <= 0)
            {
                return line.Number;
            }
        }

        return null;
    }

    private static int IndentOf(string text) => text.Length - text.TrimStart().Length;

    private static string Escape(string value) => value.Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string Spell(C4ElementKind kind) => kind switch
    {
        C4ElementKind.SoftwareSystem => "software system",
        C4ElementKind.DeploymentNode => "deployment node",
        C4ElementKind.InfrastructureNode => "infrastructure node",
        C4ElementKind.ContainerInstance => "container instance",
        C4ElementKind.SoftwareSystemInstance => "software system instance",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
