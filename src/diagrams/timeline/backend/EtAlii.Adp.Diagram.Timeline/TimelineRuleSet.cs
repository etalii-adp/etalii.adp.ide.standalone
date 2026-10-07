namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The mistakes a timeline document can hide, as a pure function over the model - no file, no
/// canvas, no connection (Requirement 12.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The rules are the definition's</b> (<c>constraints</c> in <c>definition/timeline.dis</c>),
/// evaluated by the DISL runtime over the model and reported grouped as this type always reported
/// them (<see cref="TimelineDefinition.Problems"/>): the ids, then the times, then the relations' ends.
/// </para>
/// <para>
/// Everything here is a <b>warning</b> naming its element, because Requirement 12.2 says the
/// rest of the diagram still draws. The one error this type has - a document that is not YAML -
/// never reaches these rules; the validator reports it as a single located problem instead of
/// running graph rules over a model that is empty only because the parse failed.
/// </para>
/// </remarks>
public static class TimelineRuleSet
{
    /// <summary>The problems in <paramref name="model"/>, or none.</summary>
    public static IReadOnlyList<DiagramProblem> Judge(TimelineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return TimelineDefinition.Problems(model);
    }
}
