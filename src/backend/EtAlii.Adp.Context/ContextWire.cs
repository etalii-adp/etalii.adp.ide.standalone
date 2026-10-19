using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Context;

/// <summary>
/// Maps the context service's records to the contract's proto and back - the one place that
/// mapping lives, as <c>DiagramWire</c> and <c>TableWire</c> are for theirs. <see cref="ContextService"/>
/// decides what to answer; this says how the answer looks on the wire.
/// </summary>
internal static class ContextWire
{
    /// <summary>The dialog an execution asks for, as the prompt of one interaction.</summary>
    public static ContextPrompt PromptOf(ShortGuid interactionId, ContextExecutionResult execution, FileDialogPrompt? fileDialog)
    {
        var prompt = new ContextPrompt { InteractionId = interactionId };
        switch (execution)
        {
            case ContextExecutionRequiresInput requiresInput:
                prompt.InputDialog = ToProto(requiresInput.Request);
                break;
            case ContextExecutionRequiresConfirmation requiresConfirmation:
                prompt.ConfirmDialog = ToProto(requiresConfirmation.Request);
                break;
            case ContextExecutionRequiresChoice requiresChoice:
                prompt.ChoiceDialog = ToProto(requiresChoice.Request);
                break;
            case ContextExecutionRequiresFile:
                prompt.FileDialog = fileDialog;
                break;
        }

        return prompt;
    }

    /// <summary>
    /// The project-relative form of an absolute path the backend produced - the one shape a
    /// path is ever allowed to take on the wire, and the same one a selection carries.
    /// </summary>
    public static Path ToRelativePath(string rootPath, string fullPath) =>
        Path.OfLocation(IoPath.GetRelativePath(rootPath, fullPath));

    public static ContextShortcutDefinition FromProto(ContextShortcut shortcut) =>
        new(shortcut.Key, shortcut.Ctrl, shortcut.Shift, shortcut.Alt, shortcut.Meta);

    /// <summary>The input dialog a provider asked for, with the marker that makes it an edit in place.</summary>
    public static InputDialogPrompt ToProto(ContextInputRequest request)
    {
        var prompt = new InputDialogPrompt
        {
            Title = request.Title,
            Icon = request.Icon,
            FieldLabel = request.FieldLabel,
            InitialValue = request.InitialValue,
            ConfirmLabel = request.ConfirmLabel,
        };

        // Left unset for an empty id rather than set to an empty marker: an unset field is what
        // tells the client to render the dialog exactly as it does today, and a marker carrying
        // no element would be a claim the canvas cannot check.
        if (request.InlineLabelElementId.Length > 0)
        {
            prompt.InlineLabelEdit = new InlineLabelEdit { ElementId = new ElementId { Value = request.InlineLabelElementId } };
        }

        return prompt;
    }

    private static ConfirmDialogPrompt ToProto(ContextConfirmationRequest request) => new()
    {
        Title = request.Title,
        Icon = request.Icon,
        Message = request.Message,
        ConfirmLabel = request.ConfirmLabel,
        Danger = request.Danger,
    };

    public static FileDialogPrompt ToProto(ContextFileRequest request, string rootPath)
    {
        var prompt = new FileDialogPrompt
        {
            Title = request.Title,
            Icon = request.Icon,
            ConfirmLabel = request.ConfirmLabel,
            EmptyMessage = request.EmptyMessage,
        };
        if (request.NameField is { } nameField)
        {
            prompt.NameField = new ContextTextField { Label = nameField.Label, InitialValue = nameField.InitialValue };
        }

        if (rootPath.Length == 0)
        {
            // No project to list: the dialog says so with its empty message.
            return prompt;
        }

        foreach (var pinned in request.Pinned)
        {
            if (ContextFileTree.IdOf(rootPath, pinned.FullPath) is { } id && File.Exists(pinned.FullPath))
            {
                prompt.Pinned.Add(new ContextOption { Id = id, Label = pinned.Label, Selectable = true, Icon = "mdi-pin-outline", Description = id });
            }
        }

        prompt.Files.AddRange(ContextFileTree.Build(rootPath, request.Accepts).Select(ToProto));
        return prompt;
    }

    /// <summary>The ids of every option under <paramref name="option"/>, itself included, that can be chosen.</summary>
    public static IEnumerable<string> Selectable(ContextOption option) =>
        option.Selectable ? [option.Id] : option.Children.SelectMany(Selectable);

    private static ChoiceDialogPrompt ToProto(ContextChoiceRequest request)
    {
        var prompt = new ChoiceDialogPrompt
        {
            Title = request.Title,
            Icon = request.Icon,
            ConfirmLabel = request.ConfirmLabel,
            EmptyMessage = request.EmptyMessage,
        };
        if (request.NameField is { } nameField)
        {
            prompt.NameField = new ContextTextField { Label = nameField.Label, InitialValue = nameField.InitialValue };
        }

        prompt.Options.AddRange(request.Options.Select(ToProto));
        return prompt;
    }

    private static ContextOption ToProto(ContextOptionNode node)
    {
        var option = new ContextOption
        {
            Id = node.Id,
            Label = node.Label,
            Selectable = node.Selectable,
            SuggestedValue = node.SuggestedValue,
            Description = node.Description,
            Icon = node.Icon,
            NameSuppressedReason = node.NameSuppressedReason,
            UnavailableReason = node.UnavailableReason,
        };
        if (node.Children is { Count: > 0 } children)
        {
            option.Children.AddRange(children.Select(ToProto));
        }

        return option;
    }

    /// <summary>One row of the property grid.</summary>
    public static ContextProperty ToProto(ContextPropertyDefinition definition)
    {
        var property = new ContextProperty
        {
            Id = definition.Id,
            Label = definition.Label,
            Value = definition.Value,
            Editor = definition.Editor,
            ReadOnlyReason = definition.ReadOnlyReason,
            Group = definition.Group,
        };

        // Only a Choice, a Slider and a Tags row have any, and an empty repeated field costs nothing on the wire - so this
        // adds nothing to every property that is not one.
        property.Candidates.AddRange(definition.Choices);
        return property;
    }
}
