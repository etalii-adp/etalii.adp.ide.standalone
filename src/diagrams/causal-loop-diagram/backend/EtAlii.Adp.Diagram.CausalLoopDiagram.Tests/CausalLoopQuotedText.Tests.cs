using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>
/// A label or loop name is written between double quotes, and the format has no escape for a quote
/// inside them. A label holding one was once written anyway, as a line the reading then split at that
/// quote, so the text read back was not the text written. Every edit refuses it instead, and leaves
/// the document as it was.
/// </summary>
public class CausalLoopQuotedTextTests
{
    private const string Text =
        "causal-loop 1\r\n"
        + "\r\n"
        + "variable population \"Population\"\r\n"
        + "variable births \"Births\"\r\n"
        + "\r\n"
        + "link population -> births +\r\n"
        + "link births -> population +\r\n"
        + "\r\n"
        + "loop R1 \"Births beget births\" population births\r\n";

    private const string Quoted = "The \"real\" births";

    public static TheoryData<string> Writes => ["add variable", "variable label", "link label", "add loop", "loop name"];

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task AQuoteInsideQuotedText_IsRefused_AndTheDocumentIsLeftAlone(string write)
    {
        // Arrange.
        var folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = IoPath.Combine(folder, "quoted.cld");
            await File.WriteAllTextAsync(path, Text, TestContext.Current.CancellationToken);
            var store = new CausalLoopDocumentStore();
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act.
            var result = write switch
            {
                "add variable" => await new AddVariableCommandHandler(store).ExecuteAsync(new AddVariableCommand(path, "deaths", Quoted), cancellationToken),
                "variable label" => await new SetVariableLabelCommandHandler(store).ExecuteAsync(new SetVariableLabelCommand(path, "births", Quoted), cancellationToken),
                "link label" => await new SetLinkLabelCommandHandler(store).ExecuteAsync(new SetLinkLabelCommand(path, "population", "births", Quoted), cancellationToken),
                "add loop" => await new AddLoopCommandHandler(store).ExecuteAsync(new AddLoopCommand(path, "R2", Quoted, ["population", "births"]), cancellationToken),
                "loop name" => await new SetLoopNameCommandHandler(store).ExecuteAsync(new SetLoopNameCommand(path, "R1", Quoted), cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(write)),
            };

            // Assert.
            Assert.Equal(CausalLoopEdits.UnusableText, result.Error);
            Assert.Equal(Text, await File.ReadAllTextAsync(path, cancellationToken));
        }
        finally
        {
            TestFolder.TryDelete(folder);
        }
    }
}
