namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Where a <c>template</c> reference appeared, which decides what it contributes.
/// </summary>
public enum PipelineTemplateSlot
{
    /// <summary>The pipeline's whole shape comes from the template (Requirement 5.5).</summary>
    Extends,

    /// <summary>A template contributing stages, referenced from a <c>stages</c> list.</summary>
    Stages,

    /// <summary>A template contributing jobs, referenced from a <c>jobs</c> list.</summary>
    Jobs,

    /// <summary>A template contributing steps, referenced from a <c>steps</c> list.</summary>
    Steps,

    /// <summary>A template contributing variables.</summary>
    Variables,
}
