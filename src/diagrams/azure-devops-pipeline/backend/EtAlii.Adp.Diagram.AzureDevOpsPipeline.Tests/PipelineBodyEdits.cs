using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>An edit of a body in memory, as a command makes it, for the tests that need one before a save.</summary>
internal static class PipelineBodyEdits
{
    /// <summary>
    /// Renames the element <paramref name="elementId"/> of <paramref name="body"/> to <paramref name="displayName"/>, through the
    /// definition's form and the plugin's planner as the rename command does; the first stage, job or step this file writes
    /// when no id is given.
    /// </summary>
    public static void Rename(PipelineBody body, string? elementId, string displayName)
    {
        var element = elementId is null
            ? body.Disl.Diagram.Nodes.First(node => (node.IsA("Stage") || node.IsA("JobBase") || node.IsA("Step")) && node.ValueOf("editable") is true)
            : PipelineDefinition.ElementOf(body, elementId) ?? throw new InvalidOperationException($"The pipeline holds no element {elementId}.");
        var outcome = PipelineDefinition.Apply(body, FormCommit.Row(PipelineDefinition.Specification, element, "displayName", displayName, new DislEnv()));
        if (!outcome.WasApplied) throw new InvalidOperationException(outcome.Refusal);
    }
}
