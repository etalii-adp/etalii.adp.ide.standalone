using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// The property rows of a selected node, contributed as data the grid renders without
/// understanding: its kind, its label, a Retry's attempts, and its notes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The kind is a choice among the kinds the node can become</b>: a node with children cannot
/// become a Do, a Check, an Ask the user or a Delegate, and one with several cannot become a Retry
/// or any other wrapper. Offering only those keeps a refusal from being the grid's way of saying so.
/// </para>
/// <para>
/// <b>The notes are where the detail goes</b> - the tool to use, the format to answer in, what to
/// watch out for - and they are written into the Markdown under the node, where the agent reads them.
/// </para>
/// </remarks>
public sealed class AbmContextPropertyProvider(IHistoryStackStore historyStacks, IAbmDocumentStore documents)
    : DiagramPropertyProvider<AbmBody, AbmModel>(historyStacks, documents)
{
    /// <summary>A node's kind.</summary>
    public const string KindProperty = "abm.kind";

    /// <summary>A node's label.</summary>
    public const string LabelProperty = "abm.label";

    /// <summary>A Retry's attempts.</summary>
    public const string AttemptsProperty = "abm.attempts";

    /// <summary>A node's notes.</summary>
    public const string NotesProperty = "abm.notes";

    /// <summary>A node's place in the tree, read-only.</summary>
    public const string PlaceProperty = "abm.place";

    /// <inheritdoc />
    protected override bool IsBody(string path) => path.EndsWith(Diagram.DocumentExtension, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    /// <remarks>By origin: a behavior model shares <c>.md</c> with every Markdown file.</remarks>
    protected override bool Serves(ContextTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.Origin == Diagram.AgentBehaviorModelling.Origin;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Derived from the DISL definition (<see cref="AbmDefinition.Rows"/>): the node form's items, the
    /// Kind a choice among the kinds the node can become.
    /// </remarks>
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(DocumentEntry<AbmBody, AbmModel> entry, string elementId) =>
        AbmDefinition.Rows(entry, elementId);

    /// <inheritdoc />
    protected override DiagramPropertyChange Change(ContextTarget target, AbmModel model, string propertyId, string value)
    {
        var body = target.ResolvedFullPath;
        if (model.NodeOf(target.ElementId) is not { } node)
        {
            return DiagramPropertyChange.Refused("That node is no longer in this behavior model.");
        }

        // Through the project's history and out through the delta stream - never written by the grid.
        switch (propertyId)
        {
            case KindProperty:
                var kind = AbmNodeKinds.All.FirstOrDefault(candidate => Choice(candidate.Id) == value || candidate.Id == value);
                return kind is null
                    ? DiagramPropertyChange.Refused($"'{value}' is not a kind of node.")
                    : DiagramPropertyChange.By(new SetAbmNodeKindCommand(body, node.Id, kind.Id));
            case LabelProperty:
                return DiagramPropertyChange.By(new RenameAbmNodeCommand(body, node.Id, value));
            case AttemptsProperty:
                return !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var attempts) || attempts < 1
                    ? DiagramPropertyChange.Refused($"'{value}' is not a number of attempts; a Retry allows at least 1.")
                    : DiagramPropertyChange.By(new SetAbmNodeKindCommand(body, node.Id, AbmNodeKinds.Retry, attempts));
            case NotesProperty:
                return DiagramPropertyChange.By(new SetAbmNotesCommand(body, node.Id, value));
            default:
                return DiagramPropertyChange.Refused($"'{propertyId}' cannot be edited on this selection.");
        }
    }

    /// <summary>A kind as the choice list reads it.</summary>
    private static string Choice(string kind) => kind == AbmNodeKinds.Retry ? "Retry" : AbmNodeKinds.Of(kind).Keyword;
}
