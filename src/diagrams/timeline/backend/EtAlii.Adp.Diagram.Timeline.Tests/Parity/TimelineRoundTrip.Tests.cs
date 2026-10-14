using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests.Parity;

/// <summary>
/// Byte-exact round trips over the parity transcript's corpus (timeline-disl-fbl Requirements 3.6 and
/// 3.7), the two line-ending fixtures and the one without a final newline among them: a document saved
/// without a change, or with every value set to what it already holds, is written back byte for byte;
/// and every edit, undone, puts back the bytes it started from.
/// </summary>
public sealed class TimelineRoundTripTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.TimelineRoundTrip");

    public void Dispose() => _folder.Dispose();

    public static TheoryData<string> Documents() => [.. TimelineTranscript.Corpus()];

    private static Task<byte[]> ReadAsync(string relative) =>
        File.ReadAllBytesAsync(Path.Combine(TimelineTranscript.SourceFolder, relative), TestContext.Current.CancellationToken);

    private async Task<string> CopyAsync(byte[] bytes)
    {
        var path = _folder.Combine($"{Guid.NewGuid():N}.tml");
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        return path;
    }

    [Fact]
    public void TheCorpusHoldsTheLineEndingAndFinalNewlineFixtures()
    {
        // Arrange.
        var corpus = TimelineTranscript.Corpus();

        // Act.
        var names = corpus.Select(Path.GetFileName).ToList();

        // Assert: the documents Requirement 3.7 names are among those the theories below run over.
        Assert.Contains("crlf-line-endings.tml", names);
        Assert.Contains("lf-line-endings.tml", names);
        Assert.Contains("no-trailing-newline.tml", names);
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task ADocumentSavedWithoutAChange_IsWrittenBackByteForByte(string relative)
    {
        // Arrange.
        var bytes = await ReadAsync(relative);
        var path = await CopyAsync(bytes);
        var store = new TimelineDocumentStore();

        // Act.
        var entry = store.GetOrLoad(path);
        var saved = store.Save(path, entry.Document);

        // Assert: written back as it was; a document that could not be read is not written at all.
        var after = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Assert.True(saved.Failed != entry.IsUsable, $"{relative}: {saved.Error}");
        Assert.True(bytes.SequenceEqual(after), $"{relative}: saving it unchanged changed its bytes");
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task ASaveWithoutAChange_PlansNoSplice(string relative)
    {
        // Arrange.
        var text = Encoding.UTF8.GetString(await ReadAsync(relative));
        var body = TimelineBody.Parse(text);

        // Act: what the library plans for a save of a body nobody changed (FBL section 15.3).
        var outcome = body.Model.Unreadable ? EditOutcome.Applied : body.Change(new ModelChange.Save());

        // Assert.
        Assert.True(outcome.WasApplied, $"{relative}: {outcome.Refusal}");
        Assert.True(text == body.Text, $"{relative}: a save without a change spliced the body to\n{body.Text}");
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryValueSetToWhatItHolds_WritesTheSameBytes(string relative)
    {
        // Arrange.
        var bytes = await ReadAsync(relative);
        var token = TestContext.Current.CancellationToken;
        var store = new TimelineDocumentStore();
        var path = await CopyAsync(bytes);
        var model = store.GetOrLoad(path).Model;
        var lines = Encoding.UTF8.GetString(bytes).Split('\n');
        var edits = new List<(string Name, Func<Task<CommandResult>> Run)>();

        // A command names an element by its written id, and an id written twice names its first holder.
        foreach (var element in model.Elements.Where(element => element.Id.Length > 0).DistinctBy(element => element.Id, StringComparer.Ordinal))
        {
            edits.Add(($"rename {element.Id}", () => new RenameTimelineElementCommandHandler(store).ExecuteAsync(new RenameTimelineElementCommand(path, element.Id, element.Label), token)));

            // A row that does not read (1.0, twelve) is row 0, and placing the element on row 0 writes it
            // as 0: that is a change of the text, not the value it already holds.
            if (WrittenRow(lines, element) is not { } written || written == element.Row.ToString(CultureInfo.InvariantCulture))
            {
                edits.Add(($"place {element.Id}", () => new SetTimelinePlacementCommandHandler(store).ExecuteAsync(new SetTimelinePlacementCommand(path, element.Id, element.Begin.Text, element.End?.Text, element.Row), token)));
            }
        }

        foreach (var connection in model.Connections.Where(connection => connection.Id.Length > 0).DistinctBy(connection => connection.Id, StringComparer.Ordinal))
        {
            edits.Add(($"relabel {connection.Id}", () => new RelabelTimelineConnectionCommandHandler(store).ExecuteAsync(new RelabelTimelineConnectionCommand(path, connection.Id, connection.Label), token)));
        }

        foreach ((string name, Func<Task<CommandResult>> run) in edits)
        {
            // Act.
            await run();

            // Assert: done or refused, nothing was written that was not already there.
            var after = await File.ReadAllBytesAsync(path, token);
            Assert.True(bytes.SequenceEqual(after), $"{relative} {name}: setting what it holds changed the text to\n{Encoding.UTF8.GetString(after)}");
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryEditUndone_RestoresTheBytes(string relative)
    {
        // Arrange.
        var bytes = await ReadAsync(relative);
        var token = TestContext.Current.CancellationToken;
        var applied = 0;

        foreach ((string name, Func<string, ICommand> edit) in Edits(new TimelineDocumentStore().GetOrLoad(await CopyAsync(bytes)).Model))
        {
            var path = await CopyAsync(bytes);
            var store = new TimelineDocumentStore();
            using var history = new HistoryStackStore(new TimelineTestDispatcher(store));
            var stack = history.Get(_folder.Path);

            // Act.
            var result = await stack.ExecuteAsync(edit(path), token);
            if (result.IsSuccess)
            {
                applied++;
                var undone = await stack.UndoAsync(token);
                Assert.True(undone.IsSuccess, $"{relative} {name}: {undone.Error}");
            }

            // Assert: undone, or refused with nothing written.
            var after = await File.ReadAllBytesAsync(path, token);
            Assert.True(bytes.SequenceEqual(after), $"{relative} {name}: the bytes did not come back; they read\n{Encoding.UTF8.GetString(after)}");
        }

        // A document that cannot be read takes no edit; every other takes at least the adds.
        Assert.True(applied >= 2 || new TimelineDocumentStore().GetOrLoad(await CopyAsync(bytes)).Model.Elements.Count == 0, $"{relative}: only {applied} edits applied, so the round trips prove little.");
    }

    /// <summary>The text the row of <paramref name="element"/> is written with, without its quotes; null for no row key.</summary>
    private static string? WrittenRow(string[] lines, TimelineElement element) =>
        lines.Skip(element.Range.Start).Take(element.Range.Length)
            .Select(line => Regex.Match(line, @"^[\s-]*row:\s*([^#\r]*?)\s*(#.*)?\r?$"))
            .FirstOrDefault(match => match.Success)?.Groups[1].Value.Trim('"', '\'');

    /// <summary>One edit of every command, against the first elements and relation of <paramref name="model"/>.</summary>
    private static IEnumerable<(string Name, Func<string, ICommand> Edit)> Edits(TimelineModel model)
    {
        var elements = model.Elements.Where(element => element.Id.Length > 0).ToList();
        var first = elements.FirstOrDefault();
        var firstId = first?.Id ?? "nobody";
        var secondId = elements.Select(element => element.Id).FirstOrDefault(id => id != firstId) ?? firstId;
        var period = elements.FirstOrDefault(element => element.IsPeriod)?.Id ?? firstId;
        var moment = elements.FirstOrDefault(element => !element.IsPeriod)?.Id ?? firstId;
        var connection = model.Connections.FirstOrDefault(candidate => candidate.Id.Length > 0)?.Id ?? "nobody";
        var row = ((first?.Row ?? 0) + 1).ToString(CultureInfo.InvariantCulture);

        yield return ($"rename {firstId}", path => new RenameTimelineElementCommand(path, firstId, "Round trip"));
        yield return ($"rename {firstId} to nothing", path => new RenameTimelineElementCommand(path, firstId, ""));
        yield return ($"place {firstId} on row {row}", path => new SetTimelinePlacementCommand(path, firstId, "2026-01-12", first?.End is null ? null : "2026-12-12", (first?.Row ?? 0) + 1));
        yield return ($"give {moment} an end", path => new SetTimelineEndCommand(path, moment, "2100-01-01"));
        yield return ($"remove the end of {period}", path => new SetTimelineEndCommand(path, period, null));
        yield return ($"relabel {connection}", path => new RelabelTimelineConnectionCommand(path, connection, "Round trip"));
        yield return ($"clear the label of {connection}", path => new RelabelTimelineConnectionCommand(path, connection, ""));
        yield return ($"disconnect {connection}", path => new DisconnectTimelineConnectionCommand(path, connection));
        yield return ($"remove {firstId}", path => new RemoveTimelineElementCommand(path, firstId));
        yield return ("add a period", path => new AddTimelineElementCommand(path, "roundtrp", "Round trip", "2026-03-01", "2026-03-15", 2));
        yield return ("add a moment", path => new AddTimelineElementCommand(path, "roundtrm", "Round trip", "2026-03-01", null, 2));
        yield return ($"add one related to {firstId}", path => new AddConnectedTimelineElementCommand(path, firstId, "roundtre", "roundtrr", "2100-01-01", "2100-01-15", 3));
        yield return ($"connect {secondId} to {firstId}", path => new ConnectTimelineElementsCommand(path, "roundtrc", secondId, firstId, "Round trip"));
        yield return ("arrange", path => new ArrangeTimelineCommand(path));
    }
}
