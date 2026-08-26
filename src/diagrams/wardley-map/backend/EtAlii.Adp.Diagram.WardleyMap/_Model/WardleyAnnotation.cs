namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// A numbered annotation, pinned at one place or several (Requirement 6.8).
/// </summary>
/// <param name="Number">The number shown on the map and in the annotations block.</param>
/// <param name="Occurrences">
/// Every place it is pinned, in written order. <b>Never fewer than one, and often more.</b>
/// </param>
/// <param name="Text">What the annotation says.</param>
/// <param name="Line">The 1-based line that declares it.</param>
/// <remarks>
/// <para>
/// This is the record Requirement 6.8 warned about: the DSL permits one numbered annotation
/// pinned at several places - `annotation 1 [[0.43,0.49],[0.08,0.79]] text` - which a
/// one-element-one-position model cannot express. The requirement left the representation open
/// and insisted only that no position may be lost, so the list is the answer: one annotation,
/// many occurrences, and the single-position form is simply a list of one.
/// </para>
/// <para>
/// The reference parser calls this field `occurances`. The spelling is not carried over, but it
/// is recorded here so the two are recognisably the same thing when someone reads its output.
/// </para>
/// </remarks>
public sealed record WardleyAnnotation(
    int Number,
    IReadOnlyList<WardleyCoordinate> Occurrences,
    string Text,
    uint Line);
