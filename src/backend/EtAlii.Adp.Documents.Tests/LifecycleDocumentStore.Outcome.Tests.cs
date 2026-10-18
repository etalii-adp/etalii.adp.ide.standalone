using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The shared store keeps what a module's parser reports through a <see cref="ParseOutcome{TModel}"/>:
/// the problems travel on the entry, and a body the parser cannot read is not written to
/// (code-structure FR-049).
/// </summary>
public sealed class LifecycleDocumentStoreOutcomeTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-lifecycle-outcome-" + Guid.NewGuid().ToString("N"));

    public LifecycleDocumentStoreOutcomeTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => TestFolder.TryDelete(_folder);

    private string PathOf(string name) => IoPath.Combine(_folder, name);

    private string Write(string text)
    {
        var path = PathOf("notes.txt");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void AnEntry_CarriesWhatTheParserPassedOver_AndStaysUsable()
    {
        // Arrange.
        var path = Write("a\n?b\nc\n");

        // Act.
        var entry = new OutcomeStore().GetOrLoad(path);

        // Assert.
        Assert.True(entry.IsUsable);
        Assert.Equal([new ParseProblem(1, "?b")], entry.Problems);
        Assert.Equal(entry.Problems, entry.Outcome.Problems);
        Assert.Equal("a\n?b\nc\n", entry.Outcome.Model);
    }

    [Fact]
    public async Task ABodyItsParserCannotRead_IsUnusable_AndIsNotWritten()
    {
        // Arrange.
        var path = Write("a !! b\n");
        var store = new OutcomeStore();
        var entry = store.GetOrLoad(path);
        entry.Document.Replace(new LineRange(0, 0), ["after"]);

        // Act.
        var saved = await store.SaveAsync(path, entry.Document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.Equal("the body holds !!", entry.Unreadable);
        Assert.True(saved.Failed);
        Assert.Equal("a !! b\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AMissingBody_OpensAsAUsableNewDocument_EvenWhereItsParserCannotReadNoText()
    {
        // Act.
        var entry = new OutcomeStore().GetOrLoad(PathOf("missing.txt"));

        // Assert.
        Assert.True(entry.IsUsable, entry.Unreadable);
    }

    /// <summary>
    /// A store over plain lines whose parser reports each line starting with <c>?</c> as a problem,
    /// and cannot read a body holding <c>!!</c> or no text at all.
    /// </summary>
    private sealed class OutcomeStore() : LifecycleDocumentStore<LineDocument, string>(new FileWriter())
    {
        protected override LineDocument Read(string text) => LineDocument.Parse(text);

        protected override string TextOf(LineDocument body) => body.Text;


        protected override ParseOutcome<string> Parse(LineDocument body)
        {
            var text = body.Text;
            var problems = body.Lines
                .Select((line, index) => (line.Text, index))
                .Where(line => line.Text.StartsWith('?'))
                .Select(line => new ParseProblem(line.index, line.Text))
                .ToList();
            var unreadable = text.Length == 0 ? "there is no text" : text.Contains("!!", StringComparison.Ordinal) ? "the body holds !!" : "";
            return new ParseOutcome<string>(text, problems, unreadable);
        }
    }
}
