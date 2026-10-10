import { useEffect, useRef, useState } from "react";
import type { TableGesture } from "../api/tableEvents";
import type { TableColumn, TableOption } from "../api/tableModel";
import { OPTION_COLORS, OptionTag } from "../cells/OptionTag";

/** How near the window's left edge the panel may stand, in pixels. */
const PANEL_MARGIN = 8;

export interface OptionsEditorProps {
  /** The column whose options are edited. */
  column: TableColumn;
  /** Where the panel stands, in the window: under the column's header. */
  position: { x: number; y: number };
  raise: (gesture: TableGesture) => void;
  onClose: () => void;
}

/**
 * A column's options, edited: each renamed, recoloured, moved up or down and deleted, and a new
 * one added. Every change is one gesture, raised as it is made; the panel shows the options the
 * model has, so it shows what was accepted and nothing it only hoped for.
 *
 * <b>A name is committed when its field is left or Enter is pressed</b>, not on every key: a
 * rename is an edit of the file, and one per letter would be a write per letter.
 */
export function OptionsEditor({ column, position, raise, onClose }: OptionsEditorProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const [adding, setAdding] = useState("");

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

  return (
    // Under its header - and inside the window when the header is scrolled out of it to the left.
    <div ref={panelRef} className="table-options-editor" role="dialog" aria-label={`Options of ${column.name}`} style={{ left: Math.max(PANEL_MARGIN, position.x), top: position.y }}>
      <ul className="table-options-list">
        {column.options.map((option, index) => (
          <OptionLine key={option.id} column={column} option={option} index={index} count={column.options.length} raise={raise} />
        ))}
      </ul>
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
        <button type="button" disabled={adding.trim() === ""} onClick={add}>
          Add
        </button>
      </div>
    </div>
  );
}

interface OptionLineProps {
  column: TableColumn;
  option: TableOption;
  index: number;
  count: number;
  raise: (gesture: TableGesture) => void;
}

function OptionLine({ column, option, index, count, raise }: OptionLineProps) {
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

  return (
    <li className="table-options-line" data-option-id={option.id}>
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
      <button type="button" className="table-options-button" aria-label={`Delete ${option.name}`} onClick={() => raise({ kind: "deleteOption", columnId: column.id, targetId: option.id })}>
        <span className="mdi mdi-trash-can-outline" aria-hidden="true" />
      </button>
    </li>
  );
}
