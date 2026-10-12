import { useEffect, useRef, useState } from "react";
import { usePointerGesture } from "../../../canvas/gesture/usePointerGesture";
import { ContextMenu } from "../../../shell/context/ContextMenu";
import type { TableGesture } from "../api/tableEvents";
import type { TableColumn } from "../api/tableModel";
import { kindOf, type TableDefinition } from "../definition/tableDefinition";
import { columnMenuGroups, dropIndex, resizedWidth } from "./columnActions";
import { OptionsEditor } from "./OptionsEditor";

export interface ColumnHeaderProps {
  column: TableColumn;
  /** Its place among the visible columns. */
  index: number;
  /** Its width as drawn now, in pixels. */
  width: number;
  definition: TableDefinition;
  /** False for a table that cannot be edited: the header then names its column and does nothing else. */
  editable: boolean;
  raise: (gesture: TableGesture) => void;
  /** The width a drag of the border would give, while it is dragged; null when the drag ends. */
  onResizePreview: (columnId: string, width: number | null) => void;
  /** Makes a gesture and answers with its refusal, or with nothing when it was accepted: for a name that is refused where it is typed. */
  ask?: (gesture: TableGesture) => Promise<string>;
  /** True for a column that was just added: its name is asked for at once, as if Rename had been chosen. */
  nameAtOnce?: boolean;
  /** Told when that naming has begun, so that it begins once. */
  onNamingStarted?: () => void;
}

/**
 * One column's header: its kind and its name. Pressing it opens the column's menu; dragging it
 * moves the column; dragging its right border resizes it.
 *
 * <b>A drag raises one gesture, when it ends.</b> While a border is dragged the column follows
 * the pointer as a preview the table draws, and the width is sent once on release - a resize
 * that wrote on every pointer move would be a write per pixel.
 */
export function ColumnHeader({ column, index, width, definition, editable, raise, ask, onResizePreview, nameAtOnce = false, onNamingStarted }: ColumnHeaderProps) {
  const kind = kindOf(definition, column.kind);
  const cellRef = useRef<HTMLDivElement>(null);
  const [menuAt, setMenuAt] = useState<{ x: number; y: number } | null>(null);
  const [renaming, setRenaming] = useState(false);

  // A column just added has its type's name: its own is asked for before anything else.
  useEffect(() => {
    if (nameAtOnce && editable) {
      setRenaming(true);
      onNamingStarted?.();
    }
  }, [nameAtOnce, editable, onNamingStarted]);
  // Where the panel of the column's options stands while it is open: under the header, in the window.
  const [optionsAt, setOptionsAt] = useState<{ x: number; y: number } | null>(null);

  const move = usePointerGesture<null>({
    onPress: () => {
      const rect = cellRef.current?.getBoundingClientRect();
      setMenuAt({ x: rect?.left ?? 0, y: rect?.bottom ?? 0 });
    },
    onDragEnd: (_target, dx) => {
      const cell = cellRef.current;
      const row = cell?.parentElement;
      if (cell === null || row === null || row === undefined) {
        return;
      }
      const rects = Array.from(row.querySelectorAll<HTMLElement>('[role="columnheader"]'), (header) => header.getBoundingClientRect());
      const from = rects[index];
      if (from === undefined) {
        return;
      }
      // The pointer is where it grabbed the header plus how far it went; the header's middle
      // stands in for where it grabbed, which is all a landing place needs.
      const to = dropIndex(index, (from.left + from.right) / 2 + dx, rects);
      if (to !== index) {
        raise({ kind: "moveColumn", columnId: column.id, index: to });
      }
    },
  });

  // The width the column had when its border was grabbed. The width drawn follows the pointer
  // while it is dragged, so measuring each move from the width drawn would add the distance twice.
  const grabbedWidthRef = useRef<number | null>(null);
  const resize = usePointerGesture<null>({
    onPress: () => {},
    onDragMove: (_target, dx) => {
      grabbedWidthRef.current ??= width;
      onResizePreview(column.id, resizedWidth(grabbedWidthRef.current, dx));
    },
    onDragEnd: (_target, dx) => {
      const grabbed = grabbedWidthRef.current ?? width;
      grabbedWidthRef.current = null;
      onResizePreview(column.id, null);
      const next = resizedWidth(grabbed, dx);
      if (next !== grabbed) {
        raise({ kind: "resizeColumn", columnId: column.id, settings: { width: String(next) } });
      }
    },
    onDragAbandon: () => {
      grabbedWidthRef.current = null;
      onResizePreview(column.id, null);
    },
  });

  const groups = columnMenuGroups(column, definition, {
    raise,
    startRename: () => setRenaming(true),
    editOptions: () => {
      const rect = cellRef.current?.getBoundingClientRect();
      setOptionsAt({ x: rect?.left ?? 0, y: rect?.bottom ?? 0 });
    },
  });
  const classes = ["table-header-cell", index === 0 ? "table-cell-first" : ""].filter(Boolean).join(" ");

  return (
    <div ref={cellRef} className={classes} role="columnheader" aria-colindex={index + 1} data-column-id={column.id}>
      {renaming ? (
        <RenameField
          name={column.name}
          onSubmit={(name) => {
            if (name === column.name) {
              return Promise.resolve("");
            }
            const gesture: TableGesture = { kind: "renameColumn", columnId: column.id, values: [name] };
            if (ask === undefined) {
              raise(gesture);
              return Promise.resolve("");
            }
            return ask(gesture);
          }}
          onClose={() => setRenaming(false)}
        />
      ) : editable && groups.length > 0 ? (
        <button type="button" className="table-header-button" aria-haspopup="menu" aria-expanded={menuAt !== null} {...move.press(null)}>
          <span className={`mdi ${kind.icon} table-kind-icon`} role="img" aria-label={kind.label} />
          <span className="table-header-name">{column.name}</span>
        </button>
      ) : (
        <>
          <span className={`mdi ${kind.icon} table-kind-icon`} role="img" aria-label={kind.label} />
          <span className="table-header-name">{column.name}</span>
        </>
      )}
      {editable && <span className="table-header-resize" role="separator" aria-orientation="vertical" aria-label={`Resize ${column.name}`} {...resize.press(null)} />}
      <ContextMenu open={menuAt !== null} groups={groups} position={menuAt ?? { x: 0, y: 0 }} onClose={() => setMenuAt(null)} />
      {editable && optionsAt !== null && <OptionsEditor column={column} position={optionsAt} raise={raise} onClose={() => setOptionsAt(null)} />}
    </div>
  );
}

/**
 * The column's name as a field, in the header's place. Enter keeps the name, Escape drops it, and
 * so does nothing typed. A name that is refused is refused here, under the field, which stays
 * open with what was typed; leaving it then leaves the column the name it had.
 */
function RenameField({ name, onSubmit, onClose }: { name: string; onSubmit: (name: string) => Promise<string>; onClose: () => void }) {
  const [value, setValue] = useState(name);
  const [refusal, setRefusal] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);
  // Set once the field has been left for good, so the blur that follows a key does nothing more.
  const settledRef = useRef(false);

  useEffect(() => {
    inputRef.current?.focus();
    inputRef.current?.select();
  }, []);

  const finish = (result: string | null, leaving = false) => {
    if (settledRef.current) {
      return;
    }
    settledRef.current = true;
    const trimmed = result?.trim() ?? "";
    // A name of nothing is no name, and a field left after a refusal is given up: the column keeps the name it has.
    if (trimmed === "" || (leaving && refusal !== "")) {
      onClose();
      return;
    }
    void onSubmit(trimmed).then((answer) => {
      if (answer === "") {
        onClose();
        return;
      }
      settledRef.current = false;
      setRefusal(answer);
      inputRef.current?.focus();
    });
  };

  return (
    <>
    <input
      ref={inputRef}
      className="table-header-rename"
      type="text"
      aria-label="Name"
      aria-invalid={refusal !== "" ? true : undefined}
      value={value}
      onChange={(event) => {
        setValue(event.target.value);
        setRefusal("");
      }}
      onKeyDown={(event) => {
        event.stopPropagation();
        if (event.key === "Enter") {
          event.preventDefault();
          finish(value);
        } else if (event.key === "Escape") {
          event.preventDefault();
          finish(null);
        }
      }}
      onBlur={() => finish(value, true)}
    />
    {refusal !== "" && (
      <span className="table-header-refusal" role="alert">
        {refusal}
      </span>
    )}
    </>
  );
}
