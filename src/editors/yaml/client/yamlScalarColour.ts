import { RangeSetBuilder, type Extension } from "@codemirror/state";
import { Decoration, ViewPlugin, type DecorationSet, type EditorView, type ViewUpdate } from "@codemirror/view";
import { syntaxTree } from "@codemirror/language";
import { tags, type Tag } from "@lezer/highlight";
import { editorHighlight } from "@client/editors/editorHighlight";

/**
 * Colours each plain scalar as the type YAML 1.2 reads it as (yaml-editor Requirement 3.2). The
 * grammar gives every unquoted scalar one tag, so it cannot tell `1.10` - a number - from `yes`,
 * which YAML 1.2 reads as a string. The patterns are the core schema's, the ones the `yaml` parser
 * resolves plain scalars by; a date is a string under it.
 *
 * The class is the shared scheme's own for the tag, so the module declares no colour (3.3). Only
 * the visible lines are read.
 */
const core: { tag: Tag; pattern: RegExp }[] = [
  { tag: tags.null, pattern: /^(?:~|null|Null|NULL)$/ },
  { tag: tags.bool, pattern: /^(?:true|True|TRUE|false|False|FALSE)$/ },
  { tag: tags.number, pattern: /^(?:[-+]?[0-9]+|0o[0-7]+|0x[0-9a-fA-F]+)$/ },
  { tag: tags.number, pattern: /^(?:[-+]?(?:\.[0-9]+|[0-9]+(?:\.[0-9]*)?)(?:[eE][-+]?[0-9]+)?|[-+]?\.(?:inf|Inf|INF)|\.(?:nan|NaN|NAN))$/ },
];

/** The tag YAML 1.2's core schema gives a plain scalar's text; a string when no rule matches. */
export function scalarTagOf(text: string): Tag {
  return core.find((rule) => rule.pattern.test(text))?.tag ?? tags.string;
}

function decorate(view: EditorView): DecorationSet {
  const builder = new RangeSetBuilder<Decoration>();
  const tree = syntaxTree(view.state);
  let last = -1;
  for (const { from, to } of view.visibleRanges) {
    tree.iterate({
      from,
      to,
      enter: (node) => {
        // A key keeps the key's colour, whatever its text would be read as as a value.
        if (node.name !== "Literal" || node.from <= last || node.node.parent?.name === "Key") {
          return;
        }
        const style = editorHighlight.style([scalarTagOf(view.state.sliceDoc(node.from, node.to).trim())]);
        if (style) {
          builder.add(node.from, node.to, Decoration.mark({ class: style }));
        }
        last = node.from;
      },
    });
  }
  return builder.finish();
}

export const yamlScalarColour: Extension = ViewPlugin.fromClass(
  class {
    decorations: DecorationSet;

    constructor(view: EditorView) {
      this.decorations = decorate(view);
    }

    update(update: ViewUpdate) {
      if (update.docChanged || update.viewportChanged || syntaxTree(update.startState) !== syntaxTree(update.state)) {
        this.decorations = decorate(update.view);
      }
    }
  },
  { decorations: (plugin) => plugin.decorations },
);
