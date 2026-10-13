import { LanguageSupport } from "@codemirror/language";
import { yamlLanguage } from "@codemirror/lang-yaml";
import { styleTags, tags } from "@lezer/highlight";

/**
 * The YAML grammar, with the few tags the shared scheme has no colour for named by ones it has
 * (yaml-editor Requirement 3.1). The grammar tags a plain scalar and a block's text as content, an
 * anchor and an alias as a label, and a tag as a type name; the scheme colours none of them. A
 * plain value gets its colour from `yamlScalarColour` instead, by the type YAML 1.2 reads it as;
 * the rest are named here. Nothing in this module declares a colour.
 */
export const yamlGrammar = new LanguageSupport(
  yamlLanguage.configure({
    props: [
      styleTags({
        BlockLiteralContent: tags.string,
        "Anchor Alias": tags.attributeName,
        Tag: tags.tagName,
      }),
    ],
  }),
);
