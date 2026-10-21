using EtAlii.Adp.Designer.TableModel;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>One gesture of the table: what it is called, whether it is about the view shown, and what it comes to.</summary>
/// <param name="Kind">The kind a gesture arrives under, as the client's table library raises it.</param>
/// <param name="RequiresView">
/// Whether the gesture is about the view shown - its columns, sorts, groups or filter, or rows in its
/// order - and so is refused in a table that has no view. A gesture that names its view itself, or
/// needs none, says false.
/// </param>
/// <param name="Plan">What the gesture comes to; handed a view whenever <paramref name="RequiresView"/> says it needs one.</param>
internal sealed record KnowledgeGesture(string Kind, bool RequiresView, Func<KnowledgeGestureRequest, KnowledgeEdit> Plan);

/// <summary>Everything the plan of a gesture may read.</summary>
/// <param name="Table">The table as it is shown now, edits not yet written included.</param>
/// <param name="View">The view the gesture is about: the one it names, else the one shown; null in a table without views.</param>
/// <param name="Gesture">What the author did.</param>
/// <param name="NewId">Gives an id that was never used.</param>
/// <param name="BesideItsRule">Whether the file's format adds an entry after the last one its rule reads.</param>
/// <param name="Files">The files around this one, or null where there are none to know of.</param>
internal sealed record KnowledgeGestureRequest(KnowledgeTable Table, KnowledgeView? View, TableGesture Gesture, Func<string> NewId, bool BesideItsRule, KnowledgeSurroundings? Files)
{
    /// <summary>The view of a gesture that requires one: there, because the table refuses such a gesture before its plan is asked.</summary>
    public KnowledgeView RequiredView => View ?? throw new InvalidOperationException($"'{Gesture.Kind}' needs a view and was planned without one.");
}

/// <summary>
/// The gestures by their kind. A gesture is found by its whole kind, so the order the gestures are
/// listed in decides nothing: not which of <c>setCell</c> and <c>setCells</c> a gesture is, and not
/// whether a gesture is refused for want of a view, which each gesture says for itself.
/// </summary>
internal sealed class KnowledgeGestureTable
{
    private readonly Dictionary<string, KnowledgeGesture> _byKind;

    /// <summary>The gestures; two of one kind are refused, since one of them would have to win.</summary>
    public KnowledgeGestureTable(IEnumerable<KnowledgeGesture> gestures)
    {
        _byKind = new Dictionary<string, KnowledgeGesture>(StringComparer.Ordinal);
        foreach (var gesture in gestures)
        {
            if (!_byKind.TryAdd(gesture.Kind, gesture))
            {
                throw new ArgumentException($"The gesture '{gesture.Kind}' is listed twice.", nameof(gestures));
            }
        }
    }

    /// <summary>The gesture of a kind, or <paramref name="unknown"/> for a kind the table does not have.</summary>
    public KnowledgeGesture Of(string kind, KnowledgeGesture unknown) => _byKind.GetValueOrDefault(kind) ?? unknown;
}
