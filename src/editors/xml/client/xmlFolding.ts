import { foldService, syntaxTree } from "@codemirror/language";

/** The delimiters each foldable node keeps visible, so a fold reads as what it hides. */
const delimiters: Record<string, readonly [string, string]> = {
  Comment: ["<!--", "-->"],
  Cdata: ["<![CDATA[", "]]>"],
};

/**
 * Folds a comment or a CDATA section that spans more than one line (xml-editor Requirement 3.7).
 * The XML grammar folds elements only; this adds the other two, from the end of the opening
 * delimiter to the start of the closing one. A fold hides text and changes none of it.
 */
export const xmlFolding = foldService.of((state, lineStart, lineEnd) => {
  let range: { from: number; to: number } | null = null;
  syntaxTree(state).iterate({
    from: lineStart,
    to: lineEnd,
    enter: (node) => {
      const delimiter = delimiters[node.name];
      if (range !== null || delimiter === undefined || node.from < lineStart || node.to <= lineEnd) {
        return;
      }

      const [open, close] = delimiter;
      const closed = state.doc.sliceString(node.to - close.length, node.to) === close;
      range = { from: node.from + open.length, to: closed ? node.to - close.length : node.to };
    },
  });
  return range;
});
