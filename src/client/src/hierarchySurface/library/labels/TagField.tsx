import { type KeyboardEvent, useEffect, useRef, useState } from "react";
import { SearchList } from "@client/components/SearchList";
import { OptionTag } from "@client/table/library/cells/OptionTag";
import { OptionsEditor } from "@client/table/library/header/OptionsEditor";
import type { HierarchySurfaceNode, HierarchySurfaceOption } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceLabelEdit } from "../definition/hierarchySurfaceDefinition";
import { listColumn, listRaise, lowerAscii as lower, valuesOf } from "./listEditing";

/** The class of the field's list, which a press outside it closes. */
export const TAG_FIELD_LIST = "hs-tag-list";

export interface TagFieldProps {
  /** The label's name, as the nodes' values name it. */
  label: string;
  edit: HierarchySurfaceLabelEdit;
  /** The node whose values these are. */
  node: HierarchySurfaceNode;
  /** The label's list: its members in their order, implicit ones included. */
  options: readonly HierarchySurfaceOption[];
  /** Every node of the page, whose values say how many carry a member a delete would take from them. */
  nodes: readonly HierarchySurfaceNode[];
  /** Whether the list is open; it opens from the values and from the box's control. */
  open: boolean;
  onOpen: () => void;
  onClose: () => void;
  /** Whether the values and the list can be changed: false while the document cannot be. */
  canEdit: boolean;
  onGesture: (operation: string, targetId: string, params: Readonly<Record<string, string>>) => unknown;
}

/**
 * A label of many values below a box's title, as the Knowledge designer's selection property (FR-011, R9): the values
 * drawn as its rounded tags, and, opened, its searchable list, where a member is picked, picked again to take it off, or
 * created from text that names none, and from where the list itself is edited in the table's options editor, reused as
 * it is: each member renamed, recoloured, put in its place and deleted. Every change is one gesture, sent as it is made;
 * what the files then say is the page's next model.
 */
export function TagField({ label, edit, node, options, nodes, open, onOpen, onClose, canEdit, onGesture }: TagFieldProps) {
  const { operations, params, words } = edit;
  const values = valuesOf(node, label);
  const [listAt, setListAt] = useState<{ x: number; y: number } | null>(null);
  const ref = useRef<HTMLDivElement>(null);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;

  const optionOf = (value: string) => options.find((option) => lower(option.name) === lower(value));
  const carries = (option: HierarchySurfaceOption) => values.some((value) => lower(value) === lower(option.name));

  // A press anywhere outside the list closes it, as leaving the table's list editor does: what was picked is written.
  useEffect(() => {
    if (!open) {
      return;
    }
    const outside = (event: PointerEvent) => {
      if (event.target instanceof Node && ref.current !== null && !ref.current.contains(event.target)) {
        closeRef.current();
      }
    };
    document.addEventListener("pointerdown", outside);
    return () => document.removeEventListener("pointerdown", outside);
  }, [open]);

  // Opened under the last box of the page, the list is brought into sight; its search has the focus, as the Knowledge
  // designer's has, so typing searches at once.
  useEffect(() => {
    if (open) {
      ref.current?.querySelector(`.${TAG_FIELD_LIST}`)?.scrollIntoView?.({ block: "nearest" });
      ref.current?.querySelector<HTMLInputElement>(`.${TAG_FIELD_LIST} input`)?.focus();
    }
  }, [open]);

  const add = (name: string) => onGesture(operations.add, node.id, { [params.name]: name });
  const remove = (name: string) => onGesture(operations.remove, node.id, { [params.name]: name });

  // The list, as the table's options editor edits a column's: one gesture for each change it raises.
  const column = listColumn(label, words.list, options, nodes);
  const raise = listRaise({ ...operations, add: undefined, params }, options, onGesture);

  // The list keeps its keys: Delete or Enter typed here is not the page's delete or add.
  const onKeyDown = (event: KeyboardEvent) => event.stopPropagation();

  return (
    <div ref={ref} className="hs-tag-field">
      {values.length > 0 &&
        (canEdit ? (
          // Pressing the values opens the list, and selects the box as any press on it does.
          <span
            className="hs-tag-values"
            role="button"
            tabIndex={0}
            aria-label={`${words.list} of ${node.number}: ${values.join(", ")}`}
            onClick={onOpen}
            onKeyDown={(event) => {
              if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                event.stopPropagation();
                onOpen();
              }
            }}
          >
            {values.map((value) => (
              <OptionTag key={value} label={value} color={optionOf(value)?.colour ?? "default"} />
            ))}
          </span>
        ) : (
          values.map((value) => <OptionTag key={value} label={value} color={optionOf(value)?.colour ?? "default"} />)
        ))}
      {open && (
        <div
          className={TAG_FIELD_LIST}
          role="dialog"
          aria-label={`${words.list} of ${node.number}`}
          onKeyDown={onKeyDown}
          onClick={(event) => event.stopPropagation()}
          onDoubleClick={(event) => event.stopPropagation()}
        >
          {values.length > 0 && (
            <div className="hs-tag-chosen">
              {values.map((value) => (
                <span key={value} className="hs-tag-chosen-value">
                  <OptionTag label={value} color={optionOf(value)?.colour ?? "default"} />
                  <button type="button" className="mdi mdi-close hs-tag-remove" aria-label={`${words.remove} ${value}`} title={words.remove} onClick={() => remove(value)} />
                </span>
              ))}
            </div>
          )}
          <SearchList
            label={`${words.add} to ${node.number}`}
            items={options.map((option) => ({ id: option.id, label: option.name }))}
            selected={options.filter(carries).map((option) => option.id)}
            placeholder="Search or create…"
            onPick={(id) => {
              const option = options.find((candidate) => candidate.id === id);
              if (option !== undefined) {
                void (carries(option) ? remove(values.find((value) => lower(value) === lower(option.name)) ?? option.name) : add(option.name));
              }
            }}
            onCreate={(text) => void add(text)}
            onClose={onClose}
          />
          <button
            type="button"
            className="hs-tag-edit-list"
            onClick={(event) => {
              // The editor takes the list's place. The field itself draws no box of its own to measure, and the editor is
              // laid out against the window, so the place is the list's on the screen.
              const rect = ref.current?.querySelector(`.${TAG_FIELD_LIST}`)?.getBoundingClientRect();
              setListAt({ x: rect?.left ?? event.clientX, y: rect?.top ?? event.clientY });
              onClose();
            }}
          >
            <span className="mdi mdi-pencil-outline" aria-hidden="true" />
            <span>Edit {words.list.toLowerCase()}</span>
          </button>
        </div>
      )}
      {listAt !== null && (
        <OptionsEditor
          column={column}
          position={listAt}
          raise={raise}
          onClose={() => setListAt(null)}
          addable={false}
          deleteQuestion={(option, uses) => edit.deletion.message(option.name, uses)}
        />
      )}
    </div>
  );
}
