using EtAlii.Adp.Common;
using Xunit;
// The generated gRPC stub for `service ContextService` claims the same simple name in
// EtAlii.Adp, so the backend's own service has to be named through an alias here.
using BackendContextService = EtAlii.Adp.Context.ContextService;

namespace EtAlii.Adp.Context.Tests;

/// <summary>
/// The one bit of the input prompt that is not a straight field copy: the marker saying this
/// prompt edits the label the user can see, which a canvas reads to render an editor in place
/// of that label rather than a dialog (inline-rename Requirements 2.1, 2.2, 2.5).
/// </summary>
/// <remarks>
/// Both cases are pinned because the defects are opposite and both are silent. A marker that
/// survives an empty id makes EVERY input prompt claim to be a label edit - and the forty-five
/// prompts asking for run-if values, cluster keys, prefix declarations and predicate IRIs would
/// all start trying to render inline. A mapping that drops a set id makes the feature simply
/// never happen, with the dialog appearing as it always did and nothing to see in a log.
/// </remarks>
public class ContextInputPromptTests
{
    private static ContextInputRequest Request(string inlineLabelElementId = "") =>
        new("Rename node", "mdi-pencil-outline", "Text", "before", "Rename", inlineLabelElementId);

    [Fact]
    public void ARequestWithoutAnElementId_ProducesAPromptWithNoMarker()
    {
        // Arrange.
        var request = Request();

        // Act.
        var prompt = BackendContextService.ToProto(request);

        // Assert.
        // Unset, not "set but empty": the client's whole rule is that an unset field renders the
        // dialog it renders today, so an empty marker would be a claim with nothing behind it.
        Assert.Null(prompt.InlineLabelEdit);
        Assert.Equal("before", prompt.InitialValue);
    }

    [Fact]
    public void ARequestWithAnElementId_ProducesAPromptCarryingExactlyThatId()
    {
        // Arrange.
        var request = Request("node-7");

        // Act.
        var prompt = BackendContextService.ToProto(request);

        // Assert.
        Assert.NotNull(prompt.InlineLabelEdit);
        Assert.Equal("node-7", prompt.InlineLabelEdit.ElementId.Value);

        // The rest of the prompt is untouched by the marker; a label edit is still an ordinary
        // input prompt, and a client that ignores the field sees exactly what it saw before.
        Assert.Equal("Rename node", prompt.Title);
        Assert.Equal("Text", prompt.FieldLabel);
        Assert.Equal("before", prompt.InitialValue);
        Assert.Equal("Rename", prompt.ConfirmLabel);
    }
}
