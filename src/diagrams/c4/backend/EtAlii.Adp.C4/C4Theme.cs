namespace EtAlii.Adp.C4;

/// <summary>
/// The default look of a C4 diagram: the values the reference diagrams on c4model.com show,
/// which are Structurizr's default theme.
/// </summary>
/// <remarks>
/// <para>
/// A <b>default</b>, not a rule. C4 states it is notation independent and that the familiar
/// blue and grey "isn't something that is dictated by the C4 model", so a document's own
/// <c>styles</c> block overrides any of this, and no rule in <see cref="C4RuleSet"/> is
/// expressed in terms of colour (c4-diagrams Requirement 4.8).
/// </para>
/// <para>
/// The one non-obvious value is the component's black text: at <c>#85bbf0</c> the fill is light
/// enough that white would fail to read, and the reference theme says so too.
/// </para>
/// </remarks>
public static class C4Theme
{
    public const string PersonBackground = "#08427b";
    public const string SoftwareSystemBackground = "#1168bd";
    public const string ContainerBackground = "#438dd5";
    public const string ComponentBackground = "#85bbf0";
    public const string ExternalBackground = "#999999";
    public const string DeploymentNodeBackground = "#ffffff";

    public const string LightText = "#ffffff";
    public const string DarkText = "#000000";

    /// <summary>The default style for an element, before any document override.</summary>
    public static C4Style For(C4Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        // Outside the model's scope: muted, so scope is readable at a glance (Requirement 4.4).
        if (element.IsExternal)
        {
            return new C4Style { Background = ExternalBackground, Color = LightText, Shape = ShapeOf(element) };
        }

        var (background, color) = element.Kind switch
        {
            C4ElementKind.Person => (PersonBackground, LightText),
            C4ElementKind.SoftwareSystem or C4ElementKind.SoftwareSystemInstance => (SoftwareSystemBackground, LightText),
            C4ElementKind.Container or C4ElementKind.ContainerInstance => (ContainerBackground, LightText),
            C4ElementKind.Component => (ComponentBackground, DarkText),
            C4ElementKind.DeploymentNode or C4ElementKind.InfrastructureNode => (DeploymentNodeBackground, DarkText),
            _ => (SoftwareSystemBackground, LightText),
        };

        return new C4Style { Background = background, Color = color, Shape = ShapeOf(element) };
    }

    /// <summary>
    /// The shape an element is drawn as. A person gets the person shape; a data store gets a
    /// cylinder, which the DSL expresses as a tag rather than a kind (Requirement 4.5).
    /// </summary>
    public static string ShapeOf(C4Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element.Kind == C4ElementKind.Person)
        {
            return "Person";
        }

        if (element.Tags.Any(tag => tag.Equals("Database", StringComparison.OrdinalIgnoreCase)))
        {
            return "Cylinder";
        }

        return element.Kind switch
        {
            C4ElementKind.DeploymentNode or C4ElementKind.InfrastructureNode => "Box",
            _ => "RoundedBox",
        };
    }

    /// <summary>
    /// <paramref name="style"/> with any matching document style applied on top. A document's
    /// own tag styles win, which is what makes the palette a theme rather than a law.
    /// </summary>
    public static C4Style Apply(C4Style style, C4Element element, IReadOnlyList<C4ElementStyle> documentStyles)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(documentStyles);

        // Tags that name this element: its own, plus the kind's implicit tag, which is how the
        // DSL's `element "Person" { ... }` reaches every person.
        var tags = element.Tags.Append(KindTag(element.Kind)).ToArray();
        var result = style.Clone();
        foreach (var candidate in documentStyles.Where(s => tags.Any(tag => tag.Equals(s.Tag, StringComparison.OrdinalIgnoreCase))))
        {
            if (candidate.Background is { Length: > 0 })
            {
                result.Background = candidate.Background;
            }

            if (candidate.Color is { Length: > 0 })
            {
                result.Color = candidate.Color;
            }

            if (candidate.Shape is { Length: > 0 })
            {
                result.Shape = candidate.Shape;
            }

            if (candidate.Border is { Length: > 0 })
            {
                result.Border = candidate.Border;
            }
        }

        return result;
    }

    /// <summary>The implicit tag every element of a kind carries, as the DSL defines them.</summary>
    public static string KindTag(C4ElementKind kind) => kind switch
    {
        C4ElementKind.Person => "Person",
        C4ElementKind.SoftwareSystem or C4ElementKind.SoftwareSystemInstance => "Software System",
        C4ElementKind.Container or C4ElementKind.ContainerInstance => "Container",
        C4ElementKind.Component => "Component",
        C4ElementKind.DeploymentNode => "Deployment Node",
        C4ElementKind.InfrastructureNode => "Infrastructure Node",
        _ => "Element",
    };
}
