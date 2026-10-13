import { RangeSetBuilder } from "@codemirror/state";
import { Decoration, EditorView, ViewPlugin, type DecorationSet, type ViewUpdate } from "@codemirror/view";
import { syntaxTree } from "@codemirror/language";
import { tags } from "@lezer/highlight";
import { editorHighlight } from "@client/editors/editorHighlight";

/**
 * Marks the prefix of a prefixed element or attribute name (xml-editor Requirement 3.2). The XML
 * grammar gives a name one highlight tag, prefix included, so the part before the colon is marked
 * here with the shared scheme's class for a namespace: the module still declares no colour.
 */
const prefix = Decoration.mark({ class: editorHighlight.style([tags.namespace]) ?? "" });

function prefixes(view: EditorView): DecorationSet {
  const builder = new RangeSetBuilder<Decoration>();
  for (const { from, to } of view.visibleRanges) {
    syntaxTree(view.state).iterate({
      from,
      to,
      enter: (node) => {
        if (node.name !== "TagName" && node.name !== "AttributeName") {
          return;
        }

        const colon = view.state.doc.sliceString(node.from, node.to).indexOf(":");
        if (colon > 0) {
          builder.add(node.from, node.from + colon, prefix);
        }
      },
    });
  }

  return builder.finish();
}

export const xmlPrefixColour = ViewPlugin.fromClass(
  class {
    decorations: DecorationSet;

    constructor(view: EditorView) {
      this.decorations = prefixes(view);
    }

    update(update: ViewUpdate) {
      if (update.docChanged || update.viewportChanged || syntaxTree(update.startState) !== syntaxTree(update.state)) {
        this.decorations = prefixes(update.view);
      }
    }
  },
  { decorations: (plugin) => plugin.decorations },
);
