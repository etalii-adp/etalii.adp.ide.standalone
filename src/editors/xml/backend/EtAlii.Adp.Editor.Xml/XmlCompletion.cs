using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Schema;

namespace EtAlii.Adp.Editor.Xml;

/// <summary>
/// What an XSD allows at the caret (xml-editor Requirement 7): the elements the content model
/// expects there, the attributes a start tag may still take, and the values a type limits. The
/// text before the caret is fed to an <see cref="XmlSchemaValidator"/> event by event, the
/// loop task 1's spike proved; a position the text before it hides is answered with nothing.
/// </summary>
internal static class XmlCompletion
{
    private const string NamespaceNotDeclared = "namespace not declared";
    private const string Required = "required";

    /// <summary>A boost that sorts required attributes above the rest (Requirement 7.2).</summary>
    private const int RequiredBoost = 10;

    public static IReadOnlyList<TextCompletion> Complete(string content, int offset, bool isExplicit, XmlSchemaSet schemas, string fullPath, ProjectXmlResolver resolver)
    {
        var caret = CaretContext.At(content, offset);
        if (caret.Kind == CaretKind.Unknown)
        {
            return [];
        }

        // Requirement 7.7: the text before the markup at the caret is read; an error in it
        // hides where the caret stands, and nothing is guessed.
        if (Fed.From(content[..caret.Cut], fullPath, resolver, schemas) is not { } fed)
        {
            return [];
        }

        return caret.Kind switch
        {
            CaretKind.Content => InContent(caret, fed, isExplicit),
            CaretKind.ElementName => Elements(caret, fed, typedOpen: true),
            CaretKind.AttributeName => Attributes(caret, fed),
            CaretKind.AttributeValue => AttributeValues(caret, fed),
            _ => [],
        };
    }

    private static IReadOnlyList<TextCompletion> InContent(CaretContext caret, Fed fed, bool isExplicit)
    {
        // Requirement 7.3: the text of an element whose type limits it.
        var values = fed.Current?.ElementSchemaType is { } type
            ? Values(SimpleTypeOf(type), caret.From, caret.To)
            : [];

        // Requirement 7.1: elements after a '<', or when asked for.
        var elements = isExplicit && caret.From == caret.To
            ? Elements(caret, fed, typedOpen: false)
            : [];
        return [.. values, .. elements];
    }

    private static IReadOnlyList<TextCompletion> Elements(CaretContext caret, Fed fed, bool typedOpen)
    {
        var items = new List<TextCompletion>();
        var seen = new HashSet<XmlQualifiedName>();

        // A wildcard says anything may stand here, which is no suggestion (task 1, spike).
        foreach (var element in fed.Validator.GetExpectedParticles().OfType<XmlSchemaElement>())
        {
            if (!seen.Add(element.QualifiedName))
            {
                continue;
            }

            (string name, string detail) = Named(element.QualifiedName, fed.Scopes);
            var insert = caret.IsWrittenTag ? name : ElementInsert(name, element, fed.Scopes, typedOpen);
            items.Add(new TextCompletion(name, detail, Documentation(element, element.ElementSchemaType), caret.From, caret.To, insert));
        }

        return items;
    }

    private static IReadOnlyList<TextCompletion> Attributes(CaretContext caret, Fed fed)
    {
        if (fed.Enter(caret) is null)
        {
            return [];
        }

        // What is written already is not offered again - apart from the name the caret is on,
        // which is how a hover over a written name finds its documentation (Requirement 7.6).
        var written = caret.Attributes
            .Where(attribute => attribute.NameFrom != caret.From)
            .Select(attribute => attribute.Name)
            .ToHashSet(StringComparer.Ordinal);
        var items = new List<TextCompletion>();
        // The instance namespace's attributes are allowed on every element by the platform,
        // not by this schema, and are no suggestion of it.
        foreach (var attribute in fed.Validator.GetExpectedAttributes().Where(attribute => attribute.QualifiedName.Namespace != XmlSchema.InstanceNamespace))
        {
            (string name, string detail) = Named(attribute.QualifiedName, fed.Scopes);
            if (written.Contains(name))
            {
                continue;
            }

            var required = attribute.Use == XmlSchemaUse.Required;
            var note = string.Join(", ", new[] { required ? Required : "", detail }.Where(part => part.Length > 0));
            items.Add(new TextCompletion(
                name,
                note,
                Documentation(attribute, attribute.AttributeSchemaType),
                caret.From,
                caret.To,
                caret.From == caret.To ? $"{name}=\"${{1}}\"" : name,
                required ? RequiredBoost : 0));
        }

        return items;
    }

    private static IReadOnlyList<TextCompletion> AttributeValues(CaretContext caret, Fed fed)
    {
        if (fed.Enter(caret) is not { } element || element.ElementSchemaType is not XmlSchemaComplexType type)
        {
            return [];
        }

        var name = Qualified(caret.AttributeName, fed.Scopes, isAttribute: true);
        return type.AttributeUses[name] is XmlSchemaAttribute attribute
            ? Values(attribute.AttributeSchemaType, caret.From, caret.To)
            : [];
    }

    /// <summary>The values an enumeration lists, or <c>true</c> and <c>false</c> for a boolean.</summary>
    private static IReadOnlyList<TextCompletion> Values(XmlSchemaSimpleType? type, int from, int to)
    {
        for (var current = type; current is not null; current = current.BaseXmlSchemaType as XmlSchemaSimpleType)
        {
            if (current.Content is XmlSchemaSimpleTypeRestriction restriction)
            {
                var values = restriction.Facets.OfType<XmlSchemaEnumerationFacet>().ToArray();
                if (values.Length > 0)
                {
                    return [.. values.Select(facet => new TextCompletion(facet.Value ?? "", "", Documentation(facet, null), from, to, facet.Value ?? ""))];
                }
            }
        }

        return type?.Datatype?.TypeCode == XmlTypeCode.Boolean
            ? [new TextCompletion("true", "", "", from, to, "true"), new TextCompletion("false", "", "", from, to, "false")]
            : [];
    }

    private static XmlSchemaSimpleType? SimpleTypeOf(XmlSchemaType type)
    {
        // A complex type with simple content limits its text the way its simple base does.
        for (var current = type; current is not null; current = current.BaseXmlSchemaType)
        {
            if (current is XmlSchemaSimpleType simple)
            {
                return simple;
            }

            if (current is XmlSchemaComplexType { ContentType: not XmlSchemaContentType.TextOnly })
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// A start tag with the required attributes, each empty with a tab stop, then either a
    /// self-closing end or an end tag with a tab stop between (Requirement 7.4).
    /// </summary>
    private static string ElementInsert(string name, XmlSchemaElement element, XmlNamespaceManager scopes, bool typedOpen)
    {
        var insert = new StringBuilder();
        if (!typedOpen)
        {
            insert.Append('<');
        }

        insert.Append(name);
        var stop = 1;
        if (element.ElementSchemaType is XmlSchemaComplexType complex)
        {
            foreach (var attribute in complex.AttributeUses.Values.OfType<XmlSchemaAttribute>().Where(attribute => attribute.Use == XmlSchemaUse.Required))
            {
                (string attributeName, _) = Named(attribute.QualifiedName, scopes);
                insert.Append(CultureInfo.InvariantCulture, $" {attributeName}=\"${{{stop++}}}\"");
            }

            if (complex.ContentType == XmlSchemaContentType.Empty)
            {
                return insert.Append("/>").ToString();
            }
        }

        return insert.Append(CultureInfo.InvariantCulture, $">${{{stop}}}</{name}>").ToString();
    }

    /// <summary>
    /// The name as the document writes it: with the prefix declared for its namespace, or
    /// without one and a note when none is declared - no declaration is added (Requirement 7.5).
    /// </summary>
    private static (string Name, string Detail) Named(XmlQualifiedName name, XmlNamespaceManager scopes)
    {
        if (name.Namespace.Length == 0)
        {
            return (name.Name, "");
        }

        var prefix = scopes.LookupPrefix(name.Namespace);
        return prefix switch
        {
            null => (name.Name, NamespaceNotDeclared),
            "" => (name.Name, ""),
            _ => ($"{prefix}:{name.Name}", ""),
        };
    }

    private static XmlQualifiedName Qualified(string written, XmlNamespaceManager scopes, bool isAttribute)
    {
        var colon = written.IndexOf(':', StringComparison.Ordinal);
        return colon < 0
            // An unprefixed attribute is in no namespace; an unprefixed element is in the default one.
            ? new XmlQualifiedName(written, isAttribute ? "" : scopes.DefaultNamespace)
            : new XmlQualifiedName(written[(colon + 1)..], scopes.LookupNamespace(written[..colon]) ?? "");
    }

    /// <summary>The text of an item's <c>xs:documentation</c>, or of its type's (Requirement 7.6).</summary>
    private static string Documentation(XmlSchemaAnnotated item, XmlSchemaType? type)
    {
        foreach (var annotated in new[] { item, type })
        {
            var text = annotated?.Annotation?.Items.OfType<XmlSchemaDocumentation>()
                .SelectMany(documentation => documentation.Markup ?? [])
                .Select(node => node?.InnerText ?? "")
                .Aggregate("", (all, next) => all + next)
                .Trim();
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }
        }

        return "";
    }

    /// <summary>The validator, left where the text before the caret's markup leaves it.</summary>
    private sealed class Fed
    {
        private readonly Stack<XmlSchemaElement?> _open = new();

        private Fed(XmlSchemaValidator validator, XmlNamespaceManager scopes)
        {
            Validator = validator;
            Scopes = scopes;
        }

        public XmlSchemaValidator Validator { get; }

        public XmlNamespaceManager Scopes { get; }

        /// <summary>The declaration of the innermost open element, when the schema has one.</summary>
        public XmlSchemaElement? Current => _open.Count > 0 ? _open.Peek() : null;

        /// <summary>
        /// Feeds <paramref name="before"/> to a validator, or answers null when the text holds
        /// an error before its end: running out of text is how a cut document ends, and the
        /// reader reports that at the very end, while a real error is reported where it is.
        /// </summary>
        public static Fed? From(string before, string fullPath, ProjectXmlResolver resolver, XmlSchemaSet schemas)
        {
            var names = new NameTable();
            var scopes = new XmlNamespaceManager(names);
            var validator = new XmlSchemaValidator(names, schemas, scopes, XmlSchemaValidationFlags.None);
            validator.ValidationEventHandler += (_, _) => { };
            validator.Initialize();
            var fed = new Fed(validator, scopes);

            using var reader = XmlReaders.Document(before, fullPath, resolver);
            try
            {
                while (reader.Read())
                {
                    fed.Read(reader);
                }
            }
            catch (XmlException exception)
            {
                var at = new TextPositions(before).Offset(exception.LineNumber, exception.LinePosition);
                if (at < before.Length)
                {
                    return null;
                }
            }

            return fed;
        }

        /// <summary>
        /// Enters the start tag at the caret: its namespace declarations, then its element, with
        /// none of its attributes validated so that every one the type allows is still expected.
        /// </summary>
        public XmlSchemaElement? Enter(CaretContext caret)
        {
            Scopes.PushScope();
            foreach (var attribute in caret.Attributes)
            {
                if (attribute.Name == "xmlns")
                {
                    Scopes.AddNamespace("", attribute.Value);
                }
                else if (attribute.Name.StartsWith("xmlns:", StringComparison.Ordinal))
                {
                    Scopes.AddNamespace(attribute.Name["xmlns:".Length..], attribute.Value);
                }
            }

            var name = Qualified(caret.ElementName, Scopes, isAttribute: false);
            var info = new XmlSchemaInfo();
            Validator.ValidateElement(name.Name, name.Namespace, info);
            return info.SchemaElement;
        }

        private void Read(XmlReader reader)
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    var empty = reader.IsEmptyElement;
                    Scopes.PushScope();
                    var attributes = new List<(string Local, string Namespace, string Value)>();
                    if (reader.MoveToFirstAttribute())
                    {
                        do
                        {
                            if (reader.NamespaceURI == "http://www.w3.org/2000/xmlns/")
                            {
                                Scopes.AddNamespace(reader.Prefix == "xmlns" ? reader.LocalName : "", reader.Value);
                            }
                            else
                            {
                                attributes.Add((reader.LocalName, reader.NamespaceURI, reader.Value));
                            }
                        }
                        while (reader.MoveToNextAttribute());

                        reader.MoveToElement();
                    }

                    var info = new XmlSchemaInfo();
                    Validator.ValidateElement(reader.LocalName, reader.NamespaceURI, info);
                    foreach ((string local, string ns, string value) in attributes)
                    {
                        Validator.ValidateAttribute(local, ns, value, null);
                    }

                    Validator.ValidateEndOfAttributes(null);
                    _open.Push(info.SchemaElement);
                    if (empty)
                    {
                        End();
                    }

                    break;
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                    Validator.ValidateText(reader.Value);
                    break;
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    Validator.ValidateWhitespace(reader.Value);
                    break;
                case XmlNodeType.EndElement:
                    End();
                    break;
            }
        }

        private void End()
        {
            Validator.ValidateEndElement(null);
            Scopes.PopScope();
            _open.Pop();
        }
    }
}
