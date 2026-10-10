import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { SearchList } from "../../../components/SearchList";
import type { TableCell, TableColumn } from "../api/tableModel";
import type { TableEditorKind } from "../definition/tableDefinition";

/** Where the focus goes when an editor closes. */
export type AfterEdit =
  /** Back to the cell that was edited. */
  | "stay"
  /** To the next cell in reading order, or the previous one. */
  | "next"
  | "previous"
  /** Nowhere: the focus already left the table, and taking it back would be rude. */
  | "leave";

export interface CellEditorProps {
  editor: Exclude<TableEditorKind, "checkbox">;
  column: TableColumn;
  /** The cell as it is, or undefined for a cell that holds nothing. */
  cell: TableCell | undefined;
  /** The character that opened the editor by being typed, which replaces what the cell held. */
  replace?: string;
  /**
   * Sends the value. Resolves to the backend's refusal as a sentence, or to `""` when the edit
   * was accepted. `settings` carries what a value alone cannot say - an option to create.
   */
  commit: (values: readonly string[], settings?: Readonly<Record<string, string>>) => Promise<string>;
  onClose: (then: AfterEdit) => void;
}

/** The browser input each written kind is edited in. Numbers are text: the backend says what a number is. */
const INPUT_TYPES = { text: "text", number: "text", date: "date", datetime: "datetime-local", time: "time" } as const;

/**
 * The editor of one cell, opened in place over it. Which editor is the kind's to say, through
 * the module's definition; a kind that names none is not edited.
 *
 * <b>A refusal keeps the editor open.</b> The value goes to the backend, and a value its type
 * cannot hold comes back as a sentence, shown here with the cell unchanged underneath.
 */
export function CellEditor(props: CellEditorProps) {
  return props.editor === "option" || props.editor === "options" || props.editor === "rows" ? <ListCellEditor {...props} /> : <InputCellEditor {...props} />;
}

function InputCellEditor({ editor, column, cell, replace, commit, onClose }: CellEditorProps) {
  const initial = cell?.values[0] ?? "";
  const [value, setValue] = useState(replace ?? initial);
  const [reason, setReason] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);
  // Set once the editor has been left for good - committed, or cancelled. The blur that follows
  // either must do nothing: after Escape in particular, a blur that committed would write the
  // very value the author just declined.
  const settledRef = useRef(false);

  useEffect(() => {
    const input = inputRef.current;
    if (input !== null) {
      input.focus();
      // Opened by Enter, the whole value is selected, ready to be replaced; opened by typing,
      // the caret follows the character typed.
      if (replace === undefined && input.type === "text") {
        input.select();
      }
    }
  }, [replace]);

  const finish = async (then: AfterEdit) => {
    if (settledRef.current) {
      return;
    }
    settledRef.current = true;

    // A value that was not changed is not an edit: nothing is sent and nothing is written.
    if (value === initial) {
      onClose(then);
      return;
    }

    const error = await commit(value === "" ? [] : [value]);
    if (error === "") {
      onClose(then);
      return;
    }

    settledRef.current = false;
    setReason(error);
    inputRef.current?.focus();
  };

  const cancel = () => {
    settledRef.current = true;
    onClose("stay");
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    // Every key typed in an editor is the editor's: the table underneath must not move on it.
    event.stopPropagation();
    if (event.key === "Enter") {
      event.preventDefault();
      void finish("stay");
    } else if (event.key === "Escape") {
      event.preventDefault();
      cancel();
    } else if (event.key === "Tab") {
      event.preventDefault();
      void finish(event.shiftKey ? "previous" : "next");
    }
  };

  return (
    <div className="table-cell-editor">
      <input
        ref={inputRef}
        className="table-cell-input"
        type={INPUT_TYPES[editor as keyof typeof INPUT_TYPES]}
        inputMode={editor === "number" ? "decimal" : undefined}
        aria-label={column.name}
        aria-invalid={reason !== "" ? true : undefined}
        value={value}
        onChange={(event) => {
          setValue(event.target.value);
          setReason("");
        }}
        onKeyDown={onKeyDown}
        onBlur={() => void finish("leave")}
      />
      {reason !== "" && (
        <p className="table-cell-refusal" role="alert">
          {reason}
        </p>
      )}
    </div>
  );
}

function ListCellEditor({ editor, column, cell, commit, onClose }: CellEditorProps) {
  const [reason, setReason] = useState("");
  const chosen = cell?.values ?? [];
  const several = editor !== "option";

  // The list of a relation is what the backend offers as the column's options: the rows that can
  // be related. A value the list does not hold keeps the label the cell gave it.
  const items = column.options.map((option) => ({ id: option.id, label: option.name }));
  for (const [index, value] of chosen.entries()) {
    if (!items.some((item) => item.id === value)) {
      items.push({ id: value, label: cell?.labels[index] || value });
    }
  }

  const send = async (values: readonly string[], settings: Readonly<Record<string, string>> | undefined, closes: boolean) => {
    const error = await commit(values, settings);
    if (error !== "") {
      setReason(error);
    } else if (closes) {
      onClose("stay");
    } else {
      setReason("");
    }
  };

  const pick = (id: string) => {
    const has = chosen.includes(id);
    if (several) {
      void send(has ? chosen.filter((value) => value !== id) : [...chosen, id], undefined, false);
    } else {
      // Picking the one that is chosen takes it away, as it does everywhere a single choice can be empty.
      void send(has ? [] : [id], undefined, true);
    }
  };

  return (
    <div
      className="table-cell-editor table-cell-list-editor"
      // A key typed in the list is the list's; Tab leaves it for the next cell.
      onKeyDown={(event) => {
        event.stopPropagation();
        if (event.key === "Tab") {
          event.preventDefault();
          onClose(event.shiftKey ? "previous" : "next");
        }
      }}
    >
      <SearchList
        label={column.name}
        items={items}
        selected={chosen}
        onPick={pick}
        // A related row is made in its own table, never from here.
        onCreate={editor === "rows" ? undefined : (text) => void send(several ? chosen : [], { newOption: text }, !several)}
        onClose={() => onClose("stay")}
      />
      {reason !== "" && (
        <p className="table-cell-refusal" role="alert">
          {reason}
        </p>
      )}
    </div>
  );
}
