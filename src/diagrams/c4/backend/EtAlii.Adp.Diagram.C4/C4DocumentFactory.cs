using System.Text;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// The empty Structurizr DSL document a newly added C4 diagram starts from. One instance per
/// C4 diagram type: they differ only in the view they declare, which is why the seven modules
/// need no code of their own (c4-diagrams Requirement 2.1).
/// </summary>
/// <remarks>
/// A C4 view is a view <em>of</em> something - a context diagram is about a software system,
/// a component diagram about a container - so an empty document is not an empty model. It
/// carries one placeholder software system named after the file, which is the smallest thing
/// the declared view can legally be scoped to, and gives the user something to rename rather
/// than a blank canvas with no way in.
/// </remarks>
public sealed class C4DocumentFactory : IDiagramDocumentFactory
{
    private readonly C4ViewKind _viewKind;

    public C4DocumentFactory(DiagramOrigin origin, C4ViewKind viewKind)
    {
        ArgumentNullException.ThrowIfNull(origin);
        Origin = origin;
        _viewKind = viewKind;
    }

    public DiagramOrigin Origin { get; }

    public string CreateEmptyDocument(string baseName)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? "system" : baseName.Trim();
        var id = IdentifierFor(name);
        var builder = new StringBuilder();

        builder.Append("workspace \"").Append(Escape(name)).Append("\" {\n\n");
        builder.Append("    model {\n");
        builder.Append("        ").Append(id).Append(" = softwareSystem \"").Append(Escape(name)).Append('"');

        // A component view is scoped to a container, so the placeholder model has to reach one
        // level deeper for that type - and a container needs a technology, because C4 requires
        // one on every container (Requirement 10.3). Every other kind needs nothing inside the
        // system, so it gets no block rather than an empty one.
        if (_viewKind == C4ViewKind.Component)
        {
            builder.Append(" {\n");
            builder.Append("            application = container \"Application\" \"\" \"Technology\"\n");
            builder.Append("        }\n");
        }
        else
        {
            builder.Append('\n');
        }

        // A deployment view names an environment, so one has to exist for the view to bind to.
        if (_viewKind == C4ViewKind.Deployment)
        {
            builder.Append('\n');
            builder.Append("        deploymentEnvironment \"Production\" {\n");
            // With nothing inside it the environment does not exist as far as Structurizr is
            // concerned - an environment is a property of the nodes deployed into it, not a
            // declaration in its own right - so the view below binds to nothing and the whole
            // document is rejected. One node is the smallest thing that makes it valid, and a
            // sensible place for the first container instance to land.
            builder.Append("            deploymentNode \"Server\" {\n");
            builder.Append("            }\n");
            builder.Append("        }\n");
        }

        builder.Append("    }\n\n");
        builder.Append("    views {\n");
        builder.Append(ViewDeclaration(id)).Append('\n');
        builder.Append("    }\n\n");
        builder.Append("}\n");

        return builder.ToString();
    }

    /// <summary>The view block for this type, scoped to what its kind requires (Requirements 5.4, 6.1, 7.1, 9.1, 9.6).</summary>
    private string ViewDeclaration(string id)
    {
        var body = "            include *\n            autoLayout lr\n";
        return _viewKind switch
        {
            // A landscape has no focal element: that absence is what distinguishes it from a
            // context diagram (Requirement 9.6).
            C4ViewKind.SystemLandscape => $"        systemLandscape \"landscape\" {{\n{body}        }}",
            C4ViewKind.SystemContext => $"        systemContext {id} \"context\" {{\n{body}        }}",
            C4ViewKind.Container => $"        container {id} \"containers\" {{\n{body}        }}",
            // Scoped by the bare identifier rather than `{id}.application`: the DSL's
            // identifiers are flat unless a document asks for `!identifiers hierarchical`, so
            // the dotted form names nothing and Structurizr rejects the document outright
            // ("The container ... does not exist").
            C4ViewKind.Component => $"        component application \"components\" {{\n{body}        }}",
            // A dynamic view's body is its ordered interactions, and a new one has none yet -
            // so no include, which would mean something different here (Requirement 7.5).
            C4ViewKind.Dynamic => $"        dynamic {id} \"scenario\" {{\n            autoLayout lr\n        }}",
            C4ViewKind.Deployment => $"        deployment {id} \"Production\" \"deployment\" {{\n{body}        }}",
            _ => throw new ArgumentOutOfRangeException(nameof(_viewKind), _viewKind, "Unknown C4 view kind."),
        };
    }

    /// <summary>
    /// A DSL identifier for a file name: the DSL allows letters, digits and underscores, so
    /// everything else becomes an underscore and a leading digit is prefixed.
    /// </summary>
    private static string IdentifierFor(string name)
    {
        var characters = name.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray();
        var identifier = new string(characters).Trim('_');
        if (identifier.Length == 0)
        {
            return "system";
        }

        return char.IsDigit(identifier[0]) ? "_" + identifier : identifier;
    }

    private static string Escape(string value) => value.Replace("\"", "\\\"", StringComparison.Ordinal);
}
