using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>Adds a library entry to a pipeline (Requirement 11.2).</summary>
/// <param name="BodyPath">The pipeline's file - settings JSON or a bundle's YAML.</param>
/// <param name="PipelineKey">Which pipeline; empty means the file's first.</param>
/// <param name="Kind">notebook, file or glob.</param>
/// <param name="Path">The path or include pattern.</param>
public sealed record InsertDatabricksLibraryCommand(
    string BodyPath, string PipelineKey, string Kind, string Path) : IDocumentEditCommand;

/// <summary>Removes one library entry; the last one is refused - the schema requires it.</summary>
public sealed record RemoveDatabricksLibraryCommand(
    string BodyPath, string PipelineKey, string Kind, string Path) : IDocumentEditCommand;

/// <summary>Rewrites one of the pipeline's own scalars - name, catalog, schema, channel.</summary>
public sealed record SetDatabricksPipelineScalarCommand(
    string BodyPath, string PipelineKey, string Key, string Value) : IDocumentEditCommand;

/// <inheritdoc cref="InsertDatabricksLibraryCommand" />
public sealed class InsertDatabricksLibraryCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<InsertDatabricksLibraryCommand>(documents)
{
    /// <inheritdoc />
    /// <remarks>
    /// The library is added to the pipeline the edit addresses, by the binding's rule for its kind in that pipeline's
    /// form: a nested mapping in a bundle's YAML, one item on its own line in a settings file's array (FBL 0.10 section
    /// 6.2, gap 6).
    /// </remarks>
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, InsertDatabricksLibraryCommand command)
    {
        if (PipelineOf(model, command.PipelineKey) is not { } pipeline)
        {
            return EditOutcome.Refused("There is no pipeline in this file any more.");
        }

        // The kinds the binding has a rule to add, checked first as they always were, before the path's own refusal.
        if (command.Kind is not ("notebook" or "file" or "glob"))
        {
            return EditOutcome.Refused($"There is no '{command.Kind}' library kind to add.");
        }

        if (pipeline.Libraries.Any(library => library.Kind == command.Kind && library.Path == command.Path) && command.Path.Length > 0)
        {
            return EditOutcome.Refused($"The {command.Kind} library '{command.Path}' is already there.");
        }

        return DatabricksEdits.Pipeline(
            body,
            pipeline,
            reading => new ModelChange.Add("Library", null, DatabricksEdits.Attributes(("libraryKind", command.Kind), ("path", command.Path)), DatabricksEdits.OneOf(reading, "Pipeline")));
    }
}

/// <inheritdoc cref="RemoveDatabricksLibraryCommand" />
public sealed class RemoveDatabricksLibraryCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<RemoveDatabricksLibraryCommand>(documents)
{
    /// <inheritdoc />
    /// <remarks>The last library is refused rather than removed: a pipeline with no libraries has nothing to run, and the published schema requires one.</remarks>
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, RemoveDatabricksLibraryCommand command)
    {
        if (PipelineOf(model, command.PipelineKey) is not { } pipeline)
        {
            return EditOutcome.Refused("There is no pipeline in this file any more.");
        }

        if (pipeline.Libraries.All(library => library.Kind != command.Kind || library.Path != command.Path))
        {
            return EditOutcome.Refused($"There is no {command.Kind} library '{command.Path}' any more.");
        }

        return pipeline.Libraries.Count == 1
            ? EditOutcome.Refused("A pipeline needs at least one library; removing the last one is not allowed.")
            : DatabricksEdits.Pipeline(body, pipeline, reading => new ModelChange.Remove(DatabricksEdits.LibraryId(reading, command.Kind, command.Path)));
    }
}

/// <inheritdoc cref="SetDatabricksPipelineScalarCommand" />
public sealed class SetDatabricksPipelineScalarCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<SetDatabricksPipelineScalarCommand>(documents)
{
    /// <inheritdoc />
    /// <remarks>
    /// The value is written in the file's own syntax by the binding's attribute of that name: a schema written as the
    /// legacy <c>target</c> is written to <c>schema</c>, the <c>target</c> removed in the same edit (FBL 0.10 section 5.2,
    /// gap 7), and a settings file that does not have the key refuses it.
    /// </remarks>
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, SetDatabricksPipelineScalarCommand command) =>
        PipelineOf(model, command.PipelineKey) is { } pipeline
            ? DatabricksEdits.Pipeline(body, pipeline, reading => new ModelChange.Set(DatabricksEdits.OneOf(reading, "Pipeline")!, DatabricksEdits.Attributes((command.Key, command.Value))))
            : EditOutcome.Refused("There is no pipeline in this file any more.");
}
