using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The job definition's <c>freeTaskKey</c> (databricks-disl-fbl finding A11): the first <c>&lt;type&gt;_&lt;n&gt;</c> no task
/// of the job uses, so a dropped task never collides with one left after a removal, as counting the tasks of a type and
/// adding one did.
/// </summary>
public class DatabricksFreeKeyTests
{
    [Fact]
    public void TheFirstFreeNumber_AfterARemoval_IsTheGap()
    {
        // Arrange: notebook_1 was removed, notebook_2 is left; counting the one notebook gives notebook_2 again.
        string[] keys = ["notebook_2", "python_1"];

        // Act.
        var notebook = DatabricksFreeKey.Of(keys, "notebook");
        var python = DatabricksFreeKey.Of(keys, "python");
        var condition = DatabricksFreeKey.Of(keys, "condition");

        // Assert.
        Assert.Equal("notebook_1", notebook);
        Assert.Equal("python_2", python);
        Assert.Equal("condition_1", condition);
    }

    [Fact]
    public void TheDefinitionsDrop_CallsTheFunction_OverTheJobsOwnKeys()
    {
        // Arrange: notebook_1 and notebook_3 left after notebook_2 was removed.
        var document = LineDocument.Parse("""
            resources:
              jobs:
                gapped:
                  tasks:
                    - task_key: notebook_1
                      notebook_task:
                        notebook_path: a
                    - task_key: notebook_3
                      notebook_task:
                        notebook_path: c
            """);
        var job = DatabricksModel.Read(document).Jobs.Single();

        // Act.
        var key = DatabricksDefinition.DroppedTaskKey(job, $"{DatabricksContextActionProvider.AddTaskActionPrefix}notebook");

        // Assert.
        Assert.Equal("notebook_2", key);
    }
}
