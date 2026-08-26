namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// A pipeline: a component holding child components that differ only in evolution
/// (Requirement 5.4).
/// </summary>
/// <param name="Parent">The name of the component the pipeline hangs under.</param>
/// <param name="Children">The child components, in written order. May be empty - the parser tolerates an empty pipeline.</param>
/// <param name="Line">The 1-based line the `pipeline` statement is on.</param>
/// <param name="Form">Which of the two syntaxes the document used, so the writer gives it back the same way.</param>
/// <param name="LegacyExtent">
/// The two coordinates a <see cref="WardleyPipelineForm.Legacy"/> pipeline carries on its own
/// line, which the nested form does not have. Null for the nested form.
/// </param>
public sealed record WardleyPipeline(
    string Parent,
    IReadOnlyList<WardleyPipelineChild> Children,
    uint Line,
    WardleyPipelineForm Form,
    WardleyCoordinate? LegacyExtent = null);
