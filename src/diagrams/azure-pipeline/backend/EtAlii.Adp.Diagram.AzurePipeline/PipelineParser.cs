using Serilog;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Reads a <see cref="PipelineDocument"/> into a <see cref="PipelineModel"/>, recording for every
/// element the lines that declare it.
/// </summary>
/// <remarks>
/// <para>
/// The line range is not decoration. It is the index the writer splices through, so a rename edits
/// the one line that holds the name and leaves the file otherwise untouched. Parsing and rewriting
/// therefore share one source of truth about where things are.
/// </para>
/// <para>
/// Expressions are never evaluated. A <c>${{ if }}</c> block is unwrapped so the stages inside it
/// still appear - a diagram that hides a stage because its inclusion is decided at compile time is
/// lying about the pipeline - but the expression that gates them is carried verbatim, as
/// <see cref="PipelineStage.Gate"/>, for the canvas to mark them conditional.
/// </para>
/// </remarks>
public sealed class PipelineParser
{
    private static readonly ILogger _logger = Log.ForContext<PipelineParser>();

    private static readonly Dictionary<string, PipelineStepKind> _stepKinds = new(StringComparer.Ordinal)
    {
        ["task"] = PipelineStepKind.Task,
        ["script"] = PipelineStepKind.Script,
        ["bash"] = PipelineStepKind.Bash,
        ["pwsh"] = PipelineStepKind.Pwsh,
        ["powershell"] = PipelineStepKind.PowerShell,
        ["checkout"] = PipelineStepKind.Checkout,
        ["download"] = PipelineStepKind.Download,
        ["publish"] = PipelineStepKind.Publish,
        ["template"] = PipelineStepKind.Template,
    };

    private static readonly Dictionary<string, PipelineStrategyKind> _strategyKinds = new(StringComparer.Ordinal)
    {
        ["runOnce"] = PipelineStrategyKind.RunOnce,
        ["rolling"] = PipelineStrategyKind.Rolling,
        ["canary"] = PipelineStrategyKind.Canary,
        ["matrix"] = PipelineStrategyKind.Matrix,
        ["parallel"] = PipelineStrategyKind.Parallel,
    };

    private readonly PipelineDocument _document;
    private readonly List<PipelineTemplateReference> _templates = [];
    private PipelinePool _pipelinePool = PipelinePool.None;

    private PipelineParser(PipelineDocument document)
    {
        _document = document;
    }

    /// <summary>
    /// Reads <paramref name="document"/> into a model.
    /// </summary>
    /// <exception cref="YamlDotNet.Core.YamlException">
    /// The document is not YAML this can read. It carries the line, which is what a reader needs
    /// in order to go and fix it (Requirement 3.6).
    /// </exception>
    public static PipelineModel Parse(PipelineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new PipelineParser(document).Read();
    }

    private PipelineModel Read()
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(_document.Text));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            _logger.Debug("A pipeline document holds no mapping at its root, so it declares nothing to draw");
            return PipelineModel.Empty;
        }

        var extends = ReadExtends(root);
        var stages = ReadPipelineStages(root);
        return new PipelineModel(stages, _templates, extends);
    }

    private PipelineTemplateReference? ReadExtends(YamlMappingNode root)
    {
        if (Entry(root, "extends") is not YamlMappingNode extends)
        {
            return null;
        }

        var path = Scalar(extends, "template");
        return TemplateReference("extends", "", PipelineTemplateSlot.Extends, path, extends, Range(extends));
    }

    /// <summary>
    /// The stages, or the implicit stage the schema wraps a <c>jobs</c>-only or <c>steps</c>-only
    /// file in (Requirement 4.2). Checked in that order, because that is the order the schema
    /// itself resolves them in: a file with <c>stages</c> never also has top-level <c>jobs</c>.
    /// </summary>
    private List<PipelineStage> ReadPipelineStages(YamlMappingNode root)
    {
        // The pipeline's own pool is read first: it is what a stage inherits, and what a stage's
        // jobs inherit through the stage (Requirement 4.7).
        _pipelinePool = ReadPool(root, PipelinePoolOrigin.Pipeline);

        if (Entry(root, "stages") is YamlSequenceNode stages)
        {
            var read = new List<PipelineStage>();
            ReadStages(stages, "", read);
            return read;
        }

        if (Entry(root, "jobs") is YamlSequenceNode jobs)
        {
            return [ImplicitStage(ReadJobsInto(jobs, ImplicitStageId, _pipelinePool), Range(jobs), _pipelinePool)];
        }

        if (Entry(root, "steps") is YamlSequenceNode steps)
        {
            var job = ImplicitJob(ReadSteps(steps, ImplicitJobId, ""), Range(steps), _pipelinePool);
            return [ImplicitStage([job], Range(steps), _pipelinePool)];
        }

        return [];
    }

    private const string ImplicitStageId = "stage-0";
    private const string ImplicitJobId = "stage-0/job-0";

    private static PipelineStage ImplicitStage(
        IReadOnlyList<PipelineJob> jobs,
        PipelineLineRange lines,
        PipelinePool pool) =>
        new(
            ImplicitStageId,
            "",
            "",
            pool,
            PipelineExecution.Default,
            TriggerIsManual: false,
            "",
            [],
            DependsOnDeclared: false,
            "",
            IsImplicit: true,
            jobs,
            lines);

    private static PipelineJob ImplicitJob(
        IReadOnlyList<PipelineStep> steps,
        PipelineLineRange lines,
        PipelinePool pool) =>
        new(
            ImplicitJobId,
            "",
            "",
            IsDeployment: false,
            "",
            PipelineStrategy.None,
            pool,
            PipelineExecution.Default,
            [],
            DependsOnDeclared: false,
            "",
            IsImplicit: true,
            steps,
            lines);

    private void ReadStages(YamlSequenceNode sequence, string gate, List<PipelineStage> into)
    {
        foreach (var item in sequence.Children)
        {
            if (item is not YamlMappingNode mapping)
            {
                continue;
            }

            if (Gate(mapping) is { } block)
            {
                // A ${{ if }} or ${{ each }} block wrapping a list of stages. Its contents are
                // stages like any others; what differs is that whether they exist is decided at
                // compile time, which the gate records without this having to decide it.
                ReadStages(block.Items, Combine(gate, block.Expression), into);
                continue;
            }

            if (Scalar(mapping, "template") is { Length: > 0 } template)
            {
                _templates.Add(TemplateReference(
                    $"template-{_templates.Count}", "", PipelineTemplateSlot.Stages, template, mapping, Range(mapping)));
                continue;
            }

            var name = Scalar(mapping, "stage");
            var id = name.Length > 0 ? name : $"stage-{into.Count}";
            var (dependsOn, declared) = ReadDependsOn(mapping);
            // A stage's own pool, or the pipeline's where it declares none - and the pool carries
            // which of those it was, so a reader knows which element to edit to change it.
            var pool = Inherit(ReadPool(mapping, PipelinePoolOrigin.Stage), _pipelinePool);
            var jobs = Entry(mapping, "jobs") is YamlSequenceNode jobsNode ? ReadJobsInto(jobsNode, id, pool) : [];
            into.Add(new PipelineStage(
                id,
                name,
                Scalar(mapping, "displayName"),
                pool,
                ReadExecution(mapping),
                // `trigger: manual` on a stage means it waits to be started by hand. Anything else
                // under `trigger` is a repository trigger, which is not this.
                string.Equals(Scalar(mapping, "trigger"), "manual", StringComparison.OrdinalIgnoreCase),
                Scalar(mapping, "isSkippable"),
                dependsOn,
                declared,
                gate,
                IsImplicit: false,
                jobs,
                Range(mapping)));
        }
    }

    private List<PipelineJob> ReadJobsInto(YamlSequenceNode sequence, string stageId, PipelinePool inherited)
    {
        var jobs = new List<PipelineJob>();
        ReadJobs(sequence, stageId, "", inherited, jobs);
        return jobs;
    }

    private void ReadJobs(
        YamlSequenceNode sequence,
        string stageId,
        string gate,
        PipelinePool inherited,
        List<PipelineJob> into)
    {
        foreach (var item in sequence.Children)
        {
            if (item is not YamlMappingNode mapping)
            {
                continue;
            }

            if (Gate(mapping) is { } block)
            {
                ReadJobs(block.Items, stageId, Combine(gate, block.Expression), inherited, into);
                continue;
            }

            if (Scalar(mapping, "template") is { Length: > 0 } template)
            {
                _templates.Add(TemplateReference(
                    $"template-{_templates.Count}", stageId, PipelineTemplateSlot.Jobs, template, mapping, Range(mapping)));
                continue;
            }

            var deployment = Scalar(mapping, "deployment");
            var isDeployment = deployment.Length > 0;
            var name = isDeployment ? deployment : Scalar(mapping, "job");
            var id = $"{stageId}/{(name.Length > 0 ? name : $"job-{into.Count}")}";
            var strategy = ReadStrategy(mapping);
            var (dependsOn, declared) = ReadDependsOn(mapping);
            into.Add(new PipelineJob(
                id,
                name,
                Scalar(mapping, "displayName"),
                isDeployment,
                Scalar(mapping, "environment"),
                strategy,
                // The job's own pool wins over whatever it inherited (Requirement 4.7).
                Inherit(ReadPool(mapping, PipelinePoolOrigin.Job), inherited),
                ReadExecution(mapping),
                dependsOn,
                declared,
                gate,
                IsImplicit: false,
                ReadJobSteps(mapping, id, strategy),
                Range(mapping)));
        }
    }

    /// <summary>
    /// A plain job declares its steps directly. A deployment job declares them inside its
    /// strategy's lifecycle hooks instead, and which hooks exist is what the strategy decides -
    /// so the steps are gathered per hook, each step remembering which one it belongs to.
    /// </summary>
    private List<PipelineStep> ReadJobSteps(YamlMappingNode job, string jobId, PipelineStrategy strategy)
    {
        if (Entry(job, "steps") is YamlSequenceNode direct)
        {
            return ReadSteps(direct, jobId, "");
        }

        if (strategy.Kind == PipelineStrategyKind.None ||
            Entry(job, "strategy") is not YamlMappingNode strategyNode ||
            Entry(strategyNode, StrategyKeyOf(strategy.Kind)) is not YamlMappingNode hooks)
        {
            return [];
        }

        var steps = new List<PipelineStep>();
        foreach (var (key, value) in Entries(hooks))
        {
            if (value is YamlMappingNode hook && Entry(hook, "steps") is YamlSequenceNode hookSteps)
            {
                steps.AddRange(ReadSteps(hookSteps, jobId, key, steps.Count));
            }
        }

        return steps;
    }

    private static string StrategyKeyOf(PipelineStrategyKind kind) =>
        _strategyKinds.First(pair => pair.Value == kind).Key;

    private List<PipelineStep> ReadSteps(YamlSequenceNode sequence, string jobId, string hook, int offset = 0)
    {
        var steps = new List<PipelineStep>();
        foreach (var item in sequence.Children)
        {
            if (item is not YamlMappingNode mapping)
            {
                continue;
            }

            var (kind, identifier) = ReadStepKind(mapping);
            steps.Add(new PipelineStep(
                $"{jobId}/step-{offset + steps.Count}",
                kind,
                Scalar(mapping, "displayName"),
                identifier,
                ReadExecution(mapping),
                hook,
                Range(mapping)));
        }

        return steps;
    }

    /// <summary>
    /// A step's kind is the key it leads with, so the mapping is read in written order and the
    /// first recognised key wins - rather than probing for each kind in an order of our own, which
    /// would mis-read a step that happens to carry <c>script</c> as an input to a task.
    /// </summary>
    private static (PipelineStepKind Kind, string Identifier) ReadStepKind(YamlMappingNode step)
    {
        foreach (var (key, value) in Entries(step))
        {
            if (_stepKinds.TryGetValue(key, out var kind))
            {
                return (kind, value is YamlScalarNode scalar ? scalar.Value ?? "" : "");
            }
        }

        return (PipelineStepKind.Unknown, "");
    }

    /// <summary>
    /// The <c>strategy</c> block, and with it how many jobs one declaration becomes
    /// (Requirement 4.6).
    /// </summary>
    private PipelineStrategy ReadStrategy(YamlMappingNode job)
    {
        if (Entry(job, "strategy") is not YamlMappingNode strategy)
        {
            return PipelineStrategy.None;
        }

        foreach (var (key, value) in Entries(strategy))
        {
            if (!_strategyKinds.TryGetValue(key, out var kind))
            {
                continue;
            }

            var (multiplicity, expression) = ReadMultiplicity(kind, value);
            return new PipelineStrategy(kind, multiplicity, expression, Range(strategy));
        }

        return PipelineStrategy.None;
    }

    /// <summary>
    /// How many copies of a job a strategy produces.
    /// </summary>
    /// <remarks>
    /// A matrix written out in the file has as many entries as it has keys. A matrix generated by
    /// an expression, or a <c>parallel</c> given as a variable, has a count nobody knows until the
    /// run starts - so the expression is carried instead of a guess. A deployment strategy
    /// multiplies nothing: it decides which hooks run, not how many jobs there are.
    /// </remarks>
    private static (int Multiplicity, string Expression) ReadMultiplicity(PipelineStrategyKind kind, YamlNode value) =>
        (kind, value) switch
        {
            (PipelineStrategyKind.Matrix, YamlMappingNode matrix) => (Math.Max(matrix.Children.Count, 1), ""),
            (PipelineStrategyKind.Matrix, YamlScalarNode { Value: { Length: > 0 } generated }) => (1, generated),
            (PipelineStrategyKind.Parallel, YamlScalarNode { Value: { Length: > 0 } count }) =>
                int.TryParse(count, out var parsed) ? (Math.Max(parsed, 1), "") : (1, count),
            _ => (1, ""),
        };

    /// <summary>
    /// The properties deciding whether and how an element runs. Every one of them is read as text,
    /// because every one of them may be an expression this module will not evaluate.
    /// </summary>
    private static PipelineExecution ReadExecution(YamlMappingNode element)
    {
        var condition = Scalar(element, "condition");
        var continueOnError = Scalar(element, "continueOnError");
        var enabled = Scalar(element, "enabled");
        var timeout = Scalar(element, "timeoutInMinutes");
        return condition.Length == 0 && continueOnError.Length == 0 && enabled.Length == 0 && timeout.Length == 0
            ? PipelineExecution.Default
            : new PipelineExecution(condition, continueOnError, enabled, timeout);
    }

    /// <summary>
    /// A <c>pool</c> as declared at one level, in either of the two shapes the schema allows: a
    /// bare name, or a mapping of <c>name</c>, <c>vmImage</c> and <c>demands</c>.
    /// </summary>
    private PipelinePool ReadPool(YamlMappingNode element, PipelinePoolOrigin origin) => Entry(element, "pool") switch
    {
        YamlScalarNode { Value: { Length: > 0 } name } scalar => new PipelinePool(name, "", [], origin, Range(scalar)),
        YamlMappingNode pool => new PipelinePool(
            Scalar(pool, "name"),
            Scalar(pool, "vmImage"),
            Entry(pool, "demands") is YamlSequenceNode demands
                ? demands.Children.OfType<YamlScalarNode>().Select(demand => demand.Value ?? "").ToList()
                : [],
            origin,
            Range(pool)),
        _ => PipelinePool.None,
    };

    /// <summary>
    /// The nearer of two pools: an element's own declaration if it made one, and otherwise
    /// whatever it inherited. The winner keeps the origin it was declared with, so "ubuntu-latest,
    /// from the pipeline" stays distinguishable from "ubuntu-latest, from this job".
    /// </summary>
    private static PipelinePool Inherit(PipelinePool own, PipelinePool inherited) => own.IsDeclared ? own : inherited;

    /// <summary>
    /// <c>dependsOn</c> takes either one name or a list of them. The flag says whether the key was
    /// there at all, because "no dependsOn" and "<c>dependsOn: []</c>" are different instructions
    /// and only one of them means "wait for nothing".
    /// </summary>
    private static (IReadOnlyList<string> Names, bool Declared) ReadDependsOn(YamlMappingNode element)
    {
        var node = Entry(element, "dependsOn");
        return node switch
        {
            null => ([], false),
            YamlScalarNode { Value: { Length: > 0 } single } => ([single], true),
            YamlSequenceNode sequence =>
            (
                sequence.Children.OfType<YamlScalarNode>()
                    .Select(child => child.Value ?? "")
                    .Where(value => value.Length > 0)
                    .ToList(),
                true
            ),
            _ => ([], true),
        };
    }

    private PipelineTemplateReference TemplateReference(
        string id,
        string ownerId,
        PipelineTemplateSlot slot,
        string reference,
        YamlMappingNode declaration,
        PipelineLineRange lines)
    {
        // "path@resource" names a template in another repository, which is the commonest reason
        // one cannot be followed - so the two halves are separated here rather than at resolution.
        var at = reference.LastIndexOf('@');
        var path = at >= 0 ? reference[..at] : reference;
        var resource = at >= 0 ? reference[(at + 1)..] : "";
        var parameters = Entry(declaration, "parameters") is YamlMappingNode supplied
            ? Entries(supplied).Select(entry => entry.Key).ToList()
            : [];
        return new PipelineTemplateReference(id, ownerId, slot, path, resource, parameters, lines);
    }

    /// <summary>
    /// A sequence item that is nothing but a <c>${{ ... }}</c> key over a list: the compile-time
    /// conditional and loop forms. Returns the expression and the list it wraps.
    /// </summary>
    private static (string Expression, YamlSequenceNode Items)? Gate(YamlMappingNode mapping)
    {
        var entries = Entries(mapping).ToList();
        return entries.Count == 1 &&
            entries[0].Key.StartsWith("${{", StringComparison.Ordinal) &&
            entries[0].Value is YamlSequenceNode items
            ? (entries[0].Key, items)
            : null;
    }

    private static string Combine(string outer, string inner) =>
        outer.Length == 0 ? inner : $"{outer} and {inner}";

    /// <summary>
    /// The entries of a mapping in written order, with any YAML merge key expanded in place.
    /// </summary>
    /// <remarks>
    /// <c>&lt;&lt;: *defaults</c> is how a pipeline shares a <c>pool</c> across stages, and it is a
    /// plain key as far as the representation model is concerned. Left unexpanded it would both
    /// hide the merged-in values and show up as a property named <c>&lt;&lt;</c>. Merged entries
    /// come after the mapping's own, since the mapping's own win.
    /// </remarks>
    private static IEnumerable<KeyValuePair<string, YamlNode>> Entries(YamlMappingNode mapping)
    {
        List<KeyValuePair<string, YamlNode>>? merged = null;
        foreach (var (key, value) in mapping.Children)
        {
            if (key is not YamlScalarNode { Value: { } name })
            {
                continue;
            }

            if (name != "<<")
            {
                yield return new KeyValuePair<string, YamlNode>(name, value);
                continue;
            }

            // A merge key takes either one mapping or a list of them, nearest-first.
            merged ??= [];
            IEnumerable<YamlNode> sources = value is YamlSequenceNode sequence ? sequence.Children : [value];
            foreach (var source in sources)
            {
                if (source is YamlMappingNode from)
                {
                    merged.AddRange(Entries(from));
                }
            }
        }

        if (merged is null)
        {
            yield break;
        }

        var own = mapping.Children.Keys.OfType<YamlScalarNode>().Select(key => key.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in merged.Where(entry => !own.Contains(entry.Key)))
        {
            yield return entry;
        }
    }

    private static YamlNode? Entry(YamlMappingNode mapping, string key) =>
        Entries(mapping).FirstOrDefault(entry => entry.Key == key).Value;

    private static string Scalar(YamlMappingNode mapping, string key) =>
        Entry(mapping, key) is YamlScalarNode { Value: { } value } ? value : "";

    /// <summary>
    /// The lines a node occupies, as a range into the document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// YamlDotNet counts lines from one, and leaves a block collection's own end mark empty - so a
    /// stage read from its own mark alone would span one line, and a rename would be the only edit
    /// that ever worked. The end therefore comes from the furthest end mark among the node's
    /// descendants, which for a block collection is the last scalar it contains.
    /// </para>
    /// <para>
    /// That mark points at where scanning stopped, which for a block scalar is the start of the
    /// next line - so a column-one mark is pulled back one, and then back past any blank or comment
    /// lines it swept up. Without that a stage's range would take in the comment introducing the
    /// stage after it, and a splice would eat it.
    /// </para>
    /// </remarks>
    private PipelineLineRange Range(YamlNode node)
    {
        var last = _document.Lines.Count - 1;
        var extent = EndMark(node);
        // YamlDotNet counts in long; a document with more lines than an int holds is not a thing.
        var start = Math.Clamp((int)node.Start.Line - 1, 0, last);
        var end = Math.Clamp((int)extent.Line - 1, start, last);
        if (extent.Column == 1 && end > start)
        {
            end--;
        }

        while (end > start && (_document.Lines[end].IsBlank || _document.Lines[end].IsComment))
        {
            end--;
        }

        return new PipelineLineRange(start, end);
    }

    /// <summary>
    /// The furthest end mark in a node's subtree.
    /// </summary>
    /// <remarks>
    /// An alias resolves to the node it points at, which YAML requires to have been declared
    /// earlier in the file - so following one can only ever look backwards, and never stretches a
    /// range past where the element actually ends.
    /// </remarks>
    private static Mark EndMark(YamlNode node)
    {
        var end = node.End;
        foreach (var child in Descend(node))
        {
            var childEnd = EndMark(child);
            if (childEnd.Line > end.Line)
            {
                end = childEnd;
            }
        }

        return end;
    }

    private static IEnumerable<YamlNode> Descend(YamlNode node) => node switch
    {
        YamlMappingNode mapping => mapping.Children.Keys.Concat(mapping.Children.Values),
        YamlSequenceNode sequence => sequence.Children,
        _ => [],
    };
}
