# XML editor

The XML editor (`editor/xml`) opens `.xml`, `.xsd`, `.xsl` and `.xslt` files that no diagram or designer claims, and every XML file through "Open as text". Its specification is [`.spec-workflow/specs/xml-editor/`](https://github.com/etalii-adp/etalii.adp.ide.standalone/tree/bd7e2ea95b8e4195bf6f8d97f7aede9ab4dc3ad8/.spec-workflow/specs/xml-editor). Colour, matched tags and folding are in the client; well-formedness, XSD and DTD validation and completion from an XSD are in the backend, on `System.Xml` and `System.Xml.Schema`, answered through the editor family's `Analyze` and `Complete` calls.

| Folder | Holds |
| --- | --- |
| `api/` | Nothing of its own: the calls are the family's, in `src/api/editors.proto` |
| `backend/` | The definition, the session, and `XmlAnalyzer` with the pieces it asks |
| `client/` | The panel, its analysis over the two calls, the prefix colour and the folds |
| `examples/` | One document per case the specification names; see its readme |

## What EDSL would have to state

EDSL, the editor specification language, is a placeholder in etalii.adp, so this editor is code and no `.eds` file is written for it (xml-editor Requirement 10.3; `EditorTests` holds that none exists). This is what an EDSL specification of it would have to say, each item beside the code that does it today. The first six are the items the JSON editor's list names; the last three are what XML adds.

| What | Here | Where |
| --- | --- | --- |
| The claims | The extensions `.xml`, `.xsd`, `.xsl`, `.xslt`, below every diagram and designer that claims the same file | [`Editor.cs`](backend/EtAlii.Adp.Editor.Xml/Editor.cs) |
| The grammar that colours | XML 1.0, as `@lezer/xml` reads it, colouring what it can still read in a document that is not well-formed | [`XmlEditorPanel.tsx`](client/XmlEditorPanel.tsx) |
| The kinds of token and their colour roles | Element name `tag`, attribute name `attribute`, attribute value, CDATA and entity reference `string`, comment `comment`, processing instruction, declaration and `DOCTYPE` `keyword`, a name's prefix the namespace role | [`editorHighlight.ts`](../../client/src/editors/editorHighlight.ts), [`xmlPrefixColour.ts`](client/xmlPrefixColour.ts) |
| Where a schema reference is read from the document | The prolog and the root's start tag only; see the three places below | [`SchemaAssociation.cs`](backend/EtAlii.Adp.Editor.Xml/SchemaAssociation.cs) |
| What findings exist, and their severities | Not well-formed: error, one, and the rest not checked. Declared encoding that differs from the file: warning. A schema reference that cannot be followed, a schema that does not compile, a schema language not checked: information, where it is named. A schema's own findings: the validator's error or warning | [`WellFormedness.cs`](backend/EtAlii.Adp.Editor.Xml/WellFormedness.cs), [`SchemaValidation.cs`](backend/EtAlii.Adp.Editor.Xml/SchemaValidation.cs), [`XmlAnalyzer.cs`](backend/EtAlii.Adp.Editor.Xml/XmlAnalyzer.cs) |
| What triggers completion | `<` in content and Ctrl+Space there; typing in a start tag after its name; typing in an attribute value or in an element's text. Nothing inside a comment, a CDATA section, an instruction, a declaration or an end tag | [`CaretContext.cs`](backend/EtAlii.Adp.Editor.Xml/CaretContext.cs), [`XmlCompletion.cs`](backend/EtAlii.Adp.Editor.Xml/XmlCompletion.cs) |
| Paired tags, and how they are closed | Every start tag has an end tag of the same name or closes itself; the partner of the tag at the caret is marked; typing the `>` of a start tag inserts its end tag, and `</` completes the open element's name, each one undo away; an element, a comment and a CDATA section fold | `@codemirror/lang-xml`, [`xmlFolding.ts`](client/xmlFolding.ts) |
| The three places a schema is named | `xsi:noNamespaceSchemaLocation`; the namespace and location pairs of `xsi:schemaLocation`; the `DOCTYPE`'s system identifier with its internal subset. An `xml-model` instruction is read only to say it is not checked | [`SchemaAssociation.cs`](backend/EtAlii.Adp.Editor.Xml/SchemaAssociation.cs) |
| The schema languages are XML's own | XSD 1.0, which validates and offers completion, and DTD, which validates only; not JSON Schema. XSD 1.1, RELAX NG and Schematron are named and not checked. A schema is read only from the project | [`SchemaCache.cs`](backend/EtAlii.Adp.Editor.Xml/SchemaCache.cs), [`ProjectXmlResolver.cs`](backend/EtAlii.Adp.Editor.Xml/ProjectXmlResolver.cs) |

Requirement 10.2: this list is posted to [etalii-adp/etalii.adp#154](https://github.com/etalii-adp/etalii.adp/issues/154#issuecomment-6144699531), the issue that collects every standalone editor's list, beside the JSON and YAML editors'.
