using System.Reflection;
using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// One open knowledge file on one connection (knowledge-designer Requirements 10.5, 2.3 and
/// 8.1): the table model it gives, that opening writes nothing, and that it stays true to the
/// file - meeting a text editor session's obligations towards its file, under the same tests.
/// </summary>
/// <remarks>
/// <b>How each watcher case is caused</b> is the editor family's way. A deletion is a real delete
/// against the real watcher. A window of lost events cannot be caused on demand, so the watcher is
/// switched off while the file changes - no event can carry that change - and then its own
/// <c>Error</c> is raised through <c>FileSystemWatcher.OnError</c>, which raises synchronously.
/// </remarks>
public sealed class KnowledgeSessionTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _root = Directory.CreateTempSubdirectory("adp-knowledge-session-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A watcher may still be letting go; the temp folder is the system's to clear.
        }
    }

    // ---- the table model ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task TheExample_OpensAsTheExpectedTableModel(string extension)
    {
        // Arrange.
        var path = KnowledgeFiles.CopyExample("cities" + extension, _root);
        await using var session = new KnowledgeSession(path);

        // Act.
        var baseline = session.Baseline();

        // Assert: the table, editable, with nothing to report.
        Assert.Equal("Cities", baseline.Title);
        Assert.Equal("", baseline.ReadOnlyReason);
        Assert.Empty(baseline.Findings);

        // Every property is a column, of its type's kind, the one that names a row marked and shown.
        Assert.Equal(
            ["Name:text", "Country:selection", "Population:number", "Tags:multipleSelection", "Province:relation", "Visited:checkbox"],
            baseline.Columns.Where(column => new[] { "p1", "p2", "p3", "p4", "p5", "p6" }.Contains(column.Id)).OrderBy(column => column.Id, StringComparer.Ordinal).Select(column => $"{column.Name}:{column.Kind}"));
        var title = Assert.Single(baseline.Columns, column => column.IsTitle);
        Assert.Equal("p1", title.Id);
        Assert.True(title.Visible);

        // A selection carries its options and their colours; a relation says where it points.
        var country = baseline.Columns.Single(column => column.Id == "p2");
        Assert.Equal(["Netherlands:blue", "Belgium:yellow"], country.Options.Select(option => $"{option.Name}:{option.Color}"));
        var province = baseline.Columns.Single(column => column.Id == "p5");
        Assert.Equal(("provinces.yaml", "one"), (province.Settings["targetFile"], province.Settings["limit"]));

        // The views, and the settings of the one the file was left in.
        Assert.NotEmpty(baseline.Views);
        Assert.Equal("v1", baseline.Settings.ViewId);

        // The lines of that view, which shows the cities of the Netherlands grouped by country: a
        // heading for each country and for the cities without one, the one city the filter lets
        // through, and under every heading the line a new row is added at.
        Assert.Equal("p2", baseline.Settings.GroupBy);
        Assert.Equal(3 + 1 + 3, baseline.RowCount);
    }

    [Fact]
    public async Task TheWindow_IsTheLinesAskedFor_WithTheNewRowLineAtTheBottom()
    {
        // Arrange.
        var path = KnowledgeFiles.CopyPlain("cities.yaml", _root);
        await using var session = new KnowledgeSession(path);
        var pushed = Collect(session);
        var total = session.Baseline().RowCount;

        // Act: everything, then two lines from the second.
        session.SetWindow(0, 1000);
        session.SetWindow(1, 2);

        // Assert.
        var all = Assert.IsType<TableRowsChanged>(pushed[0]);
        Assert.Equal((0, total, total), (all.First, all.Rows.Count, all.RowCount));
        Assert.All(all.Rows.Take(total - 1), row => Assert.False(row.IsNewRow));
        Assert.True(all.Rows[^1].IsNewRow);
        Assert.Equal("", all.Rows[^1].Id);

        // A row's cells are the ones that hold a value, by column; several values stay several.
        var first = all.Rows[0];
        Assert.Equal("Amsterdam", first.Cells.Single(cell => cell.ColumnId == "p1").Values.Single());
        Assert.Contains(all.Rows, row => row.Cells.Any(cell => cell is { ColumnId: "p4", Values.Count: > 1 }));

        var part = Assert.IsType<TableRowsChanged>(pushed[1]);
        Assert.Equal((1, 2, total), (part.First, part.Rows.Count, part.RowCount));
        Assert.Equal(all.Rows.Skip(1).Take(2).Select(row => row.Id), part.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task AWindowPastTheEnd_IsEmpty_AndNeverThrows()
    {
        // Arrange.
        var path = KnowledgeFiles.CopyExample("cities.yaml", _root);
        await using var session = new KnowledgeSession(path);
        var pushed = Collect(session);

        // Act.
        session.SetWindow(5000, 50);
        session.SetWindow(-3, -1);

        // Assert.
        Assert.Empty(Assert.IsType<TableRowsChanged>(pushed[0]).Rows);
        Assert.Empty(Assert.IsType<TableRowsChanged>(pushed[1]).Rows);
    }

    [Fact]
    public async Task AnotherView_IsThisConnectionsOwn()
    {
        // Arrange: two connections on one file.
        var path = KnowledgeFiles.CopyExample("cities.yaml", _root);
        await using var one = new KnowledgeSession(path);
        await using var other = new KnowledgeSession(path);
        var views = one.Baseline().Views;
        Assert.True(views.Count > 1, "The example needs a second view for this test.");
        var pushed = Collect(one);

        // Act.
        one.SetActiveView(views[1].Id);
        one.SetActiveView("no-such-view");

        // Assert: the structure for that view, once; the other connection is where it was.
        var structure = Assert.IsType<TableStructureChanged>(Assert.Single(pushed));
        Assert.Equal(views[1].Id, structure.Settings.ViewId);
        Assert.Equal(views[1].Id, one.Baseline().Settings.ViewId);
        Assert.Equal("v1", other.Baseline().Settings.ViewId);
    }

    // ---- opening writes nothing ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task OpeningAndLooking_WritesNothing(string extension)
    {
        // Arrange.
        var path = KnowledgeFiles.CopyExample("cities" + extension, _root);
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var stamp = File.GetLastWriteTimeUtc(path);

        // Act: everything a connection does without editing.
        await using (var session = new KnowledgeSession(path))
        {
            session.Baseline();
            session.SetWindow(0, 100);
            session.SetActiveView(session.Baseline().Views[^1].Id);
            session.OnExternalChange();
        }

        // Assert: the same bytes, not rewritten, and nothing beside the file.
        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(["cities" + extension], Directory.GetFileSystemEntries(_root).Select(entry => IoPath.GetFileName(entry)));
    }

    // ---- staying true to the file -------------------------------------------------------------

    [Fact]
    public async Task AChangeMadeOutsideAdp_IsShownWithoutReopening()
    {
        // Arrange.
        var path = KnowledgeFiles.CopyPlain("cities.yaml", _root);
        await using var session = new KnowledgeSession(path);
        session.SetWindow(0, 100);
        var pushed = Collect(session);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act: another program renames the table and the first city.
        await File.WriteAllTextAsync(path, text.Replace("name: Cities", "name: Towns", StringComparison.Ordinal).Replace("text: Amsterdam", "text: Mokum", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        // Assert: the structure, the findings and the window's rows, from the file as it is now.
        Assert.True(await Eventually(() => pushed.OfType<TableRowsChanged>().Any()), "The session never showed the change made to its file.");
        Assert.Equal("Towns", pushed.OfType<TableStructureChanged>().Last().Title);
        Assert.Equal("Mokum", pushed.OfType<TableRowsChanged>().Last().Rows[0].Cells.Single(cell => cell.ColumnId == "p1").Values.Single());
        Assert.Contains(pushed, change => change is TableFindingsChanged);
    }

    [Fact]
    public async Task AWriteThatChangesNothing_PushesNothing()
    {
        // Arrange.
        var path = KnowledgeFiles.CopyExample("cities.yaml", _root);
        await using var session = new KnowledgeSession(path);
        session.SetWindow(0, 100);
        var pushed = Collect(session);

        // Act: read again, as the watcher would ask after a save of the same bytes.
        session.OnExternalChange();

        // Assert.
        Assert.Empty(pushed);
    }

    [Fact]
    public async Task ADeletion_IsLearned()
    {
        // Arrange.
        var path = KnowledgeFiles.CopyExample("cities.yaml", _root);
        await using var session = new KnowledgeSession(path);
        Assert.Equal("", session.Refusal);

        // Act.
        File.Delete(path);

        // Assert: the session stops claiming the file opens and says why, and keeps the last good table.
        Assert.True(await Eventually(() => session.Refusal.Length > 0), "The session never learned its file was deleted.");
        Assert.Equal("The file no longer exists.", session.Refusal);
        Assert.Equal("Cities", session.Baseline().Title);
    }

    [Fact]
    public async Task ALostEventsWindow_IsLearned_ByReadingTheFileAgain()
    {
        // Arrange.
        var path = KnowledgeFiles.CopyExample("cities.yaml", _root);
        await using var session = new KnowledgeSession(path);
        var watcher = WatcherOf(session);
        var pushed = Collect(session);

        // Act: lose the change - nothing is watching while it happens - then say events were lost.
        watcher.EnableRaisingEvents = false;
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, text.Replace("name: Cities", "name: Towns", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        RaiseOverflow(watcher);

        // Assert: synchronously, because the watcher's own Error is.
        Assert.Equal("Towns", pushed.OfType<TableStructureChanged>().Single().Title);
        Assert.Equal("Towns", session.Baseline().Title);
    }

    [Fact]
    public async Task TheWatcher_IsSubscribedBeforeItIsEnabled_ToEveryEventAPublishRaises()
    {
        // Arrange.
        var path = KnowledgeFiles.CopyExample("cities.yaml", _root);
        await using var session = new KnowledgeSession(path);
        var watcher = WatcherOf(session);

        // Assert: enabled, and none of the five left without a handler.
        Assert.True(watcher.EnableRaisingEvents);
        foreach (var name in new[] { "Changed", "Created", "Renamed", "Deleted", "Error" })
        {
            Assert.True(HasHandler(watcher, name), $"The session's watcher has no handler for {name}.");
        }

        // And in that order. A handler attached after the watcher was enabled is attached by now
        // too, so a running watcher cannot say which came first; the session's own source can. A
        // watcher enabled first is live with nobody listening, and what it raises then is lost.
        var source = await File.ReadAllTextAsync(KnowledgeFiles.SessionSource, TestContext.Current.CancellationToken);
        var enabled = source.IndexOf("_watcher.EnableRaisingEvents = true", StringComparison.Ordinal);
        Assert.True(enabled > 0, "The session no longer enables its watcher the way this test reads it.");
        foreach (var name in new[] { "Changed", "Created", "Renamed", "Deleted", "Error" })
        {
            var subscribed = source.IndexOf($"_watcher.{name} +=", StringComparison.Ordinal);
            Assert.True(subscribed > 0, $"The session no longer subscribes to {name} the way this test reads it.");
            Assert.True(subscribed < enabled, $"The session subscribes to {name} after enabling its watcher.");
        }
    }

    // ---- the read that is refused -----------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ARefusedReRead_IsTriedAgain_SoTheChangeIsNotLost(int refusals)
    {
        // Arrange: a read that can be told to refuse, as a file mid-replace does.
        var path = KnowledgeFiles.CopyExample("cities.yaml", _root);
        var read = new ScriptedRead("The file no longer exists.");
        await using var session = new KnowledgeSession(path, read.Read);
        var pushed = Collect(session);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        WatcherOf(session).EnableRaisingEvents = false;
        await File.WriteAllTextAsync(path, text.Replace("name: Cities", "name: Towns", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        read.Reads = 0;

        // Act.
        read.ToRefuse = refusals;
        session.OnExternalChange();

        // Assert: read until it opened, and the change arrived.
        Assert.Equal(refusals + 1, read.Reads);
        Assert.Equal("", session.Refusal);
        Assert.Equal("Towns", pushed.OfType<TableStructureChanged>().Single().Title);
    }

    [Fact]
    public async Task AReadThatKeepsBeingRefused_IsGivenUp_AndTheLastTableStays()
    {
        // Arrange.
        var path = KnowledgeFiles.CopyExample("cities.yaml", _root);
        var read = new ScriptedRead("The file cannot be read: it is in use.");
        await using var session = new KnowledgeSession(path, read.Read);
        var pushed = Collect(session);
        read.Reads = 0;

        // Act.
        read.ToRefuse = int.MaxValue;
        session.OnExternalChange();

        // Assert: three attempts, then the refusal is kept and nothing is pushed over the last good table.
        Assert.Equal(3, read.Reads);
        Assert.Equal("The file cannot be read: it is in use.", session.Refusal);
        Assert.Empty(pushed);
        Assert.Equal("Cities", session.Baseline().Title);
    }

    // ---- what cannot be edited ----------------------------------------------------------------------

    [Fact]
    public async Task AFileThatCannotBeRead_IsItsReason_RefusesEveryEdit_AndIsNotWritten()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "broken.yaml");
        await File.WriteAllTextAsync(path, "ded: \"0.1\"\ndesigner: etalii/knowledge\nname: [unclosed\n", TestContext.Current.CancellationToken);
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        await using var session = new KnowledgeSession(path);

        // Act.
        var baseline = session.Baseline();
        var answers = new[] { "setCell", "addRow", "renameColumn", "addView", "deleteColumn" }.Select(kind => session.Edit(ShortGuid.NewShortGuid(), new TableGesture(kind))).ToList();

        // Assert: shown as its reason with its position, no line to add a row at, and every edit refused with that reason.
        Assert.StartsWith("This file cannot be read: ", baseline.ReadOnlyReason, StringComparison.Ordinal);
        Assert.Empty(baseline.Columns);
        Assert.Equal(0, baseline.RowCount);
        Assert.All(answers, answer => Assert.Equal(baseline.ReadOnlyReason, answer));
        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFileOfAnotherVersion_RefusesEveryEdit_AndIsNotWritten()
    {
        // Arrange.
        var path = KnowledgeFiles.CopyExample("cities.yaml", _root);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, text.Replace("ded: \"0.1\"", "ded: \"9.0\"", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        await using var session = new KnowledgeSession(path);

        // Act.
        var answers = new[] { "setCell", "addRow", "renameColumn" }.Select(kind => session.Edit(ShortGuid.NewShortGuid(), new TableGesture(kind))).ToList();

        // Assert.
        Assert.Equal(KnowledgeBody.AnotherVersion, session.Baseline().ReadOnlyReason);
        Assert.All(answers, answer => Assert.Equal(KnowledgeBody.AnotherVersion, answer));
        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFileThatIsNotThere_IsItsRefusal()
    {
        // Act.
        await using var session = new KnowledgeSession(IoPath.Combine(_root, "missing.yaml"));

        // Assert.
        Assert.Equal("The file no longer exists.", session.Baseline().ReadOnlyReason);
        Assert.Equal("The file no longer exists.", session.Edit(ShortGuid.NewShortGuid(), new TableGesture("addRow")));
    }

    // ---- the module's registrations ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public void ANewFile_IsOneTitlePropertyOneViewAndNoRows_WithIdsOfItsOwn(string extension)
    {
        // Arrange.
        var template = new KnowledgeDocumentTemplate();
        var format = Designer.Definitions.Single().Formats.Single(candidate => candidate.Extension == extension);

        // Act: twice, because an id is never reused.
        var first = KnowledgeBody.Read(System.Text.Encoding.UTF8.GetBytes(template.Create(format, "Suppliers" + extension)!), "Suppliers" + extension);
        var second = KnowledgeBody.Read(System.Text.Encoding.UTF8.GetBytes(template.Create(format, "Suppliers" + extension)!), "Suppliers" + extension);

        // Assert.
        Assert.Equal("", first.Unreadable);
        Assert.Empty(first.Model.Findings);
        Assert.Equal("Suppliers", first.Table.Name);
        var property = Assert.Single(first.Table.Properties);
        Assert.True(property.IsTitle);
        Assert.Equal("text", property.ValueType);
        var view = Assert.Single(first.Table.Views);
        Assert.Empty(first.Table.Rows);

        // The ids are ShortGuids: twenty-five characters of base 36, and different every time.
        Assert.Matches("^[0-9a-z]{25}$", property.Id);
        Assert.Matches("^[0-9a-z]{25}$", view.Id);
        Assert.NotEqual(property.Id, view.Id);
        Assert.NotEqual(property.Id, second.Table.Properties.Single().Id);
    }

    [Fact]
    public void TheDefinition_OffersTheThreeFormats_AndServesItsOwnOrigin()
    {
        // Act.
        var definition = Assert.Single(Designer.Definitions);

        // Assert.
        Assert.Equal("etalii/knowledge", definition.Origin);
        Assert.Equal([".yaml", ".json", ".xml"], definition.Formats.Select(format => format.Extension));
        Assert.NotNull(definition.Build);
        using var histories = new HistoryStackStore(new CommandDispatcher(new NoServices()));
        Assert.Equal(definition.Origin, new KnowledgeSessionFactory(histories, new KnowledgeDocuments()).Origin);
        Assert.Equal(definition.Origin, new KnowledgeDocumentTemplate().Origin);
        Assert.Null(new KnowledgeDocumentTemplate().Create(new DesignerFormat("TOML", ".toml"), "x.toml"));
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>A read that refuses as many times as it is told to, as a file mid-replace does, and counts how often it was asked.</summary>
    private sealed class ScriptedRead(string refusal)
    {
        public int ToRefuse { get; set; }

        public int Reads { get; set; }

        public KnowledgeRead Read(string bodyPath)
        {
            Reads++;
            if (ToRefuse > 0)
            {
                ToRefuse--;
                return new KnowledgeRead(null, refusal);
            }

            return KnowledgeDocumentStore.Read(bodyPath);
        }
    }

    private static List<TableChange> Collect(KnowledgeSession session)
    {
        var pushed = new List<TableChange>();
        session.Changed += (_, args) =>
        {
            lock (pushed)
            {
                pushed.AddRange(args.Changes);
            }
        };
        return pushed;
    }

    private static FileSystemWatcher WatcherOf(KnowledgeSession session) =>
        typeof(KnowledgeSession).GetField("_watcher", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(session) as FileSystemWatcher
        ?? throw new InvalidOperationException("KnowledgeSession no longer keeps its watcher in _watcher; this test must follow it.");

    private static bool HasHandler(FileSystemWatcher watcher, string eventName)
    {
        // FileSystemWatcher keeps each event's handlers in a private field named for the event.
        var field = typeof(FileSystemWatcher).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.Name.Contains(eventName, StringComparison.OrdinalIgnoreCase) && typeof(Delegate).IsAssignableFrom(candidate.FieldType))
            ?? throw new InvalidOperationException($"FileSystemWatcher no longer keeps its {eventName} handlers where this test looks.");
        return field.GetValue(watcher) is Delegate;
    }

    // The one signal a watcher gives when it has dropped events, raised the way the watcher raises it.
    private static void RaiseOverflow(FileSystemWatcher watcher) =>
        (typeof(FileSystemWatcher).GetMethod("OnError", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FileSystemWatcher.OnError was not found."))
        .Invoke(watcher, [new ErrorEventArgs(new InternalBufferOverflowException())]);

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            bool met;
            try
            {
                met = condition();
            }
            catch (InvalidOperationException)
            {
                // The list was being added to while it was read; look again.
                met = false;
            }

            if (met)
            {
                return true;
            }

            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
