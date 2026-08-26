namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// A component inside a pipeline. It carries an <b>evolution position only</b> and takes its
/// visibility from the parent (Requirement 5.4).
/// </summary>
/// <param name="Name">The child's name, as written.</param>
/// <param name="Maturity">Where it sits on the evolution axis. The one number a child has.</param>
/// <param name="Line">The 1-based line that declares it.</param>
/// <remarks>
/// Deliberately not a <see cref="WardleyComponent"/> with a synthesised visibility. A child has
/// no visibility of its own in the document, and inventing one here would make it possible to
/// write a visibility back that the format has nowhere to put - which is also why dragging a
/// child changes maturity alone (Requirement 7.4).
/// </remarks>
public sealed record WardleyPipelineChild(string Name, double Maturity, uint Line);
