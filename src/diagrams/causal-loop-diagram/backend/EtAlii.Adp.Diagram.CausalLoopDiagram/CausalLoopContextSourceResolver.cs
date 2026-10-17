using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// Makes a variable, a link or a loop selectable: resolves an <c>element_id</c> against the
/// document named by the enclosing level, and verifies the id really is in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This module shipped without one, and nothing noticed.</b> A canvas selection is resolved
/// level by level - the project, the folders, the <c>.adp</c>, and then the element inside it -
/// and the last of those is each diagram type's own job. With no resolver the element level never
/// resolved, so no <see cref="ContextTarget"/> was ever built, so
/// <see cref="CausalLoopContextActionProvider"/> and
/// <see cref="CausalLoopContextPropertyProvider"/> were never consulted at all.
/// </para>
/// <para>
/// Every symptom followed from that one absence: no context menu, nothing ever showing as
/// selected, and an unreachable property grid. Sixteen provider tests passed throughout, because
/// they construct a target by hand and ask the provider directly - which is precisely the step
/// that was missing in the running application.
/// </para>
/// <para>
/// <b>The shared resolver, told what this diagram's ids mean</b> (code-structure FR-031): the
/// checks, the gestures, the payload and the following are the base's.
/// </para>
/// <para>
/// Every diagram type answers for <c>element_id</c>, and none can tell from the id alone whether
/// the element is one of its own - only the file above it says that. So this refuses a file that
/// is not a causal loop diagram and the selection resolver moves on to the next resolver, which
/// is what lets every module's element resolver coexist.
/// </para>
/// </remarks>
public sealed class CausalLoopContextSourceResolver : DiagramElementSourceResolver<CausalLoopBody, CausalLoopModel>
{
    private readonly CausalLoopElementMapper _mapper;

    /// <summary>Creates the resolver over the router and the one document store.</summary>
    public CausalLoopContextSourceResolver(
        DiagramFileRouter router,
        ICausalLoopDocumentStore documents,
        CausalLoopElementMapper mapper)
        : base(router, documents)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _mapper = mapper;
    }

    /// <inheritdoc />
    protected override ToolOrigin Origin => Diagram.CausalLoop.Origin;

    /// <summary>
    /// A placement makes the diagram itself the subject, which is what makes the diagram-wide actions
    /// reachable with nothing selected; a relation gesture is the link the user drew.
    /// </summary>
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a causal loop diagram.",
        "That element is no longer in this diagram.",
        "This diagram",
        "New link");

    /// <summary>
    /// What an id reads as: a variable's label, a link named by its two ends, or a loop's caption.
    /// Null where the document no longer holds it.
    /// </summary>
    protected override (IReadOnlyList<string> Path, string Text)? Describe(CausalLoopModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        return TextOf(model, elementId) is { } text ? ([text], text) : null;
    }

    /// <summary>
    /// The same payload the canvas already has, taken from the mapper rather than assembled here, so
    /// the selection and the stream can never describe one element two ways.
    /// </summary>
    protected override DiagramElement? Drawn(CausalLoopModel model, string elementId) =>
        _mapper.Elements(model, CausalLoopLayout.Compute(model)).FirstOrDefault(candidate => candidate.Id == elementId);

    private static string? TextOf(CausalLoopModel model, string elementId)
    {
        if (CausalLoopSelection.VariableOf(elementId) is { } variableId)
        {
            return model.Variables.FirstOrDefault(variable => variable.Id == variableId)?.Display;
        }

        if (CausalLoopSelection.LinkOf(elementId) is { } ends)
        {
            var link = model.Links.FirstOrDefault(candidate => candidate.From == ends.From && candidate.To == ends.To);
            return link is null ? null : $"{ends.From} → {ends.To}";
        }

        if (CausalLoopSelection.LoopOf(elementId) is { } identifier)
        {
            var loop = model.Loops.FirstOrDefault(candidate => candidate.Identifier == identifier);
            return loop is null ? null : loop.Name.Length > 0 ? $"{loop.Identifier} · {loop.Name}" : loop.Identifier;
        }

        return null;
    }
}
