import { EditorSelection, Prec, type EditorState, type Extension, type Text } from "@codemirror/state";
import { keymap, type EditorView } from "@codemirror/view";
import { getIndentUnit, indentUnit } from "@codemirror/language";
import { indentLess, indentMore } from "@codemirror/commands";

/**
 * A keyboard that keeps a YAML document's indentation right (yaml-editor Requirement 3.5 and
 * 3.6): spaces at the document's own width, never a tab character, which YAML does not allow as
 * indentation.
 */

/** How many lines are read for the width: enough for any document's first indented line. */
const linesRead = 2000;

/** The document's indentation width: the smallest indentation of a line that has one, two when none has. */
export function indentationWidthOf(doc: Text): number {
  let smallest = 0;
  const last = Math.min(doc.lines, linesRead);
  for (let number = 1; number <= last; number++) {
    const line = doc.line(number).text;
    const width = /^ */.exec(line)![0].length;
    if (width > 0 && width < line.length && line[width] !== "#" && (smallest === 0 || width < smallest)) {
      smallest = width;
    }
  }
  return smallest === 0 ? 2 : smallest;
}

/** Tab: the selected lines one level in, or spaces from the caret to the next stop. */
function tab(view: EditorView): boolean {
  if (view.state.selection.ranges.some((range) => !range.empty)) {
    return indentMore(view);
  }
  const width = getIndentUnit(view.state);
  view.dispatch(
    view.state.changeByRange((range) => {
      const column = range.head - view.state.doc.lineAt(range.head).from;
      const spaces = " ".repeat(width - (column % width));
      return { changes: { from: range.head, insert: spaces }, range: EditorSelection.cursor(range.head + spaces.length) };
    }),
    { scrollIntoView: true, userEvent: "input.indent" },
  );
  return true;
}

/**
 * Where the line after `line` starts: at the column its content starts at - after any `- ` that
 * opens an item - and one level deeper when the line ends by opening a block: a key with nothing
 * after its colon, a lone `-`, or a block scalar's header.
 */
export function indentationAfter(line: string, width: number): number {
  const content = line.replace(/\s+#.*$/, "").trimEnd();
  const indentation = /^ */.exec(content)![0].length;
  const item = /^ *(?:- +)*/.exec(content)![0].length;
  if (content.trim() === "") {
    return /^ */.exec(line)![0].length;
  }
  if (/^ *(?:- +)*-$/.test(content)) {
    return indentation + width;
  }
  if (/:$/.test(content) || /:\s+[|>][-+0-9]*$/.test(content)) {
    return item + width;
  }
  return item;
}

function enter(view: EditorView): boolean {
  const state: EditorState = view.state;
  const width = getIndentUnit(state);
  view.dispatch(
    state.changeByRange((range) => {
      const line = state.doc.lineAt(range.from);
      const before = line.text.slice(0, range.from - line.from);
      // Trailing spaces before the caret are dropped, so the line left ends where its text does.
      const from = line.from + before.trimEnd().length;
      const insert = "\n" + " ".repeat(indentationAfter(before, width));
      return { changes: { from, to: range.to, insert }, range: EditorSelection.cursor(from + insert.length) };
    }),
    { scrollIntoView: true, userEvent: "input" },
  );
  return true;
}

export const yamlKeys: Extension = [
  indentUnit.compute(["doc"], (state) => " ".repeat(indentationWidthOf(state.doc))),
  Prec.high(
    keymap.of([
      { key: "Tab", run: tab, shift: indentLess },
      { key: "Enter", run: enter },
    ]),
  ),
];
