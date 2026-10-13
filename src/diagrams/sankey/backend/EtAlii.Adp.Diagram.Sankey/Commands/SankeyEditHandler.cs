using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// What every Sankey command handler shares: the store, the parser, and the refusal of an entry
/// that is not there. The run itself - load, edit a copy, save, undo - is
/// <see cref="DocumentEditRun"/>'s.
/// </summary>
public abstract class SankeyEditHandler<TCommand>(ISankeyDocumentStore documents)
    : DocumentEditHandler<TCommand, ISankeyDocumentStore, LineDocument, SankeyModel>(documents)
    where TCommand : IDocumentEditCommand
{
    /// <summary>The refusal for an entry that is not there, in one wording.</summary>
    protected static EditOutcome Gone => EditOutcome.Refused("That is no longer in this diagram.");

    /// <inheritdoc />
    protected sealed override SankeyModel Parse(LineDocument body) => SankeyParser.Parse(body);

    /// <summary>The name, or the name with the lowest number that makes it unique among <paramref name="taken"/>.</summary>
    protected static string Unique(IEnumerable<string> taken, string name)
    {
        var names = taken.ToHashSet(StringComparer.Ordinal);
        if (!names.Contains(name))
        {
            return name;
        }

        for (var number = 2; ; number++)
        {
            var candidate = $"{name} {number}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
