using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// How every Databricks command writes: it opens the body, makes one model change in the reading it edits, has
/// <c>EtAlii.Adp.Specification.Fbl</c> plan it into splices through the module's binding and stores the text that gives
/// (databricks-disl-fbl Requirement 6). No module code builds a line of YAML or JSON.
/// </summary>
/// <remarks>
/// <para>
/// <b>A command still refuses in its own words, before the plan</b> (Requirement 6.7): the checks of the handlers read
/// the model the binding reads, so a refusal never depends on how the file is written. Where the library refuses with a
/// sentence of its own that the binding cannot give, <see cref="Sentence"/> gives today's, as module code standing in for
/// FBL gap 5: a sentence that names the value it refuses, and the ones the library builds itself for a list written on
/// one line.
/// </para>
/// <para>
/// <b>The store keeps the body as lines</b> (<see cref="LineDocument"/>), so the planned text is stored by replacing
/// the lines it changed and no other, and is checked to be the planned text byte for byte before it is kept.
/// </para>
/// </remarks>
internal static class DatabricksEdits
{
    /// <summary>The refusal of an edit the line store could not keep as the library planned it; never expected.</summary>
    internal const string NotStored = "This edit could not be written as it was planned, so nothing was written.";

    /// <summary>The binding's sentence for a task type that no rule of the binding adds (<c>databricks.fbl</c>).</summary>
    private const string UnknownTaskType = "A task of that type cannot be added to a job.";

    /// <summary>The binding's sentence for a library kind that no rule of the binding adds (<c>databricks.fbl</c>).</summary>
    private const string UnknownLibraryKind = "A library of that kind cannot be added to a pipeline.";

    /// <summary>The sentence ruled on 2026-10-19 for an edit of a list written on one line that cannot be made.</summary>
    internal const string OneLineListRefusal = "This list is written on one line, which this diagram cannot change yet; edit it in the text editor.";

    /// <summary>Makes <paramref name="change"/> in the reading of the job <paramref name="job"/> and stores the result in <paramref name="document"/>.</summary>
    public static EditOutcome Job(LineDocument document, JobModel job, Func<FblModel, ModelChange> change, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        return Write(document, DatabricksBody.JobOrigin, Resource(job.Key), change, value);
    }

    /// <summary>Makes <paramref name="change"/> in the reading of the pipeline <paramref name="pipeline"/> and stores the result in <paramref name="document"/>.</summary>
    public static EditOutcome Pipeline(LineDocument document, PipelineModel pipeline, Func<FblModel, ModelChange> change, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        return Write(document, DatabricksBody.PipelineOrigin, Resource(pipeline.Key), change, value);
    }

    /// <summary>Makes <paramref name="change"/> in the bundle's reading and stores the result in <paramref name="document"/>.</summary>
    public static EditOutcome Bundle(LineDocument document, Func<FblModel, ModelChange> change, string? value = null) =>
        Write(document, DatabricksBody.BundleOrigin, null, change, value);

    /// <summary>The id of the task keyed <paramref name="key"/>: the first of that key, as the module has always found it.</summary>
    public static string TaskId(FblModel reading, string key) =>
        reading.Elements.First(element => element.Type == "Task" && Text(element, "key") == key).Id;

    /// <summary>The id of the dependency of the task keyed <paramref name="taskKey"/> on <paramref name="upstreamKey"/>.</summary>
    public static string DependencyId(FblModel reading, string taskKey, string upstreamKey)
    {
        var task = TaskId(reading, taskKey);
        return reading.Elements.First(element => element.Type == "Dependency" && element.ParentId == task && Text(element, "upstreamKey") == upstreamKey).Id;
    }

    /// <summary>The id of the library of <paramref name="kind"/> at <paramref name="path"/>.</summary>
    public static string LibraryId(FblModel reading, string kind, string path) =>
        reading.Elements.First(element => element.Type == "Library" && Text(element, "libraryKind") == kind && Text(element, "path") == path).Id;

    /// <summary>The id of the reading's one element of <paramref name="type"/>: its pipeline, or its bundle.</summary>
    public static string? OneOf(FblModel reading, string type) =>
        reading.Elements.FirstOrDefault(element => element.Type == type)?.Id;

    /// <summary>The attributes of a change, by name.</summary>
    public static Dictionary<string, object?> Attributes(params (string Name, object? Value)[] attributes) =>
        attributes.ToDictionary(attribute => attribute.Name, attribute => attribute.Value, StringComparer.Ordinal);

    private static string? Resource(string key) => key.Length > 0 ? key : null;

    private static EditOutcome Write(LineDocument document, string origin, string? resource, Func<FblModel, ModelChange> change, string? value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(change);

        var body = DatabricksBody.Open(document.Text);
        var outcome = body.Change(change(body.Reading(origin, resource)), origin, resource);
        return outcome.WasApplied ? Store(document, body.Text) : EditOutcome.Refused(Sentence(outcome.Refusal!, value));
    }

    /// <summary>
    /// Today's sentence for a refusal the library words itself (FBL gap 5, finding B19): the value refused named, and the
    /// sentence ruled for a list written on one line; every other sentence is the binding's, or the library's.
    /// </summary>
    private static string Sentence(string refusal, string? value) => refusal switch
    {
        UnknownTaskType => $"There is no '{value}' task type to add.",
        UnknownLibraryKind => $"There is no '{value}' library kind to add.",
        _ when refusal.Contains("one-line", StringComparison.Ordinal) || refusal.Contains("on one line", StringComparison.Ordinal) => OneLineListRefusal,
        _ => refusal,
    };

    /// <summary>Stores <paramref name="text"/> in <paramref name="document"/> by replacing the lines that differ, and no other.</summary>
    private static EditOutcome Store(LineDocument document, string text)
    {
        var before = document.Lines;
        var after = LineDocument.Parse(text).Lines;
        var start = 0;
        while (start < before.Count && start < after.Count && before[start] == after[start]) start++;

        var lastBefore = before.Count - 1;
        var lastAfter = after.Count - 1;
        while (lastBefore >= start && lastAfter >= start && before[lastBefore] == after[lastAfter])
        {
            lastBefore--;
            lastAfter--;
        }

        // A change the library plans with no splice is still an edit, as the writers' were: it is saved and undone as one.
        if (lastBefore < start && lastAfter < start) return EditOutcome.Applied;

        var replacement = after.Skip(start).Take(lastAfter - start + 1).Select(line => line.Text).ToList();
        if (lastBefore < start) document.Insert(start, replacement);
        else if (replacement.Count == 0) document.Remove(new LineRange(start, lastBefore));
        else document.Replace(new LineRange(start, lastBefore), replacement);

        return string.Equals(document.Text, text, StringComparison.Ordinal) ? EditOutcome.Applied : EditOutcome.Refused(NotStored);
    }

    private static string Text(FblElement element, string attribute) =>
        element.Attributes.GetValueOrDefault(attribute) switch
        {
            null => "",
            string text => text,
            var other => Convert.ToString(other, System.Globalization.CultureInfo.InvariantCulture) ?? "",
        };
}
