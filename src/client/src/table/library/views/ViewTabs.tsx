import { useEffect, useRef, useState } from "react";
import { usePointerGesture } from "../../../canvas/gesture/usePointerGesture";
import { ContextMenu } from "../../../shell/context/ContextMenu";
import type { TableGesture } from "../api/tableEvents";
import type { TableView } from "../api/tableModel";
import { dropIndex } from "../header/columnActions";

export interface ViewTabsProps {
  views: readonly TableView[];
  /** The view this tab of the application shows. */
  activeViewId: string;
  /** False for a table that cannot be edited: the tabs then only switch. */
  editable: boolean;
  onView?: (viewId: string) => void;
  raise: (gesture: TableGesture) => void;
}

/**
 * The table's views, as tabs above it. Pressing a tab shows that view; pressing the tab of the
 * view already shown opens its menu - rename, duplicate, delete; dragging a tab moves the view;
 * and a + adds one.
 *
 * Switching is not an edit: which view a tab of the application shows is that connection's, and
 * goes out through `onView`. Everything else here changes the document and is raised as a gesture.
 */
export function ViewTabs({ views, activeViewId, editable, onView, raise }: ViewTabsProps) {
  const listRef = useRef<HTMLDivElement>(null);
  const [menu, setMenu] = useState<{ viewId: string; x: number; y: number } | null>(null);
  const [renaming, setRenaming] = useState<string | null>(null);

  const press = usePointerGesture<TableView>({
    onPress: (view) => {
      if (view.id !== activeViewId) {
        onView?.(view.id);
        return;
      }
      if (editable) {
        const tabs = Array.from(listRef.current?.querySelectorAll<HTMLElement>(`[role="tab"]`) ?? []);
        const rect = tabs.find((candidate) => candidate.dataset.viewId === view.id)?.getBoundingClientRect();
        setMenu({ viewId: view.id, x: rect?.left ?? 0, y: rect?.bottom ?? 0 });
      }
    },
    onDragEnd: (view, dx) => {
      if (!editable) {
        return;
      }
      const tabs = Array.from(listRef.current?.querySelectorAll<HTMLElement>('[role="tab"]') ?? [], (tab) => tab.getBoundingClientRect());
      const from = views.findIndex((candidate) => candidate.id === view.id);
      const rect = tabs[from];
      if (rect === undefined) {
        return;
      }
      const to = dropIndex(from, (rect.left + rect.right) / 2 + dx, tabs);
      if (to !== from) {
        raise({ kind: "moveView", viewId: view.id, index: to });
      }
    },
  });

  const menuView = views.find((view) => view.id === menu?.viewId);

  return (
    <div className="table-view-tabs" role="tablist" aria-label="Views" ref={listRef}>
      {views.map((view) =>
        renaming === view.id ? (
          <ViewNameField
            key={view.id}
            name={view.name}
            onDone={(name) => {
              setRenaming(null);
              if (name !== null && name !== view.name) {
                raise({ kind: "renameView", viewId: view.id, values: [name] });
              }
            }}
          />
        ) : (
          <button
            key={view.id}
            type="button"
            role="tab"
            className={`table-view-tab${view.id === activeViewId ? " table-view-tab-active" : ""}`}
            aria-selected={view.id === activeViewId}
            data-view-id={view.id}
            {...press.press(view)}
          >
            {view.name}
          </button>
        ),
      )}
      {editable && (
        <button type="button" className="table-view-add" aria-label="Add a view" onClick={() => raise({ kind: "addView" })}>
          <span className="mdi mdi-plus" aria-hidden="true" />
        </button>
      )}
      <ContextMenu
        open={menu !== null && menuView !== undefined}
        position={menu ?? { x: 0, y: 0 }}
        onClose={() => setMenu(null)}
        groups={
          menuView === undefined
            ? []
            : [
                [
                  { id: "rename", label: "Rename", icon: "mdi-pencil-outline", onSelect: () => setRenaming(menuView.id) },
                  { id: "duplicate", label: "Duplicate", icon: "mdi-content-duplicate", onSelect: () => raise({ kind: "duplicateView", viewId: menuView.id }) },
                  {
                    id: "delete",
                    label: "Delete",
                    icon: "mdi-trash-can-outline",
                    // A table always has a view to be looked at through.
                    disabled: views.length <= 1,
                    disabledReason: views.length <= 1 ? "A table keeps at least one view." : undefined,
                    onSelect: () => raise({ kind: "deleteView", viewId: menuView.id }),
                  },
                ],
              ]
        }
      />
    </div>
  );
}

/** A view's name as a field, in its tab's place. Enter keeps the name; Escape, or a name of nothing, keeps the old one. */
function ViewNameField({ name, onDone }: { name: string; onDone: (name: string | null) => void }) {
  const [value, setValue] = useState(name);
  const inputRef = useRef<HTMLInputElement>(null);
  const settledRef = useRef(false);

  useEffect(() => {
    inputRef.current?.focus();
    inputRef.current?.select();
  }, []);

  const finish = (result: string | null) => {
    if (settledRef.current) {
      return;
    }
    settledRef.current = true;
    const trimmed = result?.trim() ?? null;
    onDone(trimmed === "" ? null : trimmed);
  };

  return (
    <input
      ref={inputRef}
      className="table-view-rename"
      type="text"
      aria-label="View name"
      value={value}
      onChange={(event) => setValue(event.target.value)}
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
      onBlur={() => finish(value)}
    />
  );
}
