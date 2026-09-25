using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The palette per diagram type, each entry carrying only data and naming the add action its
/// drop commits (databricks-diagrams Requirement 9). There is no second implementation for a
/// drop to disagree with - the drop and the menu run the same command, via placement ids.
/// </summary>
public sealed class DatabricksToolboxProvider(DiagramOrigin origin) : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } = origin.Type switch
    {
        "job" =>
        [
            new(
                "databricks.toolbox.notebook-task",
                "Notebook task",
                "mdi-notebook-outline",
                "Runs a notebook. Drop on the canvas where it should sit.",
                $"{DatabricksContextActionProvider.AddTaskActionPrefix}notebook"),
            new(
                "databricks.toolbox.python-task",
                "Python task",
                "mdi-language-python",
                "Runs a Python file. Drop on the canvas where it should sit.",
                $"{DatabricksContextActionProvider.AddTaskActionPrefix}python"),
            new(
                "databricks.toolbox.condition-task",
                "Condition task",
                "mdi-call-split",
                "Branches on a condition; downstream edges follow its true or false outcome.",
                $"{DatabricksContextActionProvider.AddTaskActionPrefix}condition"),
        ],
        "pipeline" =>
        [
            new(
                "databricks.toolbox.notebook-library",
                "Notebook library",
                "mdi-notebook-outline",
                "A notebook the pipeline's transformations come from.",
                $"{DatabricksContextActionProvider.AddLibraryActionPrefix}notebook"),
            new(
                "databricks.toolbox.file-library",
                "File library",
                "mdi-file-code-outline",
                "A source file the pipeline's transformations come from.",
                $"{DatabricksContextActionProvider.AddLibraryActionPrefix}file"),
        ],
        _ =>
        [
            new(
                "databricks.toolbox.job-resource",
                "Job",
                "mdi-transit-connection-horizontal",
                "A job resource skeleton, complete with a starter task.",
                $"{DatabricksContextActionProvider.AddResourceActionPrefix}jobs"),
            new(
                "databricks.toolbox.pipeline-resource",
                "Pipeline",
                "mdi-pipe",
                "A pipeline resource skeleton, complete with a starter library.",
                $"{DatabricksContextActionProvider.AddResourceActionPrefix}pipelines"),
        ],
    };
}
