using System.Globalization;
using System.Text;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Where a new element goes in a Structurizr DSL document, and how it gets there.
/// </summary>
/// <remarks>
/// C4 says what may contain what, and this is the one place that turns those rules into line
/// numbers. Two things make it more than an append: an element may have to go *inside* another,
/// and the element it goes inside may not have a block yet - <c>web = container "Web" "x" "y"</c>
/// is one line, and putting a component in it means giving it braces it never had.
/// </remarks>
internal static class C4Placement
{
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
    public static bool TryInsert(
        C4Document document,
        C4Workspace workspace,
        C4ElementKind kind,
        string name,
        string identifier,
        string parentId,
        out string error,
        out bool openedBlock)
    {
        error = "";
        openedBlock = false;
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
        var parentLine = document.CodeLines
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
        openedBlock = true;
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
    public static void CollapseEmptyBlock(C4Document document, C4Element parent)
    {
        var open = document.CodeLines
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
        var hasContent = document.CodeLines
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
    public static bool TryRemove(C4Document document, C4Element element, out string error)
    {
        error = "";
        var line = document.CodeLines
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
    private static uint? EndOfBlock(C4Document document, Func<C4Line, bool> opens)
    {
        var depth = 0;
        var inside = false;
        foreach (var line in document.CodeLines)
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
