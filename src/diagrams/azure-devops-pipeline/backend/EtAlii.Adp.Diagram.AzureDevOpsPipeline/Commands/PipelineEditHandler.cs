using System.Diagnostics.CodeAnalysis;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// A handler of a command that edits a pipeline: the shared edit run, over the store seen from the
/// command's root (code-structure FR-033). Each command says what it does to a body.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not the shared <see cref="DocumentEditHandler{TCommand, TStore, TBody, TModel}"/>, because a
/// pipeline's store answers only once it knows the workspace root, and the root comes with each
/// command rather than once with the handler.</b> So the run is the shared one, and the store it is
/// given is <see cref="IPipelineDocumentStore.In"/> the command's root.
/// </para>
/// <para>
/// <b>An edit that names no undo is undone by a restore of the whole text through the module's own
/// store</b>, as the run's would be had it the store. The run's own would restore through the store
/// seen from one root, which no handler serves. Most edits name theirs, which outlive a later write
/// of the file where a restore of the old text does not.
/// </para>
/// </remarks>
/// <typeparam name="TCommand">The command handled.</typeparam>
internal abstract class PipelineEditHandler<TCommand>(IPipelineDocumentStore documents) : ICommandHandler<TCommand>
    where TCommand : IPipelineEditCommand
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(DocumentEditRun.Run<IDiagramDocumentStore<LineDocument, PipelineModel>, LineDocument, PipelineModel>(
            documents.In(command.RootPath),
            command.BodyPath,
            command,
            body => ModelOf(command, body),
            (body, model) => Run(body, model, command)));
    }

    /// <summary>Applies <paramref name="command"/>, with the module's restore as the undo where it names none.</summary>
    private EditOutcome Run(LineDocument body, PipelineModel model, TCommand command)
    {
        var before = body.Text;
        var outcome = Apply(body, model, command);
        return outcome is { WasApplied: true, IsUnchanged: false, Inverse: null }
            ? EditOutcome.UndoneBy(new RestoreDocumentCommand<IPipelineDocumentStore>(command.BodyPath, before, command))
            : outcome;
    }

    /// <summary>Changes <paramref name="body"/>, whose model is <paramref name="model"/>, as <paramref name="command"/> asks, or says why not.</summary>
    protected abstract EditOutcome Apply(LineDocument body, PipelineModel model, TCommand command);

    /// <summary>The model of <paramref name="body"/>, read as the store reads the document <paramref name="command"/> edits.</summary>
    protected PipelineModel ModelOf(TCommand command, LineDocument body) =>
        documents.ModelOf(command.RootPath, command.BodyPath, body);

    /// <summary>
    /// Whether an element found at <paramref name="location"/> may not be edited here, and if so the
    /// refusal that says why: it is gone, or it is written in a template.
    /// </summary>
    protected static bool Refuses([NotNullWhen(false)] PipelineElementLocation? location, out EditOutcome refusal)
    {
        refusal = location is null
            ? EditOutcome.Refused("That element is no longer in this pipeline.")
            : PipelineEdits.RefusalFor(location) is { Length: > 0 } reason
            ? EditOutcome.Refused(reason)
            : default;
        return location is null || refusal.Refusal is not null;
    }
}
