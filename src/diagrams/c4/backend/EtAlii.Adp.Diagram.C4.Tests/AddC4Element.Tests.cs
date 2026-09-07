using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// Adding an element to a C4 model - what the toolbox does when something is dropped on the
/// canvas, and what the context menu does on "Add container…".
/// </summary>
/// <remarks>
/// The interesting half is not the append. It is that C4 says what may contain what, and that
/// the thing a new element goes inside may not have a block yet: <c>web = container "Web" …</c>
/// is one line, and putting a component in it means giving it braces it never had.
/// </remarks>
public class AddC4ElementTests : IDisposable
{
    private const string Model = """
        workspace "Bank" {
            model {
                u = person "Customer" "A customer."
                s = softwareSystem "Banking" "Does banking." {
                    web = container "Web" "Serves pages." "React"
                }
                u -> web "Uses" "HTTPS"
            }
            views {
                container s "containers" {
                    include *
                }
            }
        }
        """;

    private readonly string _root;
    private readonly string _bodyPath;
    private readonly ServiceProvider _services;
    private readonly IHistoryStack _history;
    private readonly IC4DocumentStore _documents;

    public AddC4ElementTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(_bodyPath, Model);

        _services = new ServiceCollection()
            .AddCommands().AddHierarchyCommandHandlers()
            .AddC4()
            .BuildServiceProvider();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(_root);
        _documents = _services.GetRequiredService<IC4DocumentStore>();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _services.Dispose();
        TestFolder.TryDelete(_root);
    }

    private async Task<CommandResult> AddAsync(C4ElementKind kind, string name, string parentId = "") =>
        await _history.ExecuteAsync(
            new AddC4ElementCommand(_bodyPath, kind, name, parentId), TestContext.Current.CancellationToken);

    private C4Workspace Workspace() => _documents.WorkspaceOf(_bodyPath);

    private string OnDisk() => File.ReadAllText(_bodyPath);

    [Fact]
    public async Task ASoftwareSystem_LandsAtTheTopOfTheModel()
    {
        // Act.
        var result = await AddAsync(C4ElementKind.SoftwareSystem, "Payments");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var added = Assert.Single(Workspace().Elements, element => element.Name == "Payments");
        Assert.Equal(C4ElementKind.SoftwareSystem, added.Kind);
        Assert.Null(added.ParentId);
    }

    [Fact]
    public async Task AContainer_LandsInsideTheSystemItWasDroppedOn()
    {
        // Act.
        var result = await AddAsync(C4ElementKind.Container, "Api", parentId: "s");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var added = Assert.Single(Workspace().Elements, element => element.Name == "Api");
        Assert.Equal("s", added.ParentId);
        // C4 requires a technology on a container, so one is written for the user to replace
        // rather than left out for the validator to complain about.
        Assert.NotEmpty(added.Technology);
    }

    [Fact]
    public async Task AComponent_GivesItsContainerABlockWhenItHasNone()
    {
        // Arrange.
        // `web` is declared on one line with no braces. This is the surgery worth testing.
        Assert.DoesNotContain("\"React\" {", OnDisk(), StringComparison.Ordinal);

        // Act.
        var result = await AddAsync(C4ElementKind.Component, "Controller", parentId: "web");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var added = Assert.Single(Workspace().Elements, element => element.Name == "Controller");
        Assert.Equal("web", added.ParentId);
        Assert.Contains("\"React\" {", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThatBlockSurgery_IsOneUndoAway()
    {
        // Arrange.
        var before = OnDisk();
        await AddAsync(C4ElementKind.Component, "Controller", parentId: "web");

        // Act.
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        // The brace the add opened has to close again, or undo leaves a document that will not
        // parse - which is a far worse outcome than the add having failed.
        Assert.Equal(before, OnDisk());
    }

    [Fact]
    public async Task AnAdd_ChangesOnlyTheLinesItNeeds()
    {
        // Arrange.
        var before = OnDisk().Split('\n');

        // Act.
        await AddAsync(C4ElementKind.SoftwareSystem, "Payments");

        // Assert.
        // One line added, none of the others touched: the `.dsl` is another ecosystem's file
        // and an add is line surgery on it like every other edit (Requirement 3.2).
        var after = OnDisk().Split('\n');
        Assert.Equal(before.Length + 1, after.Length);
        Assert.Empty(before.Except(after));
    }

    [Fact]
    public async Task AContainerDroppedOnNothing_IsRefusedWithSomewhereToGo()
    {
        // Act.
        var result = await AddAsync(C4ElementKind.Container, "Api");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("software system", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AComponentDroppedOnASystem_IsRefusedAndSaysWhatItWanted()
    {
        // Act.
        // C4's containment rule, enforced at the point of the gesture rather than left to the
        // validator to report afterwards.
        var result = await AddAsync(C4ElementKind.Component, "Controller", parentId: "s");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("container", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Controller", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnnamedElement_IsRefused()
    {
        // Act.
        var result = await AddAsync(C4ElementKind.SoftwareSystem, "   ");

        // Assert.
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ASecondElementOfTheSameName_GetsItsOwnIdentifier()
    {
        // Arrange.
        await AddAsync(C4ElementKind.SoftwareSystem, "Payments");

        // Act.
        var result = await AddAsync(C4ElementKind.SoftwareSystem, "Payments");

        // Assert.
        // Numbered rather than randomised: an identifier is what a human reads in the diff.
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(Workspace().Find("payments"));
        Assert.NotNull(Workspace().Find("payments2"));
    }

    [Fact]
    public async Task AnAddedElement_IsOneUndoAway_AndRedoBringsItBack()
    {
        // Arrange.
        var before = OnDisk();
        await AddAsync(C4ElementKind.SoftwareSystem, "Payments");

        // Act and assert, step by step.
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, OnDisk());

        await _history.RedoAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(Workspace().Find("payments"));
    }

    [Fact]
    public async Task TheDocumentStillParses_AfterAnAddAndAnUndo()
    {
        // Arrange.
        await AddAsync(C4ElementKind.Component, "Controller", parentId: "web");
        await AddAsync(C4ElementKind.Container, "Api", parentId: "s");
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Act.
        var workspace = C4Parser.Parse(C4Document.Parse(OnDisk()));

        // Assert.
        // The point: braces balance whatever order the edits happened in.
        Assert.NotNull(workspace.Find("controller"));
        Assert.Null(workspace.Find("api"));
        Assert.DoesNotContain(C4RuleSet.Validate(workspace), problem => problem.Severity == DiagramProblemSeverity.Error);
    }

    [Fact]
    public async Task UndoingAnAdd_DoesNotTakeACommentTheUserWroteInsideTheBlock()
    {
        // Arrange.
        // The add gave `web` its first braces. If the user then writes a comment in that block,
        // undoing the add must remove the element and leave the comment - collapsing the block
        // would delete something ADP never wrote, which is precisely what Requirement 3.3
        // forbids and what the whole line-surgery approach exists to avoid.
        await AddAsync(C4ElementKind.Component, "Controller", parentId: "web");

        var document = _documents.GetOrLoad(_bodyPath);
        var opened = document.CodeLines.Single(line => line.Code.Contains("container \"Web\"", StringComparison.Ordinal));
        document.InsertLine(opened.Number + 1, "                // Components arrive here as the design settles.");
        _documents.Save(_bodyPath);

        // Act.
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("// Components arrive here as the design settles.", OnDisk(), StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", OnDisk(), StringComparison.Ordinal);
    }

}
