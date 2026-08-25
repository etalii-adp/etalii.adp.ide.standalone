namespace EtAlii.Adp.Diagram.C4;

/// <summary>What a document parsed to: its model, its relationships, its views and its styles.</summary>
/// <param name="Name">The workspace name, for a title that wants to say it.</param>
/// <param name="Elements">Every declared element, in document order.</param>
/// <param name="Relationships">Every declared relationship, in document order.</param>
/// <param name="Views">Every declared view, in document order - the first is what a bare document opens.</param>
/// <param name="Styles">Tag-based element styles, which override ADP's default theme.</param>
/// <param name="Includes">
/// The files this document pulls in with <c>!include</c>. ADP does not follow them, so whatever
/// they declare is missing from the model - which the rule set reports, rather than leaving the
/// user to wonder where half their diagram went.
/// </param>
public sealed record C4Workspace(
    string Name,
    IReadOnlyList<C4Element> Elements,
    IReadOnlyList<C4Relationship> Relationships,
    IReadOnlyList<C4View> Views,
    IReadOnlyList<C4ElementStyle> Styles,
    IReadOnlyList<string> Includes)
{
    public static C4Workspace Empty { get; } = new("", [], [], [], [], []);

    /// <summary>The element with this id, or null. Ids are case-insensitive in the DSL.</summary>
    public C4Element? Find(string id) =>
        Elements.FirstOrDefault(element => string.Equals(element.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The view with this key, or null.</summary>
    public C4View? FindView(string key) =>
        Views.FirstOrDefault(view => string.Equals(view.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Reads the subset of the Structurizr DSL that ADP models. Everything it does not model - and
/// there is a lot, deliberately - it simply does not read: the document keeps its own text, so
/// an unrecognised construct survives a round trip untouched rather than needing a
/// representation here (c4-diagrams Requirement 3.3).
/// </summary>
/// <remarks>
/// Hand-written rather than a generated grammar, and deliberately forgiving. This is not a
/// validating parser: what a document *means* is <c>C4RuleSet</c>'s question, and reporting a
/// missing description as a parse failure would stop a mid-edit document from opening at all.
/// </remarks>
public static class C4Parser
{
    /// <summary>The keywords that declare an element, and the kind each declares.</summary>
    private static readonly Dictionary<string, C4ElementKind> ElementKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["person"] = C4ElementKind.Person,
        ["softwaresystem"] = C4ElementKind.SoftwareSystem,
        ["container"] = C4ElementKind.Container,
        ["component"] = C4ElementKind.Component,
        ["deploymentnode"] = C4ElementKind.DeploymentNode,
        ["infrastructurenode"] = C4ElementKind.InfrastructureNode,
        ["containerinstance"] = C4ElementKind.ContainerInstance,
        ["softwaresysteminstance"] = C4ElementKind.SoftwareSystemInstance,
    };

    /// <summary>The keywords that declare a view, and the kind each declares.</summary>
    private static readonly Dictionary<string, C4ViewKind> ViewKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["systemlandscape"] = C4ViewKind.SystemLandscape,
        ["systemcontext"] = C4ViewKind.SystemContext,
        ["container"] = C4ViewKind.Container,
        ["component"] = C4ViewKind.Component,
        ["dynamic"] = C4ViewKind.Dynamic,
        ["deployment"] = C4ViewKind.Deployment,
    };

    public static C4Workspace Parse(C4Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var state = new C4ParseState();
        var lines = document.CodeLines.ToArray();
        var index = 0;
        var inBlockComment = false;

        while (index < lines.Length)
        {
            var line = lines[index++];
            var code = SkipBlockComments(line.Code, ref inBlockComment);
            if (code.Length == 0)
            {
                continue;
            }

            var tokens = C4Tokens.Split(code);

            // Noted, not followed. Reading another file would make the model complete but the
            // round trip a lie: ADP writes only this document, so an edit landing in an
            // included file could not be saved (design "Prerequisites and blockers" 4).
            if (tokens.Count >= 2 && tokens[0].Equals("!include", StringComparison.OrdinalIgnoreCase))
            {
                state.Includes.Add(tokens[1]);
                continue;
            }

            ReadLine(tokens, line.Number, state);
        }

        return new C4Workspace(state.WorkspaceName, state.Elements, state.Relationships, state.Views, state.Styles, state.Includes);
    }

    /// <summary>
    /// <paramref name="code"/> with any block-commented span removed, tracking whether the
    /// comment continues past this line. Block comments are text the document carries through
    /// untouched; the parser simply must not read model keywords out of them.
    /// </summary>
    private static string SkipBlockComments(string code, ref bool inBlockComment)
    {
        var result = new System.Text.StringBuilder();
        var index = 0;
        while (index < code.Length)
        {
            if (inBlockComment)
            {
                var close = code.IndexOf("*/", index, StringComparison.Ordinal);
                if (close < 0)
                {
                    return result.ToString();
                }

                inBlockComment = false;
                index = close + 2;
                continue;
            }

            var open = code.IndexOf("/*", index, StringComparison.Ordinal);
            if (open < 0)
            {
                result.Append(code[index..]);
                return result.ToString();
            }

            result.Append(code[index..open]);
            inBlockComment = true;
            index = open + 2;
        }

        return result.ToString();
    }

    private static void ReadLine(IReadOnlyList<string> tokens, uint number, C4ParseState state)
    {
        if (tokens.Count == 0)
        {
            return;
        }

        // A closing brace pops whatever the matching opener pushed.
        if (tokens[0] == "}")
        {
            state.Pop();
            return;
        }

        var opensBlock = tokens[^1] == "{";
        var body = opensBlock ? tokens.Take(tokens.Count - 1).ToArray() : [.. tokens];
        if (body.Length == 0)
        {
            state.Push(C4ParseScope.Unknown);
            return;
        }

        // `identifier = keyword ...` - the identifier is the model's own handle on the element.
        string? identifier = null;
        if (body.Length >= 3 && body[1] == "=")
        {
            identifier = body[0];
            body = body[2..];
        }

        var keyword = body[0];
        var arguments = body[1..];

        switch (state.Current)
        {
            case C4ParseScope.Views:
                ReadInViews(keyword, arguments, identifier, number, opensBlock, state);
                return;

            case C4ParseScope.Styles:
                ReadInStyles(keyword, arguments, number, opensBlock, state);
                return;

            case C4ParseScope.View:
                ReadInsideAView(keyword, arguments, body, number, opensBlock, state);
                return;
        }

        ReadInModel(keyword, arguments, body, identifier, number, opensBlock, state);
    }

    private static void ReadInModel(
        string keyword,
        string[] arguments,
        string[] body,
        string? identifier,
        uint number,
        bool opensBlock,
        C4ParseState state)
    {
        switch (keyword.ToLowerInvariant())
        {
            case "workspace":
                state.WorkspaceName = arguments.Length > 0 ? arguments[0] : "";
                state.Push(opensBlock ? C4ParseScope.Workspace : C4ParseScope.Unknown);
                return;

            case "model":
                state.Push(opensBlock ? C4ParseScope.Model : C4ParseScope.Unknown);
                return;

            case "views":
                state.Push(opensBlock ? C4ParseScope.Views : C4ParseScope.Unknown);
                return;

            case "deploymentenvironment":
                state.Environment = arguments.Length > 0 ? arguments[0] : "";
                state.Push(opensBlock ? C4ParseScope.Model : C4ParseScope.Unknown);
                return;

            case "group":
                // A group is a visual cluster, not an abstraction level: its children belong to
                // whatever contains the group, so nothing is pushed onto the parent chain.
                state.Push(opensBlock ? C4ParseScope.Model : C4ParseScope.Unknown);
                return;
        }

        // `a -> b "description" "technology"`
        if (body.Length >= 3 && body[1] == "->")
        {
            state.Relationships.Add(new C4Relationship(
                SourceId: body[0],
                DestinationId: body[2],
                Description: body.Length > 3 ? body[3] : "",
                Technology: body.Length > 4 ? body[4] : "",
                Tags: body.Length > 5 ? [body[5]] : [],
                Line: number));
            if (opensBlock)
            {
                state.Push(C4ParseScope.Unknown);
            }

            return;
        }

        // A relationship declared inside an element block: `-> destination "description"`.
        if (keyword == "->" && arguments.Length >= 1 && state.CurrentElementId is { } source)
        {
            state.Relationships.Add(new C4Relationship(
                source,
                arguments[0],
                arguments.Length > 1 ? arguments[1] : "",
                arguments.Length > 2 ? arguments[2] : "",
                [],
                number));
            return;
        }

        // `tags "A" "B"` on a line of its own inside an element's block. The DSL takes tags
        // either as a declaration argument or as their own line, and a document whose elements
        // have bodies - which every nested deployment node has - reaches for the second form.
        // Reading only the first left them untagged, and tags are what styles key off, so the
        // elements also came out unstyled (found by the AWS deployment fixture, where every
        // tag is written this way).
        if (keyword.Equals("tags", StringComparison.OrdinalIgnoreCase)
            && arguments.Length > 0
            && state.CurrentElementId is { } tagged)
        {
            state.AddTags(tagged, arguments.SelectMany(SplitTags));
            return;
        }

        if (!ElementKeywords.TryGetValue(keyword, out var kind))
        {
            // Not something this parser models - `!docs`, `properties`, `url`... The
            // document keeps the text; only a block's nesting has to be tracked so the parent
            // chain stays correct.
            if (opensBlock)
            {
                state.Push(C4ParseScope.Unknown);
            }

            return;
        }

        var isInstance = kind is C4ElementKind.ContainerInstance or C4ElementKind.SoftwareSystemInstance;
        var (technology, tags) = ReadTechnologyAndTags(kind, arguments);
        var element = new C4Element(
            Id: identifier ?? state.GenerateId(kind, arguments.Length > 0 ? arguments[0] : keyword),
            Kind: kind,
            Name: isInstance ? "" : arguments.Length > 0 ? arguments[0] : "",
            Description: !isInstance && arguments.Length > 1 ? arguments[1] : "",
            Technology: technology,
            Tags: tags,
            ParentId: state.CurrentElementId,
            Line: number,
            ReferencedId: isInstance && arguments.Length > 0 ? arguments[0] : null);

        state.Elements.Add(element);
        if (opensBlock)
        {
            state.PushElement(element.Id);
        }
    }

    private static void ReadInViews(string keyword, string[] arguments, string? identifier, uint number, bool opensBlock, C4ParseState state)
    {
        if (keyword.Equals("styles", StringComparison.OrdinalIgnoreCase))
        {
            state.Push(opensBlock ? C4ParseScope.Styles : C4ParseScope.Unknown);
            return;
        }

        if (!ViewKeywords.TryGetValue(keyword, out var kind))
        {
            // `theme`, `branding`, `terminology`, `properties` - carried by the document, not modelled.
            if (opensBlock)
            {
                state.Push(C4ParseScope.Unknown);
            }

            return;
        }

        // The argument shapes differ by kind:
        //   systemLandscape "key"
        //   systemContext <scope> "key"
        //   container <scope> "key"          component <scope> "key"
        //   dynamic <scope> "key"            deployment <scope> "environment" "key"
        string? scope = null;
        string? environment = null;
        string key;
        switch (kind)
        {
            case C4ViewKind.SystemLandscape:
                key = arguments.Length > 0 ? arguments[0] : "landscape";
                break;

            case C4ViewKind.Deployment:
                scope = arguments.Length > 0 ? arguments[0] : null;
                environment = arguments.Length > 1 ? arguments[1] : null;
                key = arguments.Length > 2 ? arguments[2] : environment ?? "deployment";
                break;

            default:
                scope = arguments.Length > 0 ? arguments[0] : null;
                key = arguments.Length > 1 ? arguments[1] : kind.ToString().ToLowerInvariant();
                break;
        }

        var view = new C4View(kind, key, scope, environment, Title: null, IncludesEverything: false, [], [], null, [], number);
        state.Views.Add(view);
        state.Push(opensBlock ? C4ParseScope.View : C4ParseScope.Unknown);
        if (opensBlock)
        {
            state.CurrentViewIndex = state.Views.Count - 1;
        }

        _ = identifier;
    }

    private static void ReadInsideAView(string keyword, string[] arguments, string[] body, uint number, bool opensBlock, C4ParseState state)
    {
        if (state.CurrentViewIndex is not { } viewIndex)
        {
            if (opensBlock)
            {
                state.Push(C4ParseScope.Unknown);
            }

            return;
        }

        var view = state.Views[viewIndex];
        switch (keyword.ToLowerInvariant())
        {
            case "include":
                state.Views[viewIndex] = arguments.Contains("*")
                    ? view with { IncludesEverything = true }
                    : view with { Includes = [.. view.Includes, .. arguments] };
                return;

            case "exclude":
                state.Views[viewIndex] = view with { Excludes = [.. view.Excludes, .. arguments] };
                return;

            case "title":
                state.Views[viewIndex] = view with { Title = arguments.Length > 0 ? arguments[0] : null };
                return;

            case "autolayout":
                state.Views[viewIndex] = view with
                {
                    AutoLayout = new C4AutoLayout(
                        arguments.Length > 0 ? arguments[0] : "tb",
                        arguments.Length > 1 && int.TryParse(arguments[1], out var rank) ? rank : null,
                        arguments.Length > 2 && int.TryParse(arguments[2], out var node) ? node : null),
                };
                return;
        }

        // A dynamic view's body is its ordered interactions: `source -> destination "description"`.
        if (body.Length >= 3 && body[1] == "->")
        {
            var order = (view.Interactions.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            state.Views[viewIndex] = view with
            {
                Interactions = [.. view.Interactions, new C4Interaction(body[0], body[2], body.Length > 3 ? body[3] : "", order, number)],
            };
            return;
        }

        if (opensBlock)
        {
            state.Push(C4ParseScope.Unknown);
        }
    }

    private static void ReadInStyles(string keyword, string[] arguments, uint number, bool opensBlock, C4ParseState state)
    {
        if (keyword.Equals("element", StringComparison.OrdinalIgnoreCase) && arguments.Length > 0)
        {
            state.Styles.Add(new C4ElementStyle(arguments[0], null, null, null, null, number));
            state.Push(opensBlock ? C4ParseScope.Style : C4ParseScope.Unknown);
            return;
        }

        if (opensBlock)
        {
            state.Push(C4ParseScope.Unknown);
        }
    }

    /// <summary>
    /// The technology and tags out of an element's arguments, whose positions depend on the
    /// keyword. Only the things C4 requires a technology of have one: a person and a software
    /// system take <c>name, description, tags</c>, so reading position 2 as a technology for
    /// them would silently turn every tag list into a technology - and every "External" tag
    /// would stop marking anything external.
    /// </summary>
    private static (string Technology, IReadOnlyList<string> Tags) ReadTechnologyAndTags(C4ElementKind kind, string[] arguments)
    {
        switch (kind)
        {
            case C4ElementKind.ContainerInstance:
            case C4ElementKind.SoftwareSystemInstance:
                // `containerInstance <ref> [tags]` - no name, description or technology.
                return ("", arguments.Length > 1 ? SplitTags(arguments[1]) : []);

            case C4ElementKind.Person:
            case C4ElementKind.SoftwareSystem:
                return ("", arguments.Length > 2 ? SplitTags(arguments[2]) : []);

            case C4ElementKind.DeploymentNode:
                // `deploymentNode <name> [description] [technology] [tags] [instances]`. The
                // instance count comes *after* the tags, and reading the two the wrong way round
                // turns the count into a tag - which is what it did until the C4 worked example,
                // whose `deploymentNode "bigbank-web***" "" "Ubuntu 16.04 LTS" "" 4` exposed it.
                return (
                    arguments.Length > 2 ? arguments[2] : "",
                    arguments.Length > 3 ? SplitTags(arguments[3]) : []);

            default:
                return (arguments.Length > 2 ? arguments[2] : "", arguments.Length > 3 ? SplitTags(arguments[3]) : []);
        }
    }

    private static IReadOnlyList<string> SplitTags(string tags) =>
        tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

}
