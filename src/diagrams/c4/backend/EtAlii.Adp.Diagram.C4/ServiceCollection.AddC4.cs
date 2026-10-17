using System.Text;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Registers the C4 family against core's seams. One call covers all seven types, because they
/// share one engine and differ only in the view each binds - which is data here, not code
/// (c4-diagrams design, "Overview").
/// </summary>
/// <remarks>
/// The host calls this beside the other modules' registrations; a test that needs the real
/// factories calls it too. Core never names C4: everything resolves by
/// <see cref="ToolOrigin"/>.
/// </remarks>
public static class ServiceCollectionAddC4Extension
{
    /// <summary>Each C4 diagram type, paired with the view kind it binds.</summary>
    private static readonly (string Type, C4ViewKind ViewKind)[] Types =
    [
        ("system-landscape", C4ViewKind.SystemLandscape),
        ("context", C4ViewKind.SystemContext),
        ("container", C4ViewKind.Container),
        ("component", C4ViewKind.Component),
        ("dynamic", C4ViewKind.Dynamic),
        ("deployment", C4ViewKind.Deployment),
    ];

    public static IServiceCollection AddC4(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The type's commands, so every edit is one undo away (tech.md's Commands rule).
        services.AddSingleton<ICommandHandler<SetElementNameCommand>, SetElementNameCommandHandler>();
        services.AddSingleton<ICommandHandler<SetElementDescriptionCommand>, SetElementDescriptionCommandHandler>();
        services.AddSingleton<ICommandHandler<SetElementTechnologyCommand>, SetElementTechnologyCommandHandler>();
        services.AddSingleton<ICommandHandler<SetRelationshipDescriptionCommand>, SetRelationshipDescriptionCommandHandler>();
        services.AddSingleton<ICommandHandler<SetRelationshipTechnologyCommand>, SetRelationshipTechnologyCommandHandler>();
        services.AddSingleton<ICommandHandler<MoveC4ElementCommand>, MoveC4ElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreC4ElementPositionCommand>, RestoreC4ElementPositionCommandHandler>();
        services.AddSingleton<ICommandHandler<AddC4ViewCommand>, AddC4ViewCommandHandler>();
        services.AddSingleton<ICommandHandler<AddC4ElementCommand>, AddC4ElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveC4ElementCommand>, RemoveC4ElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveC4ViewCommand>, RemoveC4ViewCommandHandler>();

        // What can be done to an element or a relationship, offered through the one path that
        // reaches the ribbon, the menu and the keyboard (Requirement 13.2).
        services.AddSingleton<IContextActionProvider, C4ContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, C4ContextPropertyProvider>();

        // One store and one mapper for the whole family: several diagrams open one document,
        // so they must share the instance that holds it (Requirement 1.2).
        services.TryAddSingleton<IC4DocumentStore, C4DocumentStore>();
        services.TryAddSingleton(C4Metrics.Default);
        services.TryAddSingleton<C4LayoutSidecar>();
        services.TryAddSingleton(provider => new C4ElementMapper(provider.GetRequiredService<C4Metrics>(), provider.GetRequiredService<C4LayoutSidecar>()));

        foreach ((string type, C4ViewKind viewKind) in Types)
        {
            var origin = new ToolOrigin("c4", type);
            services.AddEmptyDocument(origin, baseName => EmptyDocument(viewKind, baseName));

            // Makes a C4 element selectable, one per type, because the shared base answers for the
            // files of one type; what an element is does not depend on which (Requirement 13.2).
            services.AddSingleton<IContextSourceResolver>(provider => new C4ContextSourceResolver(
                origin,
                provider.GetRequiredService<DiagramFileRouter>(),
                provider.GetRequiredService<IC4DocumentStore>(),
                provider.GetRequiredService<C4ElementMapper>()));

            // The palette this view offers: exactly the kinds it permits, drawn from the
            // same rule the validator uses so the two cannot disagree (Requirement 12.2).
            services.AddSingleton<IDiagramToolboxProvider>(_ => new C4ToolboxProvider(origin, viewKind));

            // The session seam: which view a session shows comes from the .adp file, so all six
            // factories are the same code under different origins.
            services.AddSingleton<IDiagramSessionFactory>(provider => new C4SessionFactory(
                origin,
                provider.GetRequiredService<IC4DocumentStore>(),
                provider.GetRequiredService<C4ElementMapper>(),
                provider.GetRequiredService<IHistoryStackStore>()));

            // The reload seam: an external write to a C4 body reaches the shared store, and
            // through it every open session (modular-text-editors Requirement 5.3).
            services.AddDiagramDocumentReloader<IC4DocumentStore>(origin);

            // The type's rules, resolved by origin through core's validator registry, so C4's
            // violations reach the errors and warnings panel like any other type's
            // (errors-and-warnings-panel Requirement 3.1, c4-diagrams Requirement 10.8).
            services.AddSingleton<IDiagramValidator>(_ => new C4Validator(origin));
        }

        // The seventh type registers an empty document that refuses: it is registered so core's
        // startup check passes, and refuses so nobody gets a half-notation (Requirement 11.7).
        services.AddEmptyDocument(new ToolOrigin("c4", "code"), _ => throw new NotSupportedException(CodeUnavailable));

        return services;
    }

    /// <summary>What creating a <c>c4/code</c> diagram is told, and what the canvas shows in place of a diagram.</summary>
    /// <remarks>
    /// C4 advises against hand-drawing the code level at all: "most IDEs can generate this level
    /// of detail on demand". Where a code diagram is wanted, C4 specifies UML class or ER
    /// notation, not the box notation the other six views use - so <c>c4/code</c> delegates to a
    /// class or ER diagram type rather than growing a fourth notation inside these modules. None
    /// of those types exists yet, which makes this the one C4 type that cannot be completed on its
    /// own (c4-diagrams Requirement 11.7).
    /// </remarks>
    public const string CodeUnavailable =
        "C4 code diagrams are not available yet: this level uses UML class or entity-relationship " +
        "notation, which ADP does not implement yet, and C4 itself recommends generating it from " +
        "an IDE rather than drawing it by hand. Use a Component diagram to describe what is inside " +
        "a container.";

    /// <summary>
    /// The empty Structurizr DSL document a newly added C4 diagram of <paramref name="viewKind"/>
    /// starts from. The six types differ only in the view they declare, which is why they need no
    /// code of their own (c4-diagrams Requirement 2.1).
    /// </summary>
    /// <remarks>
    /// A C4 view is a view <em>of</em> something - a context diagram is about a software system,
    /// a component diagram about a container - so an empty document is not an empty model. It
    /// carries one placeholder software system named after the file, which is the smallest thing
    /// the declared view can legally be scoped to, and gives the user something to rename rather
    /// than a blank canvas with no way in.
    /// </remarks>
    public static string EmptyDocument(C4ViewKind viewKind, string baseName)
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
        if (viewKind == C4ViewKind.Component)
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
        if (viewKind == C4ViewKind.Deployment)
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
        builder.Append(ViewDeclaration(viewKind, id)).Append('\n');
        builder.Append("    }\n\n");
        builder.Append("}\n");

        return builder.ToString();
    }

    /// <summary>The view block for this type, scoped to what its kind requires (Requirements 5.4, 6.1, 7.1, 9.1, 9.6).</summary>
    private static string ViewDeclaration(C4ViewKind viewKind, string id)
    {
        var body = "            include *\n            autoLayout lr\n";
        return viewKind switch
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
            _ => throw new ArgumentOutOfRangeException(nameof(viewKind), viewKind, "Unknown C4 view kind."),
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
