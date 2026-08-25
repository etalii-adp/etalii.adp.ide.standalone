namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// What a step is, named as Azure Pipelines names it (Requirement 4.1).
/// </summary>
/// <remarks>
/// A step declares its kind by which key it leads with, so these are the keys themselves rather
/// than a categorisation of our own. <see cref="Unknown"/> exists because a file may lead with a
/// key this module has never heard of, and showing it as a step of an unfamiliar kind is honest
/// where dropping it would not be.
/// </remarks>
public enum PipelineStepKind
{
    /// <summary>A step whose leading key none of the others match.</summary>
    Unknown,

    /// <summary><c>task:</c> - a task from the marketplace or the built-in catalogue.</summary>
    Task,

    /// <summary><c>script:</c> - a command line, run by cmd on Windows and bash elsewhere.</summary>
    Script,

    /// <summary><c>bash:</c>.</summary>
    Bash,

    /// <summary><c>pwsh:</c> - PowerShell Core.</summary>
    Pwsh,

    /// <summary><c>powershell:</c> - Windows PowerShell.</summary>
    PowerShell,

    /// <summary><c>checkout:</c> - which repository, if any, to fetch.</summary>
    Checkout,

    /// <summary><c>download:</c> - a pipeline artifact coming in.</summary>
    Download,

    /// <summary><c>publish:</c> - a pipeline artifact going out.</summary>
    Publish,

    /// <summary><c>template:</c> - steps declared in another file.</summary>
    Template,
}
