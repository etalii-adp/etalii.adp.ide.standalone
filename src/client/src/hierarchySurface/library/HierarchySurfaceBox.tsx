import { type ReactNode, useState } from "react";
import type { PointerPressWiring } from "@client/canvas/gesture/usePointerGesture";
import { OptionTag } from "@client/table/library/cells/OptionTag";
import type { HierarchySurfaceNode, HierarchySurfaceOptionList } from "./api/hierarchySurfaceModel";
import { ClassificationPicker } from "./classification/ClassificationPicker";
import { CollapseToggle } from "./CollapseToggle";
import {
  DELETE_ICON,
  FOCUS_ICON,
  HIDDEN_LEVELS_ICON,
  type HierarchySurfaceDefinition,
  type HierarchySurfaceLabelDefinition,
} from "./definition/hierarchySurfaceDefinition";
import type { EntryEditing } from "./editing/entryEditing";
import { EntryTextField } from "./editing/EntryTextField";
import { valuesOf } from "./labels/listEditing";
import { TagField } from "./labels/TagField";
import { NoteIcon } from "./notes/NoteIcon";
import type { NoteMark } from "./notes/noteMark";
import { ReferenceText } from "./references/ReferenceBox";

export interface HierarchySurfaceBoxProps {
  node: HierarchySurfaceNode;
  definition: HierarchySurfaceDefinition;
  optionLists: readonly HierarchySurfaceOptionList[];
  collapsed: boolean;
  selected: boolean;
  notes: NoteMark | null;
  /** Whether the list of its notes is open. */
  notesOpen: boolean;
  onToggleNotes: () => void;
  /** The list of its notes while it is open; absent otherwise. */
  noteList?: ReactNode;
  /** How many levels below this one the page does not show; 0 when they all fit. */
  hiddenLevels: number;
  onToggle: () => void;
  onSelect: () => void;
  /** Makes this node the left edge of the page; absent for an entry. */
  onFocus?: () => void;
  /** Shows the levels below this one that the page does not show; absent for an entry. */
  onShowHidden?: () => void;
  /** Asks to delete this node, with everything in it; absent when the document cannot be changed. */
  onDelete?: () => void;
  /** Its title and description typed in place; absent when the document cannot be changed. */
  editing?: EntryEditing;
  /** The wiring of the grip it is dragged by; absent when the document cannot be changed. */
  grip?: PointerPressWiring;
  /** The node of an id, for the boxes of the references in its description. */
  nodeOf: (id: string) => HierarchySurfaceNode | undefined;
  /** Shows the node a reference box names (FR-018). */
  onReveal: (id: string) => void;
  /** Where a node dragged over this box would go, or absent. */
  dropHint?: "into" | "after" | "before";
  /** Every node of the page, which a label's list counts the carriers of a member among. */
  nodes: readonly HierarchySurfaceNode[];
  /** Sends a gesture of a label's field or list; absent when the document cannot be changed. */
  onLabelGesture?: (operation: string, targetId: string, params: Readonly<Record<string, string>>) => unknown;
}

/** The class of a label's look: its own, or a tag for a label of many values and a badge for one of one. */
function lookOf(label: HierarchySurfaceLabelDefinition): string {
  return `hs-look hs-look-${label.look ?? (label.many ? "tag" : "badge")}`;
}

/** The colour the option list of `label` gives `value`, matched by name as a person types it. */
function colourOf(optionLists: readonly HierarchySurfaceOptionList[], label: string, value: string): string {
  const options = optionLists.find((list) => list.label === label)?.options ?? [];
  const wanted = value.toLowerCase();
  return options.find((option) => option.name.toLowerCase() === wanted || option.id.toLowerCase() === wanted)?.colour ?? "default";
}

/**
 * One node's own box: the collapse control, the number beside the title, the tags below the title and the description
 * under them (FR-009, FR-010), and at its top right the icon of its notes, which opens their list (FR-015, FR-016). A
 * label the definition lets the page change is a field there, opened from its values or the control in the header
 * (FR-011). A level's box also offers focus, and the mark of the levels the page does not show (FR-008, R10). What
 * stands to its right is the page's.
 *
 * The box's controls stand in one group at its top right, laid out from right to left with the icon of the notes first,
 * so it stays in sight whatever else is offered, wrapping to a next line rather than over the title, and the title,
 * tags and description flow beside them. Everything in the header is aligned to the top, so a title of several lines
 * leaves the rest on its first. A selected box offers its delete at its bottom right.
 */
export function HierarchySurfaceBox(props: HierarchySurfaceBoxProps) {
  const { node, definition, optionLists, collapsed, selected, notes, notesOpen, onToggleNotes, noteList, hiddenLevels, onToggle, onSelect, onFocus, onShowHidden, onDelete, editing, grip, dropHint, nodeOf, onReveal, nodes, onLabelGesture } = props;
  const [fieldOpen, setFieldOpen] = useState(false);
  const allLabels = definition.labels.filter((label) => label.on.includes(node.type));
  // The label of many values the page changes is its field; the others are drawn after it, as they are.
  const field = allLabels.find((label) => label.many && label.edit !== undefined);
  const fieldEdit = field?.edit;
  const labels = allLabels.filter((label) => label !== field);
  const text = editing?.field ?? null;
  const fieldOf = (attribute: "title" | "description") =>
    editing !== undefined && text !== null && text.attribute === attribute ? (
      <EntryTextField
        text={text.text}
        multiline={attribute === "description"}
        label={text.label}
        onChange={editing.change}
        onCommit={editing.commit}
        onCancel={editing.cancel}
        references={attribute === "description" ? editing.references : undefined}
      />
    ) : null;
  const shown = labels.filter((label) => label.pick === undefined);
  const tags = shown.flatMap((label) => valuesOf(node, label.label).map((value) => ({ label: label.label, value, look: lookOf(label) })));
  // A label of one value the page changes is its picker: its value, or on the selected box the way to give one.
  const picks = labels.filter((label) => label.pick !== undefined && (valuesOf(node, label.label).length > 0 || (selected && onLabelGesture !== undefined)));

  return (
    <div
      className={`hs-box${grip === undefined ? "" : " hs-draggable"}${selected ? " hs-selected" : ""}${selected && onDelete !== undefined ? " hs-deletable" : ""}${collapsed ? " hs-collapsed" : ""}${dropHint === undefined ? "" : ` hs-drop-${dropHint}`}`}
      data-node-id={node.id}
      aria-selected={selected}
      role="treeitem"
      aria-expanded={!collapsed}
      onClick={onSelect}
    >
      <span className="hs-box-icons">
        <NoteIcon
          mark={notes}
          number={node.number}
          addLabel={definition.notes.words.add}
          canAdd={editing !== undefined}
          open={notesOpen}
          onToggle={onToggleNotes}
        />
        {hiddenLevels > 0 && onShowHidden !== undefined && (
          <button
            type="button"
            className="hs-hidden-levels"
            aria-label={`${hiddenLevels} more ${hiddenLevels === 1 ? "level" : "levels"}: show the levels of ${node.number} ${node.title}`}
            title={`${hiddenLevels} more ${hiddenLevels === 1 ? "level" : "levels"}`}
            onClick={(event) => {
              event.stopPropagation();
              onShowHidden();
            }}
          >
            <span className={`mdi ${HIDDEN_LEVELS_ICON}`} aria-hidden="true" />
          </button>
        )}
        {onFocus !== undefined && (
          <button
            type="button"
            className="hs-focus"
            aria-label={`Focus on ${node.number} ${node.title}`}
            title="Focus on this item"
            onClick={(event) => {
              event.stopPropagation();
              onFocus();
            }}
          >
            <span className={`mdi ${FOCUS_ICON}`} aria-hidden="true" />
          </button>
        )}
        {field !== undefined && fieldEdit !== undefined && onLabelGesture !== undefined && (
          // Shown under the pointer or on the selected box, and kept in its place otherwise: it moves nothing a click aims at.
          <button
            type="button"
            className="mdi mdi-tag-plus-outline hs-tag-add"
            aria-label={`${fieldEdit.words.add} to ${node.number}`}
            title={fieldEdit.words.add}
            onClick={(event) => {
              event.stopPropagation();
              onSelect();
              setFieldOpen(true);
            }}
          />
        )}
        {selected && !collapsed && node.description === "" && editing !== undefined && editing.field === null && (
          // Last of the controls, on the selected box: showing it moves the title and the tags nothing.
          <button
            type="button"
            className="mdi mdi-text-box-plus-outline hs-add-description"
            aria-label="Add a description"
            title="Add a description"
            onClick={(event) => {
              event.stopPropagation();
              editing.open("description");
            }}
          />
        )}
      </span>
      <div className="hs-box-header">
        {grip !== undefined && <span className="mdi mdi-drag-vertical hs-grip" title={`Drag ${node.number}`} aria-hidden="true" {...grip} />}
        <CollapseToggle collapsed={collapsed} title={`${node.number} ${node.title}`} onToggle={onToggle} />
        <span className="hs-number">{node.number}</span>
        {fieldOf("title") ?? (
          <span className="hs-title" onDoubleClick={editing === undefined ? undefined : () => editing.open("title")}>
            {node.title}
          </span>
        )}
      </div>
      {(tags.length > 0 || picks.length > 0 || (field !== undefined && (valuesOf(node, field.label).length > 0 || fieldOpen))) && (
        <div className="hs-tags">
          {field !== undefined && fieldEdit !== undefined && (
            <span className={lookOf(field)}>
              <TagField
                label={field.label}
                edit={fieldEdit}
                node={node}
                options={optionLists.find((list) => list.label === field.label)?.options ?? []}
                nodes={nodes}
                open={fieldOpen && onLabelGesture !== undefined}
                onOpen={() => setFieldOpen(true)}
                onClose={() => setFieldOpen(false)}
                canEdit={onLabelGesture !== undefined}
                onGesture={onLabelGesture ?? (() => "")}
              />
            </span>
          )}
          {tags.map((tag) => (
            <span key={`${tag.label}:${tag.value}`} className={tag.look}>
              <OptionTag label={tag.value} color={colourOf(optionLists, tag.label, tag.value)} />
            </span>
          ))}
          {picks.map((label) => (
            <span key={label.label} className={lookOf(label)}>
              <ClassificationPicker
                label={label.label}
                pick={label.pick!}
                node={node}
                options={optionLists.find((list) => list.label === label.label)?.options ?? []}
                nodes={nodes}
                canEdit={onLabelGesture !== undefined}
                onGesture={onLabelGesture ?? (() => "")}
              />
            </span>
          ))}
        </div>
      )}
      {!collapsed &&
        (fieldOf("description") ??
          (node.description !== "" && (
            <p className="hs-description" onDoubleClick={editing === undefined ? undefined : () => editing.open("description")}>
              <ReferenceText text={node.description} references={node.references} nodeOf={nodeOf} onReveal={onReveal} />
            </p>
          )))}
      {noteList}
      {selected && onDelete !== undefined && (
        <button
          type="button"
          className={`mdi ${DELETE_ICON} hs-delete`}
          aria-label={`Delete ${node.number} ${node.title}`}
          title="Delete"
          onClick={(event) => {
            event.stopPropagation();
            onDelete();
          }}
        />
      )}
    </div>
  );
}
