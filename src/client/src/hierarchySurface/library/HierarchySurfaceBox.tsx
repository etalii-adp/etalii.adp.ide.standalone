import { OptionTag } from "@client/table/library/cells/OptionTag";
import type { HierarchySurfaceNode, HierarchySurfaceOptionList } from "./api/hierarchySurfaceModel";
import { CollapseToggle } from "./CollapseToggle";
import { FOCUS_ICON, HIDDEN_LEVELS_ICON, type HierarchySurfaceDefinition } from "./definition/hierarchySurfaceDefinition";
import type { NoteMark } from "./notes";

export interface HierarchySurfaceBoxProps {
  node: HierarchySurfaceNode;
  definition: HierarchySurfaceDefinition;
  optionLists: readonly HierarchySurfaceOptionList[];
  collapsed: boolean;
  selected: boolean;
  notes: NoteMark | null;
  /** How many levels below this one the page does not show; 0 when they all fit. */
  hiddenLevels: number;
  onToggle: () => void;
  onSelect: () => void;
  /** Makes this node the left edge of the page; absent for an entry. */
  onFocus?: () => void;
}

/** The colour the option list of `label` gives `value`, matched by name as a person types it. */
function colourOf(optionLists: readonly HierarchySurfaceOptionList[], label: string, value: string): string {
  const options = optionLists.find((list) => list.label === label)?.options ?? [];
  const wanted = value.toLowerCase();
  return options.find((option) => option.name.toLowerCase() === wanted || option.id.toLowerCase() === wanted)?.colour ?? "default";
}

/**
 * One node's own box: the collapse control, the number beside the title, the note mark, the tags below the title
 * and the description under them (FR-009, FR-010). A level's box also offers focus, and the mark of the levels the
 * page does not show (FR-008, R10). What stands to its right is the page's.
 */
export function HierarchySurfaceBox({ node, definition, optionLists, collapsed, selected, notes, hiddenLevels, onToggle, onSelect, onFocus }: HierarchySurfaceBoxProps) {
  const labels = definition.labels.filter((label) => label.on.includes(node.type));
  const tags = labels.flatMap((label) =>
    (node.labels.find((values) => values.label === label.label)?.values ?? []).map((value) => ({ label: label.label, value })),
  );

  return (
    <div
      className={`hs-box${selected ? " hs-selected" : ""}${collapsed ? " hs-collapsed" : ""}`}
      data-node-id={node.id}
      aria-selected={selected}
      role="treeitem"
      aria-expanded={!collapsed}
      onClick={onSelect}
    >
      <div className="hs-box-header">
        <CollapseToggle collapsed={collapsed} title={`${node.number} ${node.title}`} onToggle={onToggle} />
        <span className="hs-number">{node.number}</span>
        <span className="hs-title">{node.title}</span>
        {notes !== null && (
          <span className={`mdi ${notes.icon} hs-note-mark`} role="img" aria-label={notes.label} title={notes.label}>
            {notes.count > 1 && <span className="hs-note-count">{notes.count}</span>}
          </span>
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
      </div>
      {tags.length > 0 && (
        <div className="hs-tags">
          {tags.map((tag) => (
            <OptionTag key={`${tag.label}:${tag.value}`} label={tag.value} color={colourOf(optionLists, tag.label, tag.value)} />
          ))}
        </div>
      )}
      {!collapsed && node.description !== "" && <p className="hs-description">{node.description}</p>}
      {hiddenLevels > 0 && onFocus !== undefined && (
        <button
          type="button"
          className="hs-hidden-levels"
          aria-label={`${hiddenLevels} more ${hiddenLevels === 1 ? "level" : "levels"}: focus on ${node.number} ${node.title}`}
          title={`${hiddenLevels} more ${hiddenLevels === 1 ? "level" : "levels"}`}
          onClick={(event) => {
            event.stopPropagation();
            onFocus();
          }}
        >
          <span className={`mdi ${HIDDEN_LEVELS_ICON}`} aria-hidden="true" />
        </button>
      )}
    </div>
  );
}
