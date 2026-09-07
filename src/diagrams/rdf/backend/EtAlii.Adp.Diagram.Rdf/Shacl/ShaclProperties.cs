using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// The property grid of a shape card (shacl-diagram Requirement 6.3): identity, the shape's own
/// annotations, its target declarations, and every constraint row with its values.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is where the blank-node identity boundary pays for itself.</b> Requirement 4.1 asks
/// that blank-rooted content be fully <i>inspectable</i> even where it is not editable, and the
/// row projection makes that cheap: a property shape written as an anonymous node still has a
/// path, a summary and a cardinality, so it lists here as a plain row. What it does not have is
/// an identity that survives a reparse - so every such row is read-only, with
/// <see cref="ShaclRefusals.BlankRooted"/> as the stated reason rather than a bare disabled box.
/// </para>
/// <para>
/// <b>Almost everything here is read-only, on purpose.</b> Requirement 6.3 permits editing only
/// where Requirement 5 does, and Requirement 5 enumerates its writers: targets, rows, activation,
/// rename and removal are all gestures, discovered on the context menu where the confirmation
/// counts and the dialogs live. Only <c>sh:name</c> and <c>sh:description</c> land here, because
/// a single literal on an IRI-named subject has nowhere better to be edited. Each read-only row
/// says which path to take instead - Requirement 6.4's never-silently-absent rule applies to a
/// grid row exactly as it does to a menu entry.
/// </para>
/// <para>
/// Scoped by origin like <see cref="ShaclActions"/>, and for the same reason: two readings over
/// one <c>.ttl</c> produce byte-identical targets, so the element id cannot say whose grid this is.
/// </para>
/// </remarks>
public static class ShaclProperties
{
    private const string IdentityGroup = "Identity";
    private const string ShapeGroup = "Shape";
    private const string TargetsGroup = "Targets";
    private const string ConstraintsGroup = "Constraints";

    private const string RenameViaMenu = "Rename through the context menu, so every reference follows the name.";
    private const string TargetsAreGestures = "Targets are declared and withdrawn through the context menu.";
    private const string ActivationIsGesture = "Switch the shape off and on through the context menu.";
    private const string RowsAreGestures = "Property rows are added through the context menu, and edited as triples.";
    private const string EditedAsTriples = "This is edited as triples in the graph reading.";

    private const string NotExecuted =
        "Shown as written, never parsed beyond extraction and never executed - this reading draws constraints, it does not run them.";

    /// <summary>The shacl rows for the selection, or null when the element is not this reading's business.</summary>
    public static IReadOnlyList<ContextPropertyDefinition>? Describe(RdfDocumentEntry entry, ContextTarget target)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(target);

        if (!AnswersFor(target) || string.IsNullOrEmpty(target.ElementId))
        {
            return null;
        }

        var card = ShaclProjection.Project(entry.Model).Cards
            .FirstOrDefault(candidate => candidate.Id == target.ElementId);
        if (card is null)
        {
            return null;
        }

        // The two reasons that make a whole card read-only, decided once: an anonymous shape has
        // no durable identity to key an edit to, and a truncated view is not the whole file.
        var truncated = RdfSelection.IsTruncated(entry);
        var frozen = card.Blank ? ShaclRefusals.BlankRooted : truncated ? RdfSelection.TruncatedRefusal : "";

        var rows = new List<ContextPropertyDefinition>
        {
            new("shacl.iri", "IRI", card.Blank ? "(anonymous)" : card.Iri,
                ReadOnlyReason: card.Blank ? ShaclRefusals.BlankRooted : RenameViaMenu, Group: IdentityGroup),
            new("shacl.display", "Name", card.Display, ReadOnlyReason: RenameViaMenu, Group: IdentityGroup),
            new("shacl.name", "sh:name", card.Name, ReadOnlyReason: frozen, Group: ShapeGroup),
            new("shacl.description", "sh:description", card.Description, ReadOnlyReason: frozen, Group: ShapeGroup),
        };

        // Messages carry language tags and may be stated more than once, which a single-line box
        // would quietly flatten - so they are shown here and left to the text.
        var messages = Literals(entry, card, ShaclVocabulary.Message);
        if (messages.Length > 0)
        {
            rows.Add(new ContextPropertyDefinition(
                "shacl.message", "sh:message", string.Join(" / ", messages),
                ReadOnlyReason: EditedAsTriples, Group: ShapeGroup));
        }

        rows.Add(new ContextPropertyDefinition(
            "shacl.deactivated", "Deactivated", card.Deactivated ? "true" : "false",
            ReadOnlyReason: ActivationIsGesture, Group: ShapeGroup));

        if (card.Closed)
        {
            rows.Add(new ContextPropertyDefinition(
                "shacl.closed", "Closed", "true", ReadOnlyReason: EditedAsTriples, Group: ShapeGroup));
        }

        if (card.Severity.Length > 0)
        {
            rows.Add(new ContextPropertyDefinition(
                "shacl.severity", "Severity", card.Severity, ReadOnlyReason: EditedAsTriples, Group: ShapeGroup));
        }

        AddTargets(rows, card);
        AddConstraintRows(rows, card);
        AddSparqlQueries(rows, entry, card);

        return rows;
    }

    /// <summary>The command a grid edit dispatches, or null when the row is not this reading's to write.</summary>
    public static ICommand? CommandFor(RdfDocumentEntry entry, ContextTarget target, string propertyId, string value)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(target);

        if (!AnswersFor(target))
        {
            return null;
        }

        var predicateIri = propertyId switch
        {
            "shacl.name" => ShaclVocabulary.Name,
            "shacl.description" => ShaclVocabulary.Description,
            _ => null,
        };
        if (predicateIri is null)
        {
            return null;
        }

        // The gate, not the projection: the same decision the menu asks, so a blank-rooted or
        // truncated selection is refused here exactly as it is refused everywhere else.
        var decision = ShaclEditGate.For(entry.Model, target.ElementId, RdfSelection.IsTruncated(entry));
        return decision is { Applies: true, Available: true } && decision.ShapeIri.Length > 0
            ? new SetShaclLiteralCommand(target.ResolvedFullPath, decision.ShapeIri, predicateIri, value)
            : null;
    }

    /// <summary>
    /// One row per chip. "not described in this file" rides the value as a plain fact: Requirement
    /// 4.3 is explicit that a target aiming outside the file is the medium working, so it must
    /// never read as a defect here any more than it does on the canvas.
    /// </summary>
    private static void AddTargets(List<ContextPropertyDefinition> rows, ShaclCard card)
    {
        var ordinal = 0;
        foreach (var chip in card.Targets)
        {
            var absent = chip.DescribedInFile ? "" : " (not described in this file)";
            rows.Add(new ContextPropertyDefinition(
                $"shacl.target:{ordinal++}",
                KindLabel(chip.Kind),
                chip.TermDisplay + absent,
                ReadOnlyReason: chip.PredicateIri.Length == 0 ? ShaclRefusals.ImplicitTarget : TargetsAreGestures,
                Group: TargetsGroup));
        }
    }

    private static void AddConstraintRows(List<ContextPropertyDefinition> rows, ShaclCard card)
    {
        var ordinal = 0;
        foreach (var row in card.Rows)
        {
            var label = row.Path.Length > 0 ? row.Path : row.Sparql ? "SPARQL" : "constraint";
            var value = row.Cardinality.Length > 0 ? $"{row.Summary} {row.Cardinality}" : row.Summary;
            rows.Add(new ContextPropertyDefinition(
                $"shacl.row:{ordinal++}",
                row.Name.Length > 0 ? $"{label} - {row.Name}" : label,
                value.Trim(),
                ReadOnlyReason: row.Blank ? ShaclRefusals.BlankRooted : RowsAreGestures,
                Group: ConstraintsGroup));
        }
    }

    /// <summary>
    /// Requirement 1.7: the query text itself, readable, on a row that says it is not run. The
    /// text is reached through the blank node the constraint is written as, which reading may do
    /// freely - it is keying an edit to such a node, not looking at it, that the boundary forbids.
    /// </summary>
    private static void AddSparqlQueries(List<ContextPropertyDefinition> rows, RdfDocumentEntry entry, ShaclCard card)
    {
        if (card.Iri.Length == 0)
        {
            return;
        }

        var ordinal = 0;
        var constraints = entry.Model.Triples
            .Where(triple => triple.Subject is IriTerm subject && subject.Iri == card.Iri
                && triple.Predicate.Iri == ShaclVocabulary.Sparql)
            .Select(triple => triple.Object);

        foreach (var constraint in constraints)
        {
            var text = entry.Model.Triples
                .Where(triple => triple.Subject.Equals(constraint) && triple.Predicate.Iri == ShaclVocabulary.Select)
                .Select(triple => triple.Object is LiteralTerm literal ? literal.Lexical : "")
                .FirstOrDefault("");

            if (text.Length > 0)
            {
                rows.Add(new ContextPropertyDefinition(
                    $"shacl.sparql:{ordinal++}", "sh:select", text,
                    ContextPropertyEditor.Text, ReadOnlyReason: NotExecuted, Group: ConstraintsGroup));
            }
        }
    }

    /// <summary>Whether this reading owns the target: its origin, and nothing else's.</summary>
    private static bool AnswersFor(ContextTarget target) =>
        target.Origin is null || target.Origin == ServiceCollectionAddShaclExtension.ShaclOrigin;

    private static string[] Literals(RdfDocumentEntry entry, ShaclCard card, string predicateIri) =>
        card.Iri.Length == 0
            ? []
            : [.. entry.Model.Triples
                .Where(triple => triple.Subject is IriTerm subject && subject.Iri == card.Iri
                    && triple.Predicate.Iri == predicateIri
                    && triple.Object is LiteralTerm)
                .Select(triple => ((LiteralTerm)triple.Object).Lexical)];

    private static string KindLabel(ShaclTargetKind kind) => kind switch
    {
        ShaclTargetKind.Class => "Target class",
        ShaclTargetKind.Node => "Target node",
        ShaclTargetKind.SubjectsOf => "Target subjects of",
        ShaclTargetKind.ObjectsOf => "Target objects of",
        _ => "Target (implicit)",
    };
}
