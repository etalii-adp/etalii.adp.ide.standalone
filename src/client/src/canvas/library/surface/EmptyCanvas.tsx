import type { DiagramDefinition } from "../definition/diagramDefinition";
import { resolveChromeText } from "../definition/chrome";

/**
 * The canvas's own sentence for a document that holds nothing to draw (DISL 6.13 `canvas.empty`),
 * drawn in the canvas's place from the definition's `chrome.empty`.
 *
 * The module draws this instead of the canvas, because only its stream knows whether the document has
 * been read: a document still opening holds nothing either, and saying it is empty then would be
 * wrong. What it says, and how it looks, is the library's.
 */
export function EmptyCanvas({ definition, className }: { definition: DiagramDefinition; className?: string }) {
  const empty = resolveChromeText(definition.chrome?.empty, { element: { id: "__chrome__", type: "__chrome__", x: 0, y: 0 } });
  if (empty === null) {
    return null;
  }

  return <div className={[className, "canvas-host", "canvas-host-message", empty.className].filter(Boolean).join(" ")}>{empty.text}</div>;
}
