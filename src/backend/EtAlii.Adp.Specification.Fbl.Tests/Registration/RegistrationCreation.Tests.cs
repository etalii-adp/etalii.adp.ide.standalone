using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Registration;

/// <summary>A registration created beside a body on the first placement or setting (FBL §8.4, 0.7 §8.4).</summary>
public class RegistrationCreationTests
{
    /// <summary>The binding of the <c>registration-blocks</c> fixture, whose <c>createOnFirstPlacement</c> is true.</summary>
    private static readonly FblBinding _tasks = Binding();

    private static readonly FblBinding _timeline = RealFileCorpus.Binding("timeline.fbl", "timeline");

    private static FblBinding Binding()
    {
        var fixture = Path.Combine(Repository.Conformance, "etalii.adp", "specifications", "fbl", "fixtures", "registration-blocks", "fixture.json");
        var binding = FblDocumentLoader.ResolveReference("tasks.fbl#tasks", fixture, out var problems);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        return binding;
    }

    [Fact]
    public void ARegistrationWhoseBaseNameDiffers_NamesItsBody_InTheBodysLineEnding()
    {
        // Act.
        var result = RegistrationCreation.Create(_tasks, "example/tasks", "plan.adp", "week.tasks", "task t1\r\ntask t2\r\n"u8.ToArray(), null);

        // Assert.
        Assert.Null(result.Refusal);
        Assert.Equal("example/tasks\r\nbody: week.tasks\r\n"u8.ToArray(), result.Bytes);
    }

    [Fact]
    public void ARegistrationWithTheBodysBaseName_IsTheOriginLineAlone()
    {
        // Act.
        var result = RegistrationCreation.Create(_tasks, "example/tasks", "week.adp", "week.tasks", "task t1\ntask t2\r\ntask t3\n"u8.ToArray(), null);

        // Assert: LF, the body's dominant line ending.
        Assert.Null(result.Refusal);
        Assert.Equal("example/tasks\n"u8.ToArray(), result.Bytes);
    }

    [Fact]
    public void TheCreatedRegistrationIsNoStepOfTheHistory()
    {
        // Arrange: created before the edit is planned.
        var created = RegistrationCreation.Create(_tasks, "example/tasks", "plan.adp", "week.tasks", "task t1\r\n"u8.ToArray(), null).Bytes;
        Assert.NotNull(created);
        var registration = OpenRegistration.Open(created, _tasks);
        Assert.IsType<PlanResult.Planned>(registration.Change(new ModelChange.Setting("tags", "security", "red")));

        // Act.
        var undone = registration.Undo();

        // Assert: the undo takes the setting back and leaves the file as it was created.
        Assert.IsType<UndoResult.Done>(undone);
        Assert.Equal(created, registration.Bytes);
        Assert.False(registration.CanUndo);
    }

    [Fact]
    public void ADifferentFileOfThatName_IsRefusedWithAReason()
    {
        // Act.
        var result = RegistrationCreation.Create(_tasks, "example/tasks", "plan.adp", "week.tasks", "task t1\r\n"u8.ToArray(), "my own notes\r\n"u8.ToArray());

        // Assert.
        Assert.Null(result.Bytes);
        Assert.False(string.IsNullOrWhiteSpace(result.Refusal));
        Assert.Contains("plan.adp", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ABindingThatDoesNotCreateOnFirstPlacement_IsRefusedWithAReason()
    {
        // Act.
        var result = RegistrationCreation.Create(_timeline, "generic/timeline", "plan.adp", "plan.tml", "elements: []\n"u8.ToArray(), null);

        // Assert.
        Assert.Null(result.Bytes);
        Assert.False(string.IsNullOrWhiteSpace(result.Refusal));
    }
}
