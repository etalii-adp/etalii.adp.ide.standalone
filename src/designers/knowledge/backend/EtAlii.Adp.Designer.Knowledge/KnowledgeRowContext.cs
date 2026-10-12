using System.Text;
using EtAlii.Adp.Context;
using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// A row of a knowledge table as something that can be selected (knowledge-designer Requirement
/// 4.6): it is found under its table's file by its id, named by its title, and followed while the
/// table changes - so that the property grid shows the row that is selected, as it is now.
/// </summary>
internal sealed class KnowledgeRowSourceResolver(IKnowledgeDocumentStore documents) : IContextSourceResolver
{
    /// <summary>The origin a selected row is marked with, so that only this module's property provider answers for it.</summary>
    public static readonly DiagramOrigin RowOrigin = new("etalii", "knowledge");

    /// <summary>What the row's values are carried under in its detail: a change of them is a change of the detail, which is what the grid watches.</summary>
    private const string PayloadType = "etalii/knowledge+row";

    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.ElementId;

    public ValueTask<ContextLevelResolution> ResolveAsync(ShortGuid watchId, string rootPath, ContextSource id, IReadOnlyList<string> clientPath, ContextResolvedLevel? parent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        cancellationToken.ThrowIfCancellationRequested();

        if (parent is null || parent.Scope != ContextScope.Hierarchy || KnowledgeProject.BodyOf(parent.Target.ResolvedFullPath) is not { } bodyPath)
        {
            return Rejected("The selected file is not a knowledge table.");
        }

        var rowId = id.ElementId.Value;
        if (Describe(bodyPath, rowId) is not { } described)
        {
            return Rejected("That row is no longer in this table.");
        }

        var detail = new ContextLevelDetail
        {
            Element = new ElementDetail
            {
                Text = described.Title,
                ElementType = PayloadType,
                Payload = new Any { TypeUrl = PayloadType, Value = ByteString.CopyFromUtf8(described.Values) },
            },
        };
        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(new ContextResolvedLevel(
            id,
            [described.Title],
            ContextScope.DiagramElement,
            new ContextTarget(ContextScope.DiagramElement, bodyPath, IsContainer: false, SourceId: default, rootPath, watchId, rowId, RowOrigin),
            detail,
            this)));
    }

    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);

        var bodyPath = level.Target.ResolvedFullPath;
        var rowId = level.Target.ElementId;
        documents.Reloaded += OnReloaded;
        return new Unsubscriber(() => documents.Reloaded -= OnReloaded);

        void OnReloaded(string path)
        {
            if (string.Equals(path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                // Read again: an edit keeps the id while changing what is shown, and a deleted row clears the selection.
                onChange(Describe(bodyPath, rowId) is { } described ? [described.Title] : null);
            }
        }
    }

    /// <summary>The row's title and its values as one text, or null when the table or the row is not there.</summary>
    private static (string Title, string Values)? Describe(string bodyPath, string rowId)
    {
        if (KnowledgeDocumentStore.Read(bodyPath).Body is not { Unreadable.Length: 0 } body || body.Table.Rows.FirstOrDefault(candidate => candidate.Id == rowId) is not { } row)
        {
            return null;
        }

        var title = KnowledgeRelations.TitleOf(body.Table, row);
        var values = new StringBuilder();
        foreach (var cell in row.Cells)
        {
            values.Append(cell.PropertyId).Append('=').AppendJoin(',', cell.Values).Append(';');
        }

        return (title.Length > 0 ? title : rowId, values.ToString());
    }

    private static ValueTask<ContextLevelResolution> Rejected(string reason) => ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

/// <summary>
/// A selected row's values as the property grid's rows (knowledge-designer Requirement 4.6): one
/// per property of the table, each edited there as its type is, and each edit the same undoable
/// step it is in the table.
/// </summary>
internal sealed class KnowledgeRowPropertyProvider(IHistoryStackStore histories) : IContextPropertyProvider
{
    public const string RelationStays = "A relation is filled in in the table, where its rows can be chosen.";
    private const string ComputedStays = "This side of the relation is filled in from the other table.";

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (target.Origin != KnowledgeRowSourceResolver.RowOrigin
            || KnowledgeDocumentStore.Read(target.ResolvedFullPath).Body is not { Unreadable.Length: 0 } body
            || body.Table.Rows.FirstOrDefault(candidate => candidate.Id == target.ElementId) is not { } row)
        {
            return ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);
        }

        return ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([.. body.Table.Properties.Select(property => Row(body, property, row))]);
    }

    private static ContextPropertyDefinition Row(KnowledgeBody body, KnowledgeProperty property, KnowledgeRow row)
    {
        var values = row.Cells.FirstOrDefault(cell => cell.PropertyId == property.Id)?.Values ?? [];
        string NameOf(string optionId) => property.Options.FirstOrDefault(option => option.Id == optionId)?.Name ?? optionId;
        return property.ValueType switch
        {
            "checkbox" => new ContextPropertyDefinition(property.Id, property.Name, values.FirstOrDefault() == "true" ? "true" : "false", ContextPropertyEditor.Toggle, body.ReadOnlyReason),
            "selection" => new ContextPropertyDefinition(property.Id, property.Name, values.Count > 0 ? NameOf(values[0]) : "", ContextPropertyEditor.Choice, body.ReadOnlyReason, Candidates: ["", .. property.Options.Select(option => option.Name)]),
            "multipleSelection" => new ContextPropertyDefinition(property.Id, property.Name, string.Join(", ", values.Select(NameOf)), ContextPropertyEditor.Tags, body.ReadOnlyReason, Candidates: [.. property.Options.Select(option => option.Name)]),
            "relation" => new ContextPropertyDefinition(property.Id, property.Name, string.Join(", ", values), ContextPropertyEditor.Line, property.IsComputed ? ComputedStays : RelationStays),
            _ => new ContextPropertyDefinition(property.Id, property.Name, values.FirstOrDefault() ?? "", ContextPropertyEditor.Line, body.ReadOnlyReason),
        };
    }

    public async ValueTask<ContextPropertyResult> SetAsync(ContextTarget target, string propertyId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var bodyPath = target.ResolvedFullPath;
        if (target.Origin != KnowledgeRowSourceResolver.RowOrigin || KnowledgeDocumentStore.Read(bodyPath).Body is not { Unreadable.Length: 0 } body)
        {
            return ContextPropertyResult.Failure("That table is no longer there.");
        }

        if (body.ReadOnlyReason.Length > 0)
        {
            return ContextPropertyResult.Failure(body.ReadOnlyReason);
        }

        if (body.Table.Properties.FirstOrDefault(candidate => candidate.Id == propertyId) is not { } property)
        {
            return ContextPropertyResult.Failure("That property is no longer in this table.");
        }

        // The grid speaks of options by their names; the file holds their ids.
        List<string> values = [];
        if (property.ValueType is "selection" or "multipleSelection")
        {
            foreach (var name in (property.ValueType == "selection" ? [value] : value.Split(',')).Select(part => part.Trim()).Where(part => part.Length > 0))
            {
                if (property.Options.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } option)
                {
                    return ContextPropertyResult.Failure($"'{name}' is not one of the options of {property.Name}.");
                }

                values.Add(option.Id);
            }
        }
        else if (property.ValueType == "relation")
        {
            return ContextPropertyResult.Failure(property.IsComputed ? ComputedStays : RelationStays);
        }
        else
        {
            values.Add(value);
        }

        var edit = KnowledgeEdits.Plan(
            body.Table,
            "",
            new TableGesture("setCell", RowId: target.ElementId, ColumnId: propertyId, Values: values),
            () => ShortGuid.NewShortGuid().ToString(),
            KnowledgeDefinition.AddsBesideItsRule(System.IO.Path.GetExtension(bodyPath)));
        if (edit.IsRefused)
        {
            return ContextPropertyResult.Failure(edit.Refusal);
        }

        if (edit.Changes.Count == 0)
        {
            return ContextPropertyResult.Success;
        }

        var result = await histories.Get(target.RootPath).ExecuteAsync(new KnowledgeEditCommand(bodyPath, edit.Changes, edit.Others), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }
}
