using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Adds a view to an existing model, and the <c>.adp</c> that opens it. This is what makes a
/// second diagram of one model - the "model once, view many" of Requirement 2.7.
/// </summary>
/// <param name="BodyPath">The <c>.dsl</c> the view is appended to.</param>
/// <param name="RegistrationPath">The <c>.adp</c> to create, naming that body and this view.</param>
/// <param name="MimeType">The C4 type the registration declares.</param>
/// <param name="ViewKind">Which view to declare.</param>
/// <param name="ViewKey">The key the registration's <c>view:</c> header names.</param>
/// <param name="BodyRelativePath">The body, relative to the registration's own folder, as the header records it.</param>
public sealed record AddC4ViewCommand(
    string BodyPath,
    string RegistrationPath,
    string MimeType,
    C4ViewKind ViewKind,
    string ViewKey,
    string BodyRelativePath) : ICommand;

/// <summary>Undoes an <see cref="AddC4ViewCommand"/>: removes the registration and the view block.</summary>
public sealed record RemoveC4ViewCommand(string BodyPath, string RegistrationPath, string ViewKey) : ICommand;

internal sealed class AddC4ViewCommandHandler(IC4DocumentStore documents) : ICommandHandler<AddC4ViewCommand>
{
    public Task<CommandResult> ExecuteAsync(AddC4ViewCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = documents.WorkspaceOf(command.BodyPath);
        if (workspace.FindView(command.ViewKey) is not null)
        {
            return Task.FromResult(CommandResult.Failure($"This model already has a view called '{command.ViewKey}'."));
        }

        var document = documents.GetOrLoad(command.BodyPath);
        var insertAt = C4ViewBlock.EndOfViewsBlock(document);
        if (insertAt is null)
        {
            return Task.FromResult(CommandResult.Failure("This document has no views block to add a view to."));
        }

        // The scope a view needs: the first software system for most kinds, and none at all for
        // a landscape, which is a context diagram without a focus.
        var scope = workspace.Elements.FirstOrDefault(element => element.Kind == C4ElementKind.SoftwareSystem)?.Id;
        if (scope is null && command.ViewKind != C4ViewKind.SystemLandscape)
        {
            return Task.FromResult(CommandResult.Failure("This model has no software system for the view to be about."));
        }

        var lines = C4ViewBlock.Declare(command.ViewKind, command.ViewKey, scope, workspace);
        for (var index = 0; index < lines.Count; index++)
        {
            document.InsertLine(insertAt.Value + (uint)index, lines[index]);
        }

        var saved = documents.Save(command.BodyPath, document);
        if (saved.Failed)
        {
            // The body never reached the disk, so writing a registration pointing at a view
            // it does not contain would be worse than stopping here.
            return Task.FromResult(CommandResult.Failure(saved.Error));
        }

        try
        {
            AdpFileWriter.Save(
                command.RegistrationPath,
                command.MimeType + "\n" + "body: " + command.BodyRelativePath + "\n" + "view: " + command.ViewKey + "\n");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The view is already in the document; leaving it there with no registration would
            // be a half-done job, so undo it before reporting.
            C4ViewBlock.RemoveView(documents, command.BodyPath, command.ViewKey);
            return Task.FromResult(CommandResult.Failure($"Could not create {Path.GetFileName(command.RegistrationPath)}: {exception.Message}"));
        }

        return Task.FromResult(CommandResult.Success(
            new RemoveC4ViewCommand(command.BodyPath, command.RegistrationPath, command.ViewKey)));
    }
}

internal sealed class RemoveC4ViewCommandHandler(IC4DocumentStore documents) : ICommandHandler<RemoveC4ViewCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveC4ViewCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = documents.WorkspaceOf(command.BodyPath);
        var view = workspace.FindView(command.ViewKey);
        if (view is null)
        {
            return Task.FromResult(CommandResult.Failure($"This model has no view called '{command.ViewKey}'."));
        }

        var kind = view.ViewKindOrDefault();
        C4ViewBlock.RemoveView(documents, command.BodyPath, command.ViewKey);

        try
        {
            if (File.Exists(command.RegistrationPath))
            {
                AdpFileWriter.Delete(command.RegistrationPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(CommandResult.Failure($"Could not remove {Path.GetFileName(command.RegistrationPath)}: {exception.Message}"));
        }

        // The inverse needs the body path relative to the registration's own folder; the
        // registration and its body sit side by side, so the file name is the whole path.
        return Task.FromResult(CommandResult.Success(new AddC4ViewCommand(
            command.BodyPath,
            command.RegistrationPath,
            MimeTypeFor(kind),
            kind,
            command.ViewKey,
            Path.GetFileName(command.BodyPath))));
    }

    private static string MimeTypeFor(C4ViewKind kind) => kind switch
    {
        C4ViewKind.SystemLandscape => "c4/system-landscape",
        C4ViewKind.SystemContext => "c4/context",
        C4ViewKind.Container => "c4/container",
        C4ViewKind.Component => "c4/component",
        C4ViewKind.Dynamic => "c4/dynamic",
        C4ViewKind.Deployment => "c4/deployment",
        _ => "c4/context",
    };
}

/// <summary>Writing and removing a view block, which both the add and its inverse need.</summary>
internal static class C4ViewBlock
{
    /// <summary>Convenience for a parsed view, whose kind is already known.</summary>
    public static C4ViewKind ViewKindOrDefault(this C4View view) => view.Kind;

    /// <summary>The line the closing brace of the views block sits on - where a new view goes above.</summary>
    public static uint? EndOfViewsBlock(C4Document document)
    {
        var depth = 0;
        var inViews = false;
        foreach (var line in document.CodeLines)
        {
            var code = line.Code;
            if (!inViews && code.StartsWith("views", StringComparison.OrdinalIgnoreCase) && code.EndsWith('{'))
            {
                inViews = true;
                depth = 1;
                continue;
            }

            if (!inViews)
            {
                continue;
            }

            depth += code.Count(character => character == '{') - code.Count(character => character == '}');
            if (depth <= 0)
            {
                return line.Number;
            }
        }

        return null;
    }

    /// <summary>The lines one view declaration occupies, indented as the document's views are.</summary>
    public static IReadOnlyList<string> Declare(C4ViewKind kind, string key, string? scope, C4Workspace workspace)
    {
        var body = new[] { "            include *", "            autoLayout lr" };
        var header = kind switch
        {
            C4ViewKind.SystemLandscape => $"        systemLandscape \"{key}\" {{",
            C4ViewKind.SystemContext => $"        systemContext {scope} \"{key}\" {{",
            C4ViewKind.Container => $"        container {scope} \"{key}\" {{",
            C4ViewKind.Component => $"        component {ComponentScope(workspace, scope)} \"{key}\" {{",
            C4ViewKind.Dynamic => $"        dynamic {scope} \"{key}\" {{",
            C4ViewKind.Deployment => $"        deployment {scope} \"Production\" \"{key}\" {{",
            _ => $"        systemContext {scope} \"{key}\" {{",
        };

        // A dynamic view's body is its ordered interactions, and a new one has none yet.
        return kind == C4ViewKind.Dynamic
            ? [header, "            autoLayout lr", "        }"]
            : [header, .. body, "        }"];
    }

    /// <summary>Removes a view's declaration, from its header line to its closing brace.</summary>
    public static void RemoveView(IC4DocumentStore documents, string bodyPath, string viewKey)
    {
        var workspace = documents.WorkspaceOf(bodyPath);
        var view = workspace.FindView(viewKey);
        if (view is null)
        {
            return;
        }

        var document = documents.GetOrLoad(bodyPath);
        var first = view.Line;
        var last = first;
        var depth = 0;
        foreach (var line in document.CodeLines.Where(line => line.Number >= first))
        {
            depth += line.Code.Count(character => character == '{') - line.Code.Count(character => character == '}');
            last = line.Number;
            if (depth <= 0)
            {
                break;
            }
        }

        document.RemoveLines(first, last);

        // Discarded deliberately, and this is the one place that is right: this undoes a
        // view whose registration could not be written, and the caller is already returning
        // that failure. A second sentence about the undo would replace the one that says
        // what actually went wrong.
        _ = documents.Save(bodyPath, document);
    }

    /// <summary>A component view is scoped to a container, so it needs one rather than a system.</summary>
    private static string ComponentScope(C4Workspace workspace, string? fallback) =>
        workspace.Elements.FirstOrDefault(element => element.Kind == C4ElementKind.Container)?.Id ?? fallback ?? "";
}
