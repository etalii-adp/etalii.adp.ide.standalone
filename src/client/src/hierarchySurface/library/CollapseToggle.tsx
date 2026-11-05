export interface CollapseToggleProps {
  collapsed: boolean;
  /** The text that names the node, read out with the toggle. */
  title: string;
  onToggle: () => void;
}

/**
 * The control to the left of every title (FR-010), in the shape of the table's `NestedRowToggle`: it folds the node
 * to its number, title, tags and note mark, and brings it back.
 */
export function CollapseToggle({ collapsed, title, onToggle }: CollapseToggleProps) {
  return (
    <button
      type="button"
      className="hs-toggle"
      aria-expanded={!collapsed}
      aria-label={`${collapsed ? "Expand" : "Collapse"} ${title}`}
      // The toggle is not the box: pressing it must not select the node as well.
      onClick={(event) => {
        event.stopPropagation();
        onToggle();
      }}
    >
      <span className={`mdi ${collapsed ? "mdi-chevron-right" : "mdi-chevron-down"}`} aria-hidden="true" />
    </button>
  );
}
