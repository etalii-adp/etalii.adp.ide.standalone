/**
 * The lines of a long YAML document a completion needs (yaml-editor Performance: a list within
 * 300 milliseconds of being asked for on a 5,000-line document). The service validates the whole
 * document to find the schemas that match at the caret, so its time grows with the document; what
 * decides a completion is only the caret's line, the keys beside it in its own mapping, and the
 * line of each collection it is in. Those lines are kept and every other is left out.
 *
 * Only a document longer than {@link windowAbove} lines is cut down; a shorter one is answered
 * whole, so the ordinary case is the service's own reading. Flow collections across lines are not
 * followed, which in a document that long only costs an offer, never the text.
 */

/** The length past which a completion reads the window instead of the whole document. */
export const windowAbove = 300;

export interface CompletionWindow {
  /** The kept lines, in their order, joined by line feeds. */
  text: string;
  /** For each kept line, its index in the document. */
  lines: number[];
  /** The caret's line in {@link text}. */
  caretLine: number;
}

/** Leading spaces. */
function indentOf(line: string): number {
  return /^ */.exec(line)![0].length;
}

/** The column a line's content starts at, past any `- ` that opens an item. */
function contentOf(line: string): number {
  return /^ *(?:- +)*/.exec(line)![0].length;
}

function isContent(line: string): boolean {
  const trimmed = line.trim();
  return trimmed !== "" && !trimmed.startsWith("#");
}

/** The window for a caret on line `caret` of `lines`, at column `column`; null when the document is short enough to read whole. */
export function completionWindow(lines: readonly string[], caret: number, column: number): CompletionWindow | null {
  if (lines.length <= windowAbove) {
    return null;
  }

  const caretText = lines[caret];
  // On a line with nothing before the caret, the caret's column is where the new key goes.
  const target = caretText.slice(0, column).trim() === "" ? column : contentOf(caretText);
  const kept = new Set<number>([caret]);

  // A key is kept with the first line of its value: a key left with nothing under it would read as
  // empty, and the service reads the line after an empty key as that key's value.
  const keepKey = (index: number) => {
    kept.add(index);
    for (let child = index + 1; child < lines.length && child !== caret; child++) {
      if (isContent(lines[child])) {
        if (indentOf(lines[child]) > indentOf(lines[index])) {
          kept.add(child);
        }
        return;
      }
    }
  };

  // Up: the keys of the caret's own mapping, then each line that opens a collection around it.
  let level = target;
  let siblings = true;
  for (let index = caret - 1; index >= 0 && (siblings || level > 0); index--) {
    const line = lines[index];
    if (/^(?:---|%)/.test(line)) {
      break;
    }
    if (!isContent(line)) {
      continue;
    }
    const indent = indentOf(line);
    if (indent > level || (indent === level && !siblings)) {
      continue;
    }
    if (indent === level) {
      // A key beside the caret's is needed, so it is not offered again; an item of a sequence is not.
      if (contentOf(line) === indent) {
        keepKey(index);
      }
      continue;
    }
    // Shallower: the line that opens an item this mapping starts on, or the collection's own key.
    kept.add(index);
    siblings = false;
    level = indent;
  }

  // Down: the keys after the caret in the same mapping, up to where it ends.
  for (let index = caret + 1; index < lines.length; index++) {
    const line = lines[index];
    if (/^(?:---|\.\.\.)/.test(line)) {
      break;
    }
    if (!isContent(line)) {
      continue;
    }
    const indent = indentOf(line);
    if (indent < target || (indent === target && contentOf(line) !== indent)) {
      break;
    }
    if (indent === target) {
      keepKey(index);
    }
  }

  // The document's own start: its marker and directives, so the window reads as the same document.
  for (let index = 0; index < caret; index++) {
    if (/^(?:---|%)/.test(lines[index])) {
      kept.add(index);
    } else if (isContent(lines[index])) {
      break;
    }
  }

  const order = [...kept].sort((a, b) => a - b);
  return { text: order.map((index) => lines[index]).join("\n") + "\n", lines: order, caretLine: order.indexOf(caret) };
}
