using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The property rows of a selected node or flow, derived from the DISL definition
/// (<see cref="SankeyDefinition.Rows"/>) and contributed as data the grid renders without understanding.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row edits through <see cref="SetSankeyPropertyCommand"/></b>, so a value typed in the
/// grid is checked by the same rules as one stepped on the canvas, and lands on the project's undo
/// stack like any other edit.
/// </para>
/// <para>
/// <b>A colour is offered twice</b>: as a palette word to pick, which reads well in both themes,
/// and as a custom <c>#rrggbb</c> to type, for a brand colour the palette does not have. Both write
/// the one <c>color</c> key.
/// </para>
/// <para>
/// <b>A node's value is shown and not edited</b>: it is what flows through it, so it changes by
/// changing a flow.
/// </para>
/// </remarks>
public sealed class SankeyContextPropertyProvider(IHistoryStackStore historyStacks, ISankeyDocumentStore documents)
    : DiagramPropertyProvider<SankeyBody, SankeyModel>(historyStacks, documents)
{
    /// <summary>The name of a node.</summary>
    public const string NameProperty = "sankey.name";

    /// <summary>The palette colour of a node or a flow.</summary>
    public const string ColorProperty = "sankey.color";

    /// <summary>A custom <c>#rrggbb</c> colour of a node or a flow.</summary>
    public const string CustomColorProperty = "sankey.custom-color";

    /// <summary>The line under a node's value.</summary>
    public const string NoteProperty = "sankey.note";

    /// <summary>How a node's value is written.</summary>
    public const string FormatProperty = "sankey.format";

    /// <summary>The column a node is drawn in.</summary>
    public const string ColumnProperty = "sankey.column";

    /// <summary>The description of a node or a flow.</summary>
    public const string DescriptionProperty = "sankey.description";

    /// <summary>A node's value, read-only, or a flow's value.</summary>
    public const string ValueProperty = "sankey.value";

    /// <summary>How much one + or − changes a flow's value.</summary>
    public const string StepProperty = "sankey.step";

    /// <summary>The colour choice that removes the entry's own colour.</summary>
    public const string DefaultColor = "(default)";

    /// <summary>The colour choice shown while a custom colour is in use.</summary>
    public const string CustomColor = "(custom)";

    /// <summary>The column choice that lets the flows decide.</summary>
    public const string AutoColumn = "auto";

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <inheritdoc />
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(DocumentEntry<SankeyBody, SankeyModel> entry, string elementId) =>
        // Derived from the DISL definition: a node's or a flow's form.
        SankeyDefinition.Rows(entry, elementId);

    /// <inheritdoc />
    protected override DiagramPropertyChange Change(ContextTarget target, SankeyModel model, string propertyId, string value)
    {
        _ = model;

        string? key = propertyId switch
        {
            NameProperty => SankeyKeys.Name,
            ColorProperty or CustomColorProperty => SankeyKeys.Color,
            NoteProperty => SankeyKeys.Note,
            FormatProperty => SankeyKeys.Format,
            ColumnProperty => SankeyKeys.Column,
            DescriptionProperty => SankeyKeys.Description,
            ValueProperty => SankeyKeys.Value,
            StepProperty => SankeyKeys.Step,
            _ => null,
        };

        if (key is null)
        {
            return DiagramPropertyChange.Refused($"'{propertyId}' cannot be edited on this selection.");
        }

        if (propertyId == ColorProperty && value == CustomColor)
        {
            // Picking the custom entry keeps the custom colour; a new one is typed in its own row.
            return DiagramPropertyChange.Unchanged;
        }

        value = propertyId switch
        {
            ColorProperty when value == DefaultColor => "",
            ColumnProperty when value == AutoColumn => "",
            _ => value,
        };

        return DiagramPropertyChange.By(new SetSankeyPropertyCommand(target.ResolvedFullPath, target.ElementId, key, value));
    }

}
