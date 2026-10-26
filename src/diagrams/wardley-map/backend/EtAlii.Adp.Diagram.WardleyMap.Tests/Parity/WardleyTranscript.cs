using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;

/// <summary>
/// The Wardley map's parity transcript: what today's hand-written code answers for every document
/// of the corpus, frozen as <c>Parity/wardley-map.transcript.json</c> before any provider, the
/// parser or the writer is changed (wardley-map-disl-fbl, Requirement 1). Every later step must
/// reproduce it byte for byte, except where the specification names a change.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b>: the test project's <c>Fixtures/*.owm</c> (the thirteen it had and one for
/// each finding marked <i>read</i> that is about what the tool reads, writes or answers), every
/// <c>.owm</c> under the module's <c>examples/</c> and every one under the shipped copies in
/// <c>src/examples/diagrams/wardley-map/</c>. An example's <c>{base}.identities.json</c> is copied
/// beside it, because the ids it stores are part of what the tool answers. A document whose bytes,
/// and whose identities file, equal one recorded earlier is written as <c>sameAs</c> that one.
/// </para>
/// <para>
/// <b>Where a document is opened</b>: at <c>maps/&lt;name&gt;</c> in a scratch project whose root holds
/// <c>at-root.owm</c> and whose <c>maps/</c> folder holds <c>beside.owm</c>, so that the submap rule
/// shows which folder it resolves an address against (finding A5).
/// </para>
/// <para>
/// <b>Per document</b>: the identities the store reconciles; the context menu of every element id,
/// the empty canvas and an unknown id; the property rows of every element id, with the rows of the
/// same map in a read-only file; the findings; the payload of every mapped element; and a scripted
/// edit sequence that runs every command once, each step's answer, the hunks it made to the body and
/// the changes to the identities after it, then undoes all of it. Four documents add steps of their
/// own, for the findings they exist to show (B9, B10, B11 and B13).
/// </para>
/// <para>
/// <b>Only the new ids are masked.</b> An id the store mints is random, so each one is replaced by
/// <c>new-N</c>, numbered within its document in the order the identities first list it. An id read
/// from an identities file is written as it is. Nothing else is masked.
/// </para>
/// </remarks>
public sealed partial class WardleyTranscript() : TranscriptAdapter("wardley-map", "EtAlii.Adp.Diagram.WardleyMap.Tests", "wardley-map.transcript.json")
{
    private const string Unknown = "parity:unknown";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(WardleyMapReflection.Descriptor);

    public override string Origin => MapOrigin.Key;

    private static ToolOrigin MapOrigin => Diagram.WardleyMap.Origin;

    protected override IReadOnlyList<ToolboxItemDefinition> Toolbox => WardleyDefinition.Toolbox;

    /// <summary>The corpus, in the order it is recorded.</summary>
    public override IReadOnlyList<TranscriptDocument> Corpus() => [.. Files().Select(OnDisk)];

    /// <summary>The corpus, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    private IReadOnlyList<string> Files()
    {
        var fixtures = Directory.GetFiles(IoPath.Combine(ModuleFolder, "backend", TestProject, "Fixtures"), "*.owm");
        var examples = Directory.GetFiles(IoPath.Combine(ModuleFolder, "examples"), "*.owm", SearchOption.AllDirectories);
        var shipped = Directory.GetFiles(IoPath.Combine(SourceFolder, "examples", "diagrams", "wardley-map"), "*.owm", SearchOption.AllDirectories);
        return [.. Relative(fixtures), .. Relative(examples), .. Relative(shipped)];
    }

    /// <summary>The identities file beside a document of the corpus, or null.</summary>
    private string? SidecarOf(TranscriptDocument document)
    {
        var path = WardleyIdentities.PathFor(IoPath.Combine(SourceFolder, document.Name));
        return File.Exists(path) ? path : null;
    }

    /// <inheritdoc />
    /// <remarks>The bytes, and those of the identities file beside them: equal bodies with other stored ids are other readings.</remarks>
    public override async Task<string?> WriteIdentityAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(document);
        var sha = TranscriptText.Sha256(bytes);
        json.WriteString("sha256", sha);
        if (SidecarOf(document) is not { } sidecar)
        {
            return sha;
        }

        var identities = TranscriptText.Sha256(await File.ReadAllBytesAsync(sidecar, cancellationToken));
        json.WriteString("identitiesSha256", identities);
        return $"{sha}+{identities}";
    }

    /// <inheritdoc />
    public override async Task WriteDocumentAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, string folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(document);

        var maps = IoPath.Combine(folder, "maps");
        Directory.CreateDirectory(maps);
        await File.WriteAllTextAsync(IoPath.Combine(folder, "at-root.owm"), "title At the root\n", cancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(maps, "beside.owm"), "title Beside the map\n", cancellationToken);

        var body = IoPath.Combine(maps, IoPath.GetFileName(document.Name));
        await File.WriteAllBytesAsync(body, bytes, cancellationToken);
        var recorded = new HashSet<string>(StringComparer.Ordinal);
        if (SidecarOf(document) is { } sidecar)
        {
            File.Copy(sidecar, WardleyIdentities.PathFor(body));
            foreach (var entry in new WardleyIdentities(new FileWriter()).Read(body))
            {
                recorded.Add(entry.Id);
            }
        }

        using var session = new Session(folder, body, recorded);
        await session.ReadAsync(cancellationToken);
        await WriteReadingsAsync(json, session, bytes, cancellationToken);
        await WriteEditsAsync(json, session, IoPath.GetFileName(document.Name), cancellationToken);
    }

    private static async Task WriteReadingsAsync(Utf8JsonWriter json, Session session, byte[] bytes, CancellationToken cancellationToken)
    {
        var identities = session.Identities;
        json.WritePropertyName("identities");
        json.WriteLines([.. identities.Select(session.IdentityLine)]);

        List<string> targets = [.. identities.Select(entry => entry.Id).Distinct(StringComparer.Ordinal), "", Unknown];

        var menus = new List<IReadOnlyList<string>>();
        var choices = new List<IReadOnlyList<string>>();
        var lines = new List<(string Target, int Menu, IReadOnlyList<string> Rows)>();
        foreach (var target in targets)
        {
            var menu = menus.IndexOfOrAdd((await session.Actions.DiscoverAsync(session.Target(target), cancellationToken)).MenuLines());
            IReadOnlyList<string> rows = [.. (await session.Properties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => row.RowLine(choices))];
            lines.Add((target, menu, rows));
        }

        // The same rows in a file that cannot be written; the menus of such a file are empty,
        // because the action provider does not serve it.
        File.SetAttributes(session.Body, File.GetAttributes(session.Body) | FileAttributes.ReadOnly);
        var readOnly = new List<IReadOnlyList<string>>();
        var readOnlyMenus = new List<int>();
        try
        {
            foreach (var target in targets)
            {
                readOnlyMenus.Add(menus.IndexOfOrAdd((await session.Actions.DiscoverAsync(session.Target(target), cancellationToken)).MenuLines()));
                readOnly.Add([.. (await session.Properties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => row.RowLine(choices))]);
            }
        }
        finally
        {
            File.SetAttributes(session.Body, File.GetAttributes(session.Body) & ~FileAttributes.ReadOnly);
        }

        json.WriteStartArray("menus");
        foreach (var menu in menus)
        {
            json.WriteLines(menu.Select(session.Mask));
        }

        json.WriteEndArray();

        json.WriteStartArray("choices");
        foreach (var list in choices)
        {
            json.WriteLines(list.Select(session.Mask));
        }

        json.WriteEndArray();

        json.WriteStartArray("targets");
        for (var index = 0; index < lines.Count; index++)
        {
            (string target, int menu, _) = lines[index];
            json.WriteStringValue(session.Mask($"{TranscriptText.Quoted(target)} => menu #{menu}, read-only menu #{readOnlyMenus[index]}"));
        }

        json.WriteEndArray();

        json.WriteStartArray("rows");
        for (var index = 0; index < lines.Count; index++)
        {
            (string target, _, IReadOnlyList<string> rows) = lines[index];
            if (rows.Count == 0 && readOnly[index].Count == 0)
            {
                continue;
            }

            json.WriteStartObject();
            json.WriteString("target", session.Mask(target));
            json.WritePropertyName("rows");
            json.WriteLines(rows.Select(session.Mask));
            if (TranscriptText.ReadOnlyVariant(rows, readOnly[index]) is { } variant)
            {
                json.WriteString("readOnly", variant);
            }
            else
            {
                json.WritePropertyName("readOnly");
                json.WriteLines(readOnly[index].Select(session.Mask));
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();

        var problems = await new WardleyValidator(session.Store).ValidateAsync(
            new DiagramValidationRequest(TranscriptText.Decode(bytes), IoPath.GetFileNameWithoutExtension(session.Body), ProjectRoot.From(session.Folder), session.Body, null),
            cancellationToken);
        json.WritePropertyName("findings");
        json.WriteLines([.. problems.Select(problem => session.Mask($"{Location(problem.Location)} | {problem.Severity} | {problem.RuleId} | {problem.Message}"))]);

        json.WritePropertyName("payloads");
        json.WriteLines([.. session.Mapper.Visible(session.Model, session.Identities, DiagramViewport.Unbounded)
            .Select(element => session.Mask(TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes)))]);
    }

    private static string Location(DiagramProblemLocation? location) => location switch
    {
        DiagramProblemLineLocation line => $"line {line.Number}",
        DiagramProblemElementLocation element => $"element {TranscriptText.Quoted(element.Id)}",
        null => "nowhere",
        _ => location.ToString(),
    };

    /// <summary>
    /// The scripted edit sequence: drags of a component and of a pipeline component, every property
    /// row of a component, a link, a pipeline component and a pipeline with values that reach their
    /// refusals, every action of the menus with its dialog's commit, every add, the actions on an
    /// unknown id and a removal, each run through the real history on a copy of the document, then
    /// the steps of the document's own, then everything undone.
    /// </summary>
    private static async Task WriteEditsAsync(Utf8JsonWriter json, Session session, string name, CancellationToken cancellationToken)
    {
        var script = new Script(json, session, cancellationToken);
        var original = await script.StartAsync();

        var model = session.Model;
        var component = session.IdOf(WardleyIdentityKind.Component, model.Components.Select(WardleyIdentityKeys.Of).FirstOrDefault());
        var other = session.IdOf(WardleyIdentityKind.Component, model.Components.Select(WardleyIdentityKeys.Of).Skip(1).FirstOrDefault());
        var link = session.IdOf(WardleyIdentityKind.Link, model.Links.Select(WardleyIdentityKeys.Of).FirstOrDefault());
        var nested = model.Pipelines.FirstOrDefault(pipeline => pipeline is { Form: WardleyPipelineForm.Nested, Children.Count: > 0 });
        var child = nested is null ? null : session.IdOf(WardleyIdentityKind.PipelineChild, WardleyIdentityKeys.Of(nested, nested.Children[0]));
        var pipeline = session.IdOf(WardleyIdentityKind.Pipeline, model.Pipelines.Select(WardleyIdentityKeys.Of).FirstOrDefault());
        var parent = nested is null ? null : session.IdOf(WardleyIdentityKind.Component, nested.Parent);

        if (component is not null)
        {
            await script.DragAsync(component, 0.25, 0.75);
            await script.DragAsync(component, -0.5, 1.5);

            await script.SetAsync(component, WardleyContextPropertyProvider.VisibilityPropertyId, "0.5");
            await script.SetAsync(component, WardleyContextPropertyProvider.VisibilityPropertyId, "abc");
            await script.SetAsync(component, WardleyContextPropertyProvider.VisibilityPropertyId, "1.4");
            await script.SetAsync(component, WardleyContextPropertyProvider.MaturityPropertyId, "0.25");
            await script.SetAsync(component, WardleyContextPropertyProvider.EvolveNamePropertyId, "Not yet");
            await script.SetAsync(component, WardleyContextPropertyProvider.EvolvePropertyId, "0.7");
            await script.SetAsync(component, WardleyContextPropertyProvider.EvolvePropertyId, "1.5");
            await script.SetAsync(component, WardleyContextPropertyProvider.EvolveNamePropertyId, "Parity becomes");
            await script.SetAsync(component, WardleyContextPropertyProvider.EvolveNamePropertyId, "");
            await script.SetAsync(component, WardleyContextPropertyProvider.EvolvePropertyId, "");
            await script.SetAsync(component, WardleyContextPropertyProvider.InertiaPropertyId, "true");
            await script.SetAsync(component, WardleyContextPropertyProvider.InertiaPropertyId, "false");
            await script.SetAsync(component, WardleyContextPropertyProvider.DecoratorsPropertyId, "build, market");
            await script.SetAsync(component, WardleyContextPropertyProvider.DecoratorsPropertyId, "nonsense");
            await script.SetAsync(component, WardleyContextPropertyProvider.DecoratorsPropertyId, "");
            await script.SetAsync(component, WardleyContextPropertyProvider.StagePropertyId, "Product");
            await script.SetAsync(component, WardleyContextPropertyProvider.KindPropertyId, "anchor");
            await script.SetAsync(component, WardleyContextPropertyProvider.LabelOffsetPropertyId, "1, 2");
            await script.SetAsync(component, "wardley.url", "elsewhere");
            await script.SetAsync(component, "parity:property", "x");
            await script.SetAsync(component, WardleyContextPropertyProvider.NamePropertyId, " ");
            await script.SetAsync(component, WardleyContextPropertyProvider.NamePropertyId, "Parity named");
        }

        if (link is not null)
        {
            await script.SetAsync(link, WardleyContextPropertyProvider.LinkContextPropertyId, "Parity context");
            await script.SetAsync(link, WardleyContextPropertyProvider.LinkContextPropertyId, "");
            await script.SetAsync(link, WardleyContextPropertyProvider.LinkSourcePropertyId, "Somebody");
            await script.SetAsync(link, WardleyContextPropertyProvider.LinkKindPropertyId, "flow");
            await script.ExecuteAsync(link, WardleyContextActionProvider.UnlinkActionId);
        }

        if (child is not null)
        {
            await script.DragAsync(child, 0.9, 0.1);
            await script.SetAsync(child, WardleyContextPropertyProvider.MaturityPropertyId, "0.35");
            await script.SetAsync(child, WardleyContextPropertyProvider.MaturityPropertyId, "2");
            await script.SetAsync(child, WardleyContextPropertyProvider.VisibilityPropertyId, "0.5");
            await script.SetAsync(child, WardleyContextPropertyProvider.NamePropertyId, "Renamed child");
        }

        if (pipeline is not null)
        {
            await script.SetAsync(pipeline, WardleyContextPropertyProvider.PipelineChildrenPropertyId, "x");
            await script.SetAsync(pipeline, WardleyContextPropertyProvider.NamePropertyId, "x");
        }

        if (component is not null)
        {
            await script.ExecuteAsync(component, WardleyContextActionProvider.RenameActionId);
            await script.ValidateAsync(component, WardleyContextActionProvider.RenameActionId, "  ");
            await script.CommitAsync(component, WardleyContextActionProvider.RenameActionId, "Parity renamed");
            await script.ExecuteAsync(component, WardleyContextActionProvider.SetEvolveActionId);
            await script.ValidateAsync(component, WardleyContextActionProvider.SetEvolveActionId, "2");
            await script.CommitAsync(component, WardleyContextActionProvider.SetEvolveActionId, "0.65");
            await script.ExecuteAsync(component, WardleyContextActionProvider.SetEvolveActionId);
            await script.ExecuteAsync(component, WardleyContextActionProvider.ClearEvolveActionId);
            await script.ExecuteAsync(component, WardleyContextActionProvider.ClearEvolveActionId);
            await script.ExecuteAsync(component, WardleyContextActionProvider.ToggleInertiaActionId);
            await script.ExecuteAsync(component, WardleyContextActionProvider.ToggleInertiaActionId);
            foreach (var decorator in WardleyDecorators.All)
            {
                await script.ExecuteAsync(component, WardleyContextActionProvider.DecoratorActionIdFor(decorator));
            }

            await script.LinkAsync(component, other, WardleyContextActionProvider.LinkActionId);
            await script.LinkAsync(component, other, WardleyContextActionProvider.FlowActionId);
            await script.LinkAsync(component, other, WardleyContextActionProvider.LinkActionId);
            await script.UnlinkAsync(component);
            await script.ExecuteAsync(component, WardleyContextActionProvider.AddToPipelineActionId);
            await script.CommitAsync(component, WardleyContextActionProvider.AddToPipelineActionId, "Parity child");
            await script.CommitAsync(component, WardleyContextActionProvider.AddToPipelineActionId, "Parity child");
            await script.CommitAsync(component, WardleyContextActionProvider.AddToPipelineActionId, "Second child");
        }

        if (parent is not null && parent != component)
        {
            await script.CommitAsync(parent, WardleyContextActionProvider.AddToPipelineActionId, "Parity sibling");
        }

        if (child is not null)
        {
            await script.ExecuteAsync(child, WardleyContextActionProvider.RemoveFromPipelineActionId);
            await script.ExecuteAsync(child, WardleyContextActionProvider.RemoveFromPipelineActionId);
        }

        foreach (var add in new[]
        {
            WardleyContextActionProvider.AddComponentActionId,
            WardleyContextActionProvider.AddAnchorActionId,
            WardleyContextActionProvider.AddSubmapActionId,
            WardleyContextActionProvider.AddMarketActionId,
            WardleyContextActionProvider.AddEcosystemActionId,
            WardleyContextActionProvider.AddNoteActionId,
            WardleyContextActionProvider.AddAnnotationActionId,
        })
        {
            await script.ExecuteAsync("", add);
            await script.ValidateAsync("", add, " ");
            await script.CommitAsync("", add, $"Parity {add["wardley.add-".Length..]}");
        }

        await script.CommitAsync("", WardleyContextActionProvider.AddComponentActionId, "Parity component");
        await script.ExecuteAsync("", WardleyContextActionProvider.RenameActionId);

        await script.ExecuteAsync(Unknown, WardleyContextActionProvider.RenameActionId);
        await script.ExecuteAsync(Unknown, WardleyContextActionProvider.RemoveActionId);
        await script.SetAsync(Unknown, WardleyContextPropertyProvider.NamePropertyId, "Nobody");
        await script.CommitAsync(Unknown, WardleyContextActionProvider.RenameActionId, "Nobody");
        await script.DragAsync(Unknown, 0.5, 0.5);

        if (other is not null && await script.ExecuteAsync(other, WardleyContextActionProvider.RemoveActionId) is "confirm")
        {
            await script.CommitAsync(other, WardleyContextActionProvider.RemoveActionId, "");
        }

        await script.EndAsync(original);

        // The document's own steps run on the document as it was opened, once the script above is undone.
        if (await DocumentStepsAsync(new Script(json, session, cancellationToken, "documentEdits"), session, name) is { } own)
        {
            await own.EndAsync(original);
        }
    }

    /// <summary>The steps one document adds for the finding it exists to show; null when it adds none.</summary>
    private static async Task<Script?> DocumentStepsAsync(Script script, Session session, string name)
    {
        if (name is not ("tea-shop.owm" or "comments-everywhere.owm" or "pipelines-both-forms.owm" or "unclosed-pipeline.owm" or "awkward-names.owm"
            or "same-key-twice.owm" or "duplicate-names.owm" or "decorators.owm" or "lenient-trailer.owm" or "evolve-twice.owm" or "trailing-comments.owm"))
        {
            return null;
        }

        await script.StartAsync();
        switch (name)
        {
            case "tea-shop.owm":
                // B10: a rename and the ids of the links that name the element; its undo. B11: a
                // value set on a statement whose other number is written with a trailing zero.
                {
                    var kettle = session.IdOf(WardleyIdentityKind.Component, "Kettle")!;
                    await script.CommitAsync(kettle, WardleyContextActionProvider.RenameActionId, "Kettle renamed");
                    await script.UndoAsync();
                    await script.RedoAsync();
                    await script.UndoAsync();
                    var hotWater = session.IdOf(WardleyIdentityKind.Component, "Hot Water")!;
                    await script.SetAsync(hotWater, WardleyContextPropertyProvider.VisibilityPropertyId, "0.5");
                    await script.ExecuteAsync(hotWater, WardleyContextActionProvider.ToggleInertiaActionId);
                    await script.ExecuteAsync(hotWater, WardleyContextActionProvider.DecoratorActionIdFor(WardleyDecorator.Buy));
                    var powerLink = session.IdOf(WardleyIdentityKind.Link, $"Kettle{WardleyIdentityKeys.Separator}Power{WardleyIdentityKeys.Separator}{WardleyLinkKind.Dependency}")!;
                    await script.SetAsync(powerLink, WardleyContextPropertyProvider.LinkContextPropertyId, "Parity context");
                    break;
                }

            case "comments-everywhere.owm":
                // B11: setting a link's context, and moving an element, on statements that hold a comment.
                {
                    var cup = session.IdOf(WardleyIdentityKind.Component, "Cup of Tea")!;
                    await script.SetAsync(cup, WardleyContextPropertyProvider.MaturityPropertyId, "0.5");
                    await script.ExecuteAsync(cup, WardleyContextActionProvider.ToggleInertiaActionId);
                    await script.CommitAsync(cup, WardleyContextActionProvider.SetEvolveActionId, "0.9");
                    break;
                }

            case "pipelines-both-forms.owm":
                // B12: a new pipeline component in a block; a child added to the legacy form and to the empty pipeline.
                await script.CommitAsync(session.IdOf(WardleyIdentityKind.Component, "Kettle")!, WardleyContextActionProvider.AddToPipelineActionId, "Gas Kettle");
                await script.CommitAsync(session.IdOf(WardleyIdentityKind.Component, "Power")!, WardleyContextActionProvider.AddToPipelineActionId, "Solar");
                await script.CommitAsync(session.IdOf(WardleyIdentityKind.Component, "Water")!, WardleyContextActionProvider.AddToPipelineActionId, "Tap");
                break;

            case "decorators.owm":
                // B6: the edits of a component that carries an unknown decorator, and one that carries a decorator twice.
                {
                    var unknown = session.IdOf(WardleyIdentityKind.Component, "Unknown")!;
                    await script.ExecuteAsync(unknown, WardleyContextActionProvider.DecoratorActionIdFor(WardleyDecorator.Market));
                    await script.SetAsync(unknown, WardleyContextPropertyProvider.DecoratorsPropertyId, "build");
                    await script.ExecuteAsync(unknown, WardleyContextActionProvider.ToggleInertiaActionId);
                    await script.SetAsync(unknown, WardleyContextPropertyProvider.MaturityPropertyId, "0.45");
                    var repeated = session.IdOf(WardleyIdentityKind.Component, "Repeated")!;
                    await script.ExecuteAsync(repeated, WardleyContextActionProvider.DecoratorActionIdFor(WardleyDecorator.Build));
                    await script.SetAsync(repeated, WardleyContextPropertyProvider.DecoratorsPropertyId, "");
                    break;
                }

            case "lenient-trailer.owm":
                // B4: the edits of components whose trailer is written in another order or spacing.
                {
                    var ordered = session.IdOf(WardleyIdentityKind.Component, "Ordered")!;
                    await script.ExecuteAsync(ordered, WardleyContextActionProvider.ToggleInertiaActionId);
                    await script.ExecuteAsync(ordered, WardleyContextActionProvider.DecoratorActionIdFor(WardleyDecorator.Buy));
                    var spaced = session.IdOf(WardleyIdentityKind.Component, "Spaced")!;
                    await script.ExecuteAsync(spaced, WardleyContextActionProvider.DecoratorActionIdFor(WardleyDecorator.Market));
                    await script.ExecuteAsync(spaced, WardleyContextActionProvider.ToggleInertiaActionId);
                    var glued = session.IdOf(WardleyIdentityKind.Component, "Glued")!;
                    await script.ExecuteAsync(glued, WardleyContextActionProvider.ToggleInertiaActionId);
                    await script.ExecuteAsync(session.IdOf(WardleyIdentityKind.Component, "Lookalike")!, WardleyContextActionProvider.ToggleInertiaActionId);
                    break;
                }

            case "evolve-twice.owm":
                // A9: which of two evolve statements the rows and the edits reach.
                {
                    var kettle = session.IdOf(WardleyIdentityKind.Component, "Kettle")!;
                    await script.CommitAsync(kettle, WardleyContextActionProvider.SetEvolveActionId, "0.7");
                    await script.SetAsync(kettle, WardleyContextPropertyProvider.EvolveNamePropertyId, "Electric kettle");
                    await script.ExecuteAsync(kettle, WardleyContextActionProvider.ClearEvolveActionId);
                    await script.ExecuteAsync(kettle, WardleyContextActionProvider.ClearEvolveActionId);
                    await script.ExecuteAsync(kettle, WardleyContextActionProvider.ClearEvolveActionId);
                    break;
                }

            case "trailing-comments.owm":
                // B3: the edits of statements that hold a trailing comment; B12: a pipeline component indented by four.
                {
                    var kettle = session.IdOf(WardleyIdentityKind.Component, "Kettle")!;
                    await script.DragAsync(kettle, 0.6, 0.4);
                    await script.ExecuteAsync(kettle, WardleyContextActionProvider.DecoratorActionIdFor(WardleyDecorator.Market));
                    await script.ExecuteAsync(kettle, WardleyContextActionProvider.ToggleInertiaActionId);
                    await script.CommitAsync(kettle, WardleyContextActionProvider.SetEvolveActionId, "0.8");
                    await script.CommitAsync(kettle, WardleyContextActionProvider.AddToPipelineActionId, "Gas");
                    var powerLink = session.IdOf(WardleyIdentityKind.Link, $"Kettle{WardleyIdentityKeys.Separator}Power{WardleyIdentityKeys.Separator}{WardleyLinkKind.Dependency}")!;
                    await script.SetAsync(powerLink, WardleyContextPropertyProvider.LinkContextPropertyId, "Parity context");
                    await script.CommitAsync(kettle, WardleyContextActionProvider.RenameActionId, "Boiler");
                    break;
                }

            case "unclosed-pipeline.owm":
                // B2: adding to a pipeline that is never closed.
                await script.CommitAsync(session.IdOf(WardleyIdentityKind.Component, "Kettle")!, WardleyContextActionProvider.AddToPipelineActionId, "Induction");
                break;

            case "awkward-names.owm":
                // B13: a name read up to its coordinates and renamed up to its first bracket; a link
                // target holding a slash; a link source holding a hyphen, drawn again; a name holding //.
                {
                    var user = session.IdOf(WardleyIdentityKind.Component, "User")!;
                    await script.CommitAsync(session.IdOf(WardleyIdentityKind.Component, "Tea [hot]")!, WardleyContextActionProvider.RenameActionId, "Tea [cold]");
                    await script.CommitAsync(session.IdOf(WardleyIdentityKind.Component, "CI/CD")!, WardleyContextActionProvider.RenameActionId, "Delivery");
                    var selfService = session.IdOf(WardleyIdentityKind.Component, "Self-service")!;
                    await script.LinkAsync(selfService, session.IdOf(WardleyIdentityKind.Component, "C++ build"), WardleyContextActionProvider.LinkActionId);
                    await script.LinkAsync(selfService, session.IdOf(WardleyIdentityKind.Component, "C++ build"), WardleyContextActionProvider.LinkActionId);
                    await script.CommitAsync("", WardleyContextActionProvider.AddComponentActionId, "Before // after");
                    await script.CommitAsync(user, WardleyContextActionProvider.RenameActionId, "User // renamed");
                    await script.CommitAsync(selfService, WardleyContextActionProvider.AddToPipelineActionId, "Child // cut");
                    await script.CommitAsync(selfService, WardleyContextActionProvider.SetEvolveActionId, "0.9");
                    await script.SetAsync(selfService, WardleyContextPropertyProvider.EvolveNamePropertyId, "Arrives // cut");
                    break;
                }

            case "same-key-twice.owm":
            case "duplicate-names.owm":
                // B9: the holders of one kind and key, selected, moved and renamed by their id.
                {
                    var repeated = name == "same-key-twice.owm" ? "Tea" : "Kettle";
                    var id = session.IdOf(WardleyIdentityKind.Component, repeated)!;
                    await script.DragAsync(id, 0.1, 0.1);
                    await script.CommitAsync(id, WardleyContextActionProvider.RenameActionId, $"{repeated} renamed");
                    await script.CommitAsync("", WardleyContextActionProvider.AddComponentActionId, repeated);

                    // A later holder by its own id: moved, linked from (ruling 4 gives it a name with
                    // a number first) and renamed on its own line (ruling 5).
                    await script.UndoAsync();
                    var later = session.IdOf(WardleyIdentityKind.Component, WardleyIdentityKeys.Numbered(repeated, 2))!;
                    await script.DragAsync(later, 0.2, 0.8);
                    await script.LinkAsync(later, session.IdOf(WardleyIdentityKind.Component, name == "same-key-twice.owm" ? "User" : "Power"), WardleyContextActionProvider.LinkActionId);
                    await script.CommitAsync(session.IdOf(WardleyIdentityKind.Component, WardleyIdentityKeys.Numbered(repeated, 2)) ?? later, WardleyContextActionProvider.RenameActionId, $"{repeated} on its own");
                    break;
                }
        }

        return script;
    }

    /// <summary>One document's store, history and providers.</summary>
    private sealed partial class Session : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HashSet<string> _recorded;
        private readonly Dictionary<string, string> _masks = new(StringComparer.Ordinal);

        public Session(string folder, string body, HashSet<string> recorded)
        {
            Folder = folder;
            Body = body;
            _recorded = recorded;
            _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddWardleyMap().BuildServiceProvider();
            Store = _services.GetRequiredService<IWardleyDocumentStore>();
            Mapper = _services.GetRequiredService<WardleyElementMapper>();
            var history = _services.GetRequiredService<IHistoryStackStore>();
            Actions = new WardleyContextActionProvider(history, Store);
            Properties = new WardleyContextPropertyProvider(history, Store);
            Stack = history.Get(ProjectRoot.From(folder));
        }

        public string Folder { get; }

        public string Body { get; }

        public IWardleyDocumentStore Store { get; }

        public WardleyElementMapper Mapper { get; }

        public WardleyContextActionProvider Actions { get; }

        public WardleyContextPropertyProvider Properties { get; }

        public IHistoryStack Stack { get; }

        /// <summary>Opens the document, as the diagram's first open does; the readings and edits read what it holds.</summary>
        public async Task ReadAsync(CancellationToken cancellationToken) => await Store.GetOrLoadAsync(Body, cancellationToken);

        public WardleyMap Model => Store.Get(Body)!.Model;

        /// <summary>The identities the store holds now, each new id given its mask in the order they are listed.</summary>
        public IReadOnlyList<WardleyIdentityEntry> Identities
        {
            get
            {
                var identities = Store.Identities(Body);
                foreach (var entry in identities.Where(entry => !_recorded.Contains(entry.Id)))
                {
                    _masks.TryAdd(entry.Id, $"new-{_masks.Count + 1}");
                }

                return identities;
            }
        }

        /// <summary>The id of the first identity of <paramref name="kind"/> keyed <paramref name="key"/>, or null.</summary>
        public string? IdOf(string kind, string? key) =>
            key is null ? null : Identities.FirstOrDefault(entry => entry.Kind == kind && entry.Key == key)?.Id;

        /// <summary>One identity as a line: its id, its kind and its key.</summary>
        public string IdentityLine(WardleyIdentityEntry entry) => Mask($"{entry.Id} | {entry.Kind} | {TranscriptText.Quoted(entry.Key)}");

        /// <summary><paramref name="text"/> with every new id replaced by its mask.</summary>
        public string Mask(string text)
        {
            _ = Identities;
            return _masks.Count == 0 ? text : IdExpression().Replace(text, match => _masks.TryGetValue(match.Value, out var mask) ? mask : match.Value);
        }

        public ContextTarget Target(string elementId) =>
            ContextTarget.ForElement(Body, elementId, ProjectRoot.From(Folder), origin: MapOrigin);

        public WardleySession Open() => new(Body, Store, Mapper, Stack);

        public void Dispose() => _services.Dispose();

        [GeneratedRegex("[0-9a-z]{25}")]
        private static partial Regex IdExpression();
    }

    /// <summary>Runs the steps of one document's edit sequence and writes each as it goes.</summary>
    private sealed class Script(Utf8JsonWriter json, Session session, CancellationToken cancellationToken, string name = "edits")
    {
        private string _text = "";
        private IReadOnlyList<string> _identities = [];

        public async Task<string> StartAsync()
        {
            _text = await ReadAsync();
            _identities = Identities();
            json.WriteStartArray(name);
            return _text;
        }

        public async Task EndAsync(string original)
        {
            json.WriteEndArray();

            var undone = 0;
            var thrown = "";
            try
            {
                while (session.Stack.CanUndo && (await session.Stack.UndoAsync(cancellationToken)).IsSuccess)
                {
                    undone++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                thrown = $"; the next {Thrown(exception)}";
            }

            var restored = await ReadAsync();
            json.WriteString(name == "edits" ? "undoAll" : $"{name}UndoAll", $"{undone} undone{thrown}; {(string.Equals(restored, original, StringComparison.Ordinal) ? "the original text is back" : "the original text is NOT back")}");
            if (!string.Equals(restored, original, StringComparison.Ordinal))
            {
                json.WriteStartObject(name == "edits" ? "undoAllLeaves" : $"{name}UndoAllLeaves");
                json.WriteChange(original, restored);
                json.WriteEndObject();
            }

            json.WritePropertyName(name == "edits" ? "identitiesAfterUndoAll" : $"{name}IdentitiesAfterUndoAll");
            json.WriteLines(Identities());
        }

        /// <summary>A drop of <paramref name="id"/> at a canvas point, as the canvas reports one.</summary>
        public async Task DragAsync(string id, double x, double y)
        {
            string answer;
            try
            {
                await using var diagram = session.Open();
                answer = Answer(await diagram.MoveElementToAsync(id, x, y, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            await RecordAsync($"drag {Named(id)} to {x}, {y}", answer);
        }

        public async Task SetAsync(string target, string propertyId, string value)
        {
            var named = Named(target);
            string answer;
            try
            {
                var result = await session.Properties.SetAsync(session.Target(target), propertyId, value, cancellationToken);
                answer = result.IsSuccess ? "ok" : $"refused: {result.Error}";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            await RecordAsync($"set {named} {propertyId} = {TranscriptText.Quoted(value)}", answer);
        }

        public async Task ValidateAsync(string target, string actionId, string value)
        {
            var named = Named(target);
            string answer;
            try
            {
                var result = await session.Actions.ValidateAsync(session.Target(target), actionId, value, cancellationToken);
                answer = result.Valid ? "accepted" : $"rejected: {result.Reason}";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            await RecordAsync($"validate {actionId} on {named} = {TranscriptText.Quoted(value)}", answer);
        }

        /// <summary>Runs an action; answers "confirm" when it asked for a confirmation, else null.</summary>
        public async Task<string?> ExecuteAsync(string target, string actionId)
        {
            (ContextExecutionResult? result, _) = await RunAsync(target, actionId);
            return result is ContextExecutionRequiresConfirmation ? "confirm" : null;
        }

        /// <summary>A link or a flow drawn from <paramref name="source"/> through its menu, answered with <paramref name="target"/>.</summary>
        public async Task LinkAsync(string source, string? target, string actionId)
        {
            (ContextExecutionResult? result, _) = await RunAsync(source, actionId);
            if (result is ContextExecutionRequiresChoice && target is not null)
            {
                await CommitAsync(source, actionId, target);
            }
        }

        /// <summary>"Remove link…" on a component, answered with its first link when it asks which.</summary>
        public async Task UnlinkAsync(string component)
        {
            (ContextExecutionResult? result, _) = await RunAsync(component, WardleyContextActionProvider.UnlinkActionId);
            if (result is ContextExecutionRequiresChoice { Request.Options: [var first, ..] })
            {
                await CommitAsync(component, WardleyContextActionProvider.UnlinkActionId, first.Id);
            }
        }

        public async Task CommitAsync(string target, string actionId, string value)
        {
            var named = Named(target);
            var given = session.Mask(TranscriptText.Quoted(value));
            string answer;
            try
            {
                var result = await session.Actions.CommitAsync(session.Target(target), actionId, value, "", cancellationToken);
                answer = result.Completed ? "ok" : $"refused: {result.Error}";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            await RecordAsync($"commit {actionId} on {named} = {given}", answer);
        }

        public async Task UndoAsync()
        {
            var result = await session.Stack.UndoAsync(cancellationToken);
            await RecordAsync("undo", result.IsSuccess ? "ok" : $"refused: {result.Error}");
        }

        public async Task RedoAsync()
        {
            var result = await session.Stack.RedoAsync(cancellationToken);
            await RecordAsync("redo", result.IsSuccess ? "ok" : $"refused: {result.Error}");
        }

        private async Task<(ContextExecutionResult? Result, string Answer)> RunAsync(string target, string actionId)
        {
            var named = Named(target);
            ContextExecutionResult? result = null;
            string answer;
            try
            {
                result = await session.Actions.ExecuteAsync(session.Target(target), actionId, cancellationToken);
                answer = Describe(result);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                answer = Thrown(exception);
            }

            await RecordAsync($"execute {actionId} on {named}", answer);
            return (result, answer);
        }

        /// <summary>A target as a step names it: its id, and what the identities say it is now.</summary>
        private string Named(string target)
        {
            if (target.Length == 0)
            {
                return "the map";
            }

            var entry = session.Identities.FirstOrDefault(candidate => candidate.Id == target);
            return entry is null ? session.Mask(target) : session.Mask($"{target} ({entry.Kind} {TranscriptText.Quoted(entry.Key)})");
        }

        /// <summary>
        /// A step today's code throws on, recorded rather than ending the transcript: what it throws is
        /// the answer a user would meet, and the steps after it still run.
        /// </summary>
        private static string Thrown(Exception exception) => $"throws {exception.GetType().Name}: {exception.Message}";

        private static string Answer(string refusal) => refusal.Length == 0 ? "accepted" : $"refused: {refusal}";

        private async Task RecordAsync(string step, string answer)
        {
            var after = await ReadAsync();
            var identities = Identities();
            json.WriteStartObject();
            json.WriteString("do", step);
            json.WriteString("answer", session.Mask(answer));
            json.WriteChange(_text, after);
            var removed = _identities.Except(identities, StringComparer.Ordinal).ToArray();
            var added = identities.Except(_identities, StringComparer.Ordinal).ToArray();
            if (removed.Length > 0 || added.Length > 0)
            {
                json.WritePropertyName("identities");
                json.WriteLines([.. removed.Select(line => $"- {line}"), .. added.Select(line => $"+ {line}")]);
            }

            json.WriteEndObject();
            _text = after;
            _identities = identities;
        }

        private IReadOnlyList<string> Identities() => [.. session.Identities.Select(session.IdentityLine)];

        private async Task<string> ReadAsync() => TranscriptText.Decode(await File.ReadAllBytesAsync(session.Body, cancellationToken));

        private string Describe(ContextExecutionResult result) => result switch
        {
            ContextExecutionCompleted => "ok",
            ContextExecutionFailed failed => $"refused: {failed.Message}",
            ContextExecutionRequiresInput input =>
                $"asks: {input.Request.Title} | {input.Request.FieldLabel} | {TranscriptText.Quoted(input.Request.InitialValue)} | {input.Request.ConfirmLabel}",
            ContextExecutionRequiresChoice choice =>
                $"chooses: {choice.Request.Title} | {choice.Request.ConfirmLabel} | [{string.Join(", ", choice.Request.Options.Select(option => $"{session.Mask(option.Id)} {TranscriptText.Quoted(option.Label)}"))}] | {choice.Request.EmptyMessage}",
            ContextExecutionRequiresConfirmation confirmation =>
                $"confirms: {confirmation.Request.Title} | {confirmation.Request.Message} | {confirmation.Request.ConfirmLabel}",
            _ => result.ToString(),
        };
    }
}
