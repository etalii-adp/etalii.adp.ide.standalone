import { useEffect, useRef, useState, type CSSProperties, type HTMLAttributes } from "react";
import { createPortal } from "react-dom";
import { usePointerGesture } from "../../../canvas/gesture/usePointerGesture";
import type { TableGesture } from "../api/tableEvents";
import type { TableColumn, TableOption } from "../api/tableModel";
import { OPTION_COLORS, OptionTag } from "../cells/OptionTag";
import { dropIndex } from "./columnActions";

/** The setting a column says under how many rows have one of its options. */
export const USES_PREFIX = "uses:";

/** How near the window's left edge the panel may stand, in pixels. */
const PANEL_MARGIN = 8;

export interface OptionsEditorProps {
  /** The column whose options are edited. */
  column: TableColumn;
  /** Where the panel stands, in the window: under the column's header. */
  position: { x: number; y: number };
  raise: (gesture: TableGesture) => void;
  onClose: () => void;
  /** Whether a new option can be added from the panel; true for a column, whose options are made here. */
  addable?: boolean;
  /** What deleting an option some rows have asks, for the option and how many have it; the table's words when absent. */
  deleteQuestion?: (option: TableOption, uses: number) => string;
}

/**
 * A column's options, edited: each renamed, recoloured, moved up or down and deleted, and a new
 * one added. Every change is one gesture, raised as it is made; the panel shows the options the
 * model has, so it shows what was accepted and nothing it only hoped for.
 *
 * <b>A name is committed when its field is left or Enter is pressed</b>, not on every key: a
 * rename is an edit of the file, and one per letter would be a write per letter.
 */
export function OptionsEditor({ column, position, raise, onClose, addable = true, deleteQuestion = tableDeleteQuestion }: OptionsEditorProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const [adding, setAdding] = useState("");
  const listRef = useRef<HTMLUListElement>(null);
  // The option being dragged, how far, and the place it would be dropped at, while a drag lasts.
  const [moving, setMoving] = useState<{ from: number; to: number; dy: number; step: number } | null>(null);

  /** Where an option dragged `dy` pixels from its line would go, and how far apart the lines stand; null without its line. */
  const placeOf = (option: TableOption, dy: number) => {
    // Where the lines stand in the list, which the shifts a drag shows leave alone, unlike their place on the screen.
    const lines = Array.from(listRef.current?.querySelectorAll<HTMLElement>(":scope > li") ?? [], (line) => ({ left: line.offsetTop, right: line.offsetTop + line.offsetHeight }));
    const from = column.options.findIndex((candidate) => candidate.id === option.id);
    const line = lines[from];
    if (line === undefined) {
      return null;
    }
    const next = lines[from + 1] ?? lines[from - 1];
    const step = next === undefined ? line.right - line.left : Math.abs(next.left - line.left);
    return { from, to: dropIndex(from, (line.left + line.right) / 2 + dy, lines), dy, step };
  };

  // An option is put in its place by dragging its handle: one gesture, when the drag ends, as a sort's is. While it is
  // dragged, it follows the pointer and the others make way for it, as the list would stand once it is dropped.
  const drag = usePointerGesture<TableOption>({
    onPress: () => {},
    onDragMove: (option, _dx, dy) => setMoving(placeOf(option, dy)),
    onDragEnd: (option, _dx, dy) => {
      setMoving(null);
      const place = placeOf(option, dy);
      if (place !== null && place.to !== place.from) {
        raise({ kind: "moveOption", columnId: column.id, targetId: option.id, index: place.to });
      }
    },
    onDragAbandon: () => setMoving(null),
  });

  /** How a line stands while an option is dragged: the dragged one under the pointer, the ones it passed a line further. */
  const shiftOf = (index: number): CSSProperties | undefined => {
    if (moving === null) {
      return undefined;
    }
    const { from, to, dy, step } = moving;
    if (index === from) {
      return { transform: `translateY(${dy}px)`, zIndex: 1, position: "relative" };
    }
    const shift = from < to && index > from && index <= to ? -step : from > to && index >= to && index < from ? step : 0;
    return { transform: `translateY(${shift}px)`, transition: "transform 0.12s ease" };
  };

  // Leaving the panel - a press anywhere else, or Escape - closes it.
  useEffect(() => {
    const onPress = (event: PointerEvent) => {
      if (panelRef.current !== null && event.target instanceof Node && !panelRef.current.contains(event.target)) {
        onClose();
      }
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose();
      }
    };
    document.addEventListener("pointerdown", onPress);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("pointerdown", onPress);
      document.removeEventListener("keydown", onKey);
    };
  }, [onClose]);

  const add = () => {
    const name = adding.trim();
    if (name !== "") {
      raise({ kind: "addOption", columnId: column.id, values: [name] });
      setAdding("");
    }
  };

  // Drawn at the document's top, as a menu is: inside its header it is painted in the header's
  // layer, under the rows that scroll past and the frozen cells beside it.
  return createPortal(
    // Under its header - and inside the window when the header is scrolled out of it to the left.
    <div ref={panelRef} className="table-options-editor" role="dialog" aria-label={`Options of ${column.name}`} style={{ left: Math.max(PANEL_MARGIN, position.x), top: position.y }}>
      <ul className="table-options-list" ref={listRef}>
        {column.options.map((option, index) => (
          <OptionLine
            key={option.id}
            column={column}
            option={option}
            index={index}
            count={column.options.length}
            raise={raise}
            handle={drag.press(option)}
            deleteQuestion={deleteQuestion}
            style={shiftOf(index)}
            dragging={moving?.from === index}
          />
        ))}
      </ul>
      {addable && (
        <div className="table-options-add">
          <input
            type="text"
            aria-label="New option"
            placeholder="Add an option…"
            value={adding}
            onChange={(event) => setAdding(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter") {
                event.preventDefault();
                add();
              }
            }}
          />
          <button type="button" className="dialog-button dialog-button-primary table-options-add-button" disabled={adding.trim() === ""} onClick={add}>
            Add
          </button>
        </div>
      )}
    </div>,
    document.body,
  );
}

interface OptionLineProps {
  column: TableColumn;
  option: TableOption;
  /** What makes the line's handle start a drag of it. */
  handle: HTMLAttributes<HTMLElement>;
  index: number;
  count: number;
  raise: (gesture: TableGesture) => void;
  deleteQuestion: (option: TableOption, uses: number) => string;
  /** Where it stands while an option is dragged. */
  style?: CSSProperties;
  /** Whether it is the option being dragged. */
  dragging: boolean;
}

/** What deleting an option of a table's column asks. */
function tableDeleteQuestion(_option: TableOption, uses: number): string {
  return uses === 1 ? "1 row has this option. Deleting it clears that value." : `${uses} rows have this option. Deleting it clears those values.`;
}

function OptionLine({ column, option, index, count, raise, handle, deleteQuestion, style, dragging }: OptionLineProps) {
  const [name, setName] = useState(option.name);

  // The model's name is the truth: a rename that was refused, or made elsewhere, shows here.
  useEffect(() => setName(option.name), [option.name]);

  const rename = () => {
    const next = name.trim();
    if (next === "" || next === option.name) {
      setName(option.name);
      return;
    }
    raise({ kind: "renameOption", columnId: column.id, targetId: option.id, values: [next] });
  };

  const move = (to: number) => raise({ kind: "moveOption", columnId: column.id, targetId: option.id, index: to });

  // How many rows have the option, as the column says it; nothing said is none.
  const uses = Number(column.settings[`${USES_PREFIX}${option.id}`] ?? "0") || 0;
  const [asking, setAsking] = useState(false);
  const remove = () => {
    setAsking(false);
    raise({ kind: "deleteOption", columnId: column.id, targetId: option.id });
  };

  return (
    <li className={`table-options-line${dragging ? " table-options-dragging" : ""}`} data-option-id={option.id} style={style}>
      <span className="mdi mdi-drag-vertical table-sort-handle" role="img" aria-label={`Move option ${option.name}`} {...handle} />
      <OptionTag label={option.name} color={option.color} />
      <input
        type="text"
        aria-label={`Name of ${option.name}`}
        value={name}
        onChange={(event) => setName(event.target.value)}
        onBlur={rename}
        onKeyDown={(event) => {
          if (event.key === "Enter") {
            event.preventDefault();
            rename();
          }
        }}
      />
      <select
        aria-label={`Colour of ${option.name}`}
        value={(OPTION_COLORS as readonly string[]).includes(option.color) ? option.color : "default"}
        onChange={(event) => raise({ kind: "recolourOption", columnId: column.id, targetId: option.id, settings: { colour: event.target.value } })}
      >
        {OPTION_COLORS.map((color) => (
          <option key={color} value={color}>
            {color.charAt(0).toUpperCase() + color.slice(1)}
          </option>
        ))}
      </select>
      <button type="button" className="table-options-button" aria-label={`Move ${option.name} up`} disabled={index === 0} onClick={() => move(index - 1)}>
        <span className="mdi mdi-chevron-up" aria-hidden="true" />
      </button>
      <button type="button" className="table-options-button" aria-label={`Move ${option.name} down`} disabled={index === count - 1} onClick={() => move(index + 1)}>
        <span className="mdi mdi-chevron-down" aria-hidden="true" />
      </button>
      <button type="button" className="table-options-button" aria-label={`Delete ${option.name}`} onClick={() => (uses > 0 ? setAsking(true) : remove())}>
        <span className="mdi mdi-trash-can-outline" aria-hidden="true" />
      </button>
      {asking && (
        // An option some rows have is not deleted on one press: its author is told how many first.
        <div className="table-options-ask" role="alertdialog" aria-label={`Delete ${option.name}?`}>
          <span>{deleteQuestion(option, uses)}</span>
          <button type="button" className="dialog-button dialog-button-danger table-options-add-button" onClick={remove}>
            Delete
          </button>
          <button type="button" className="dialog-button dialog-button-neutral table-options-add-button" onClick={() => setAsking(false)}>
            Keep
          </button>
        </div>
      )}
    </li>
  );
}
