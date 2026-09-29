namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>Which of the three levels an element id named.</summary>
public enum PipelineElementLocationKind
{
    /// <summary>A stage.</summary>
    Stage,

    /// <summary>A job, plain or deployment.</summary>
    Job,

    /// <summary>A step.</summary>
    Step,
}
