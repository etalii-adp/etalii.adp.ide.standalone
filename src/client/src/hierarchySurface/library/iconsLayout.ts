import { type RefObject, useLayoutEffect, useState } from "react";

/** The size of a box's controls: every icon of its list, and its delete. */
export const ICON_SIZE = 20;
/** The space between a box's border and what it holds, on every side, and between its text and its controls. */
export const BOX_SPACING = 6;
/** The space between the parts of a box's first line: the grip, the collapse control, the number and the title. */
export const HEADER_GAP = 4;
/** The width of the grip a box is dragged by. */
export const GRIP_WIDTH = 14;

/** How a box's controls stand: in a row beside its title when the whole first line fits, in a column at its right otherwise. */
export type IconsLayout = "row" | "column";

/** What the first line of a box holds, to see whether it fits on one line beside the controls. */
export interface BoxHeader {
  grip: boolean;
  number: string;
  title: string;
}

/** The fonts of a box's number and title, read once from the first box drawn: every box has the same. */
interface HeaderFonts {
  number: string;
  title: string;
}

let fonts: HeaderFonts | null = null;
let context: CanvasRenderingContext2D | null | undefined;

/** The width `text` takes in `font`, or null where nothing can measure it. */
function textWidth(text: string, font: string): number | null {
  if (context === undefined) {
    context = typeof document === "undefined" ? null : document.createElement("canvas").getContext("2d");
  }
  if (context === null) {
    return null;
  }
  context.font = font;
  return context.measureText(text).width;
}

/** The width of a box's first line on one line, or null while it cannot be measured. */
export function headerWidth(header: BoxHeader): number | null {
  if (fonts === null) {
    return null;
  }
  const number = textWidth(header.number, fonts.number);
  const title = textWidth(header.title, fonts.title);
  if (number === null || title === null) {
    return null;
  }
  return (header.grip ? GRIP_WIDTH + HEADER_GAP : 0) + ICON_SIZE + HEADER_GAP + number + HEADER_GAP + title;
}

/**
 * Whether the controls stand in a row beside the first line or in a column at the right of the box. The row is taken
 * only when the first line and every control fit side by side in the box, with the space between them; a box not yet
 * measured takes the column, which never stands over its text.
 */
export function iconsLayoutOf(boxWidth: number | null, header: number | null, icons: number): IconsLayout {
  if (boxWidth === null || header === null) {
    return "column";
  }
  return header + BOX_SPACING + icons * ICON_SIZE <= boxWidth - 2 * BOX_SPACING ? "row" : "column";
}

const watched = new WeakMap<Element, (width: number) => void>();
let observer: ResizeObserver | null = null;

/** Tells every watched box its width; one observer serves all boxes, so a page of many measures in one pass. */
function resized(entries: readonly ResizeObserverEntry[]) {
  for (const entry of entries) {
    if (fonts === null) {
      readFonts(entry.target);
    }
    watched.get(entry.target)?.(entry.target.clientWidth);
  }
}

/** Reads the fonts of the number and the title from a drawn box. */
function readFonts(box: Element) {
  const number = box.querySelector(".hs-number");
  const title = box.querySelector(".hs-title");
  if (number !== null && title !== null) {
    fonts = { number: getComputedStyle(number).font, title: getComputedStyle(title).font };
  }
}

/** The width of the box `ref` holds, inside its border, as it changes; null where it cannot be watched. */
export function useBoxWidth(ref: RefObject<HTMLElement | null>): number | null {
  const [width, setWidth] = useState<number | null>(null);
  useLayoutEffect(() => {
    const box = ref.current;
    if (box === null || typeof ResizeObserver === "undefined") {
      return;
    }
    observer ??= new ResizeObserver(resized);
    watched.set(box, setWidth);
    observer.observe(box);
    return () => {
      observer?.unobserve(box);
      watched.delete(box);
    };
  }, [ref]);
  return width;
}
