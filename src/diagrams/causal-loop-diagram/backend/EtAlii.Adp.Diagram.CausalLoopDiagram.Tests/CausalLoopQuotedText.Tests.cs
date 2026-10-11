using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>
/// A label or loop name is written between double quotes, and the format has no escape for a quote
/// inside them. A label holding one was written anyway, as a line the parser then split at that
/// quote, so the text read back was not the text written. The writer refuses it instead, and leaves
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
    public void AQuoteInsideQuotedText_IsRefused_AndTheDocumentIsLeftAlone(string write)
    {
        // Arrange.
        var document = CausalLoopDocument.Parse(Text);
        var model = CausalLoopParser.Parse(document).Model;

        // Act.
        var refusal = write switch
        {
            "add variable" => CausalLoopWriter.AddVariable(document, model, "deaths", Quoted),
            "variable label" => CausalLoopWriter.SetVariableLabel(document, model, "births", Quoted),
            "link label" => CausalLoopWriter.SetLinkLabel(document, model, "population", "births", Quoted),
            "add loop" => CausalLoopWriter.AddLoop(document, model, "R2", Quoted, ["population", "births"]),
            "loop name" => CausalLoopWriter.SetLoopName(document, model, "R1", Quoted),
            _ => throw new ArgumentOutOfRangeException(nameof(write)),
        };

        // Assert.
        Assert.Equal(CausalLoopWriter.UnusableText, refusal);
        Assert.Equal(Text, document.Text);
    }
}
