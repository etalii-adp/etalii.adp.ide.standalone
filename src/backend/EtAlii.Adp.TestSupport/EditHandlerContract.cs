using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// What every diagram module's command handlers must do with a save, written once (code-structure
/// FR-022, D4): a warning of the save reaches the command's result, and an edit of an element that
/// is not there is refused and writes nothing. A module's handler tests derive from this and supply
/// its store, an example, a dispatcher and two commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the warning is the first fact.</b> <see cref="DocumentSaveResult.Warning"/> means the
/// document landed and something beside it did not, and it must not pass unmentioned. Six modules'
/// handlers built their result as <c>Failed ? Failure : Success</c>, which drops it; that was
/// harmless only while one store produced warnings. No example produces one, so the store here is
/// wrapped in <see cref="WarningDocumentStore{TBody, TModel}"/>, which adds one to every save.
/// </para>
/// </remarks>
public abstract class EditHandlerContract<TStore, TBody, TModel> : IDisposable
    where TStore : IDiagramDocumentStore<TBody, TModel>
{
    private const string SaveWarning = "The edit contract's warning: something beside the document was not kept.";

    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.EditHandlerContract");

    /// <summary>A module example with at least one element, copied to a scratch folder for each test.</summary>
    protected abstract string Example { get; }

    /// <summary>The copy of <see cref="Example"/> the test works on, made on first use.</summary>
    private string Body => field ??= CopyExample();

    /// <summary>A new store, with nothing loaded.</summary>
    protected abstract TStore CreateStore();

    /// <summary>
    /// <paramref name="store"/>, with every save it makes carrying <paramref name="warning"/>: the
    /// module's store interface over a <see cref="WarningDocumentStore{TBody, TModel}"/>.
    /// </summary>
    protected abstract TStore Warning(TStore store, string warning);

    /// <summary>Routes the module's commands to its handlers over <paramref name="store"/>, as the host does.</summary>
    protected abstract ICommandDispatcher Dispatcher(TStore store);

    /// <summary>A command that changes an element of the example at <paramref name="bodyPath"/>.</summary>
    protected abstract ICommand EditOf(string bodyPath);

    /// <summary>The same kind of command, naming an element that is not in the example.</summary>
    protected abstract ICommand EditOfMissing(string bodyPath);

    private string CopyExample()
    {
        var body = _folder.Combine(Path.GetFileName(Example));
        File.Copy(Example, body);
        return body;
    }

    public void Dispose()
    {
        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AWarningOfTheSave_ReachesTheCommandResult()
    {
        // Arrange.
        var before = await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken);
        var dispatcher = Dispatcher(Warning(CreateStore(), SaveWarning));

        // Act.
        var result = await dispatcher.DispatchAsync(EditOf(Body), TestContext.Current.CancellationToken);

        // Assert: the edit landed, can be undone, and says what did not.
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Inverse);
        Assert.NotEqual(before, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        Assert.Equal(SaveWarning, result.Warning);
    }

    [Fact]
    public async Task AnEditOfAMissingElement_IsRefused_AndWritesNothing()
    {
        // Arrange.
        var before = await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken);
        var dispatcher = Dispatcher(CreateStore());

        // Act.
        var result = await dispatcher.DispatchAsync(EditOfMissing(Body), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.NotEqual("", result.Error);
        Assert.Null(result.Inverse);
        Assert.Equal(before, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }
}

/// <summary>
/// A store that saves through another and adds a warning to every save that lands, for
/// <see cref="EditHandlerContract{TStore, TBody, TModel}"/>. A module derives from it once to give
/// it the module's store interface.
/// </summary>
public class WarningDocumentStore<TBody, TModel>(IDiagramDocumentStore<TBody, TModel> inner, string warning)
    : IDiagramDocumentStore<TBody, TModel>
{
    /// <inheritdoc />
    public event EventHandler<DocumentChangedEventArgs>? Changed
    {
        add => inner.Changed += value;
        remove => inner.Changed -= value;
    }

    /// <inheritdoc />
    public DocumentEntry<TBody, TModel> GetOrLoad(string path) => inner.GetOrLoad(path);

    /// <inheritdoc />
    public DocumentSaveResult Save(string path, TBody document)
    {
        var saved = inner.Save(path, document);
        return saved.Failed ? saved : DocumentSaveResult.WithWarning(warning);
    }

    /// <inheritdoc />
    public void Reload(string path) => inner.Reload(path);

    /// <inheritdoc />
    public void BodyDeleted(string path) => inner.BodyDeleted(path);

    /// <inheritdoc />
    public void Forget(string path) => inner.Forget(path);
}

/// <summary>
/// A <see cref="WarningDocumentStore{TBody, TModel}"/> for a store whose edits are saved against
/// the file as it is now: the warning is added to those saves too.
/// </summary>
public class WarningEditSavingDocumentStore<TBody, TModel> : WarningDocumentStore<TBody, TModel>, IEditSavingDocumentStore<TBody, TModel>
{
    private readonly IEditSavingDocumentStore<TBody, TModel> _inner;
    private readonly string _warning;

    protected WarningEditSavingDocumentStore(IEditSavingDocumentStore<TBody, TModel> inner, string warning)
        : base(inner, warning)
    {
        _inner = inner;
        _warning = warning;
    }

    /// <inheritdoc />
    public DocumentEditSaved Save(string path, DocumentEntry<TBody, TModel> basis, Func<TBody, string> edit)
    {
        var saved = _inner.Save(path, basis, edit);
        return saved.Result.Failed ? saved : saved with { Result = DocumentSaveResult.WithWarning(_warning) };
    }
}
