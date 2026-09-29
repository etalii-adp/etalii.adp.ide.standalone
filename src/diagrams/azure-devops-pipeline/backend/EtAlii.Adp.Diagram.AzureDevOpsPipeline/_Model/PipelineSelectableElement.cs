namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// What the context mechanism needs to know about a selected element, whichever kind it is.
/// </summary>
/// <remarks>
/// A stage, a job and a step answer the same four questions, so they are answered once here rather
/// than three times at the call site - where the third copy would be the one that drifted.
/// </remarks>
/// <param name="Path">Its chain of names within the pipeline, which is what identifies it to a person.</param>
/// <param name="Text">What to show for it.</param>
/// <param name="HasChildren">Whether anything nests inside it.</param>
/// <param name="IsFromTemplate">Whether its text lives in another file.</param>
public sealed record PipelineSelectableElement(
    IReadOnlyList<string> Path,
    string Text,
    bool HasChildren,
    bool IsFromTemplate);
