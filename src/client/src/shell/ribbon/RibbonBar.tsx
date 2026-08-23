import { Fragment, useState } from "react";
import { RibbonContextualGroups } from "./RibbonContextualGroups";
import { RibbonHistoryGroup } from "./RibbonHistoryGroup";
import { useProjectShortcuts } from "../context/useProjectShortcuts";

interface RibbonButtonDef {
  icon: string;
  label: string;
}

interface RibbonGroupDef {
  group: string;
  buttons: RibbonButtonDef[];
}

// File and View are still mock buttons belonging to other specs; Edit's Undo/Redo are now real,
// rendered by RibbonHistoryGroup from the backend's pushed project actions rather than mocked
// here (diagram-undo-redo Requirement 6.4). The History group takes Edit's old slot, between
// File and View, so the layout does not shift.
const RIBBON_GROUPS: RibbonGroupDef[] = [
  {
    group: "File",
    buttons: [
      { icon: "mdi-content-save-outline", label: "Save" },
      { icon: "mdi-file-plus-outline", label: "New" },
    ],
  },
  {
    group: "View",
    buttons: [
      { icon: "mdi-magnify-plus-outline", label: "Zoom In" },
      { icon: "mdi-magnify-minus-outline", label: "Zoom Out" },
      { icon: "mdi-fit-to-page-outline", label: "Fit to View" },
    ],
  },
];

export function RibbonBar() {
  const [pressedIds, setPressedIds] = useState<Set<string>>(new Set());
  // The global Undo/Redo shortcuts (Ctrl+Z, Ctrl+Y), mounted once with the shell.
  useProjectShortcuts();

  const toggle = (id: string) => {
    // TODO(diagram-ide-mockup): wire real command handler
    setPressedIds((previous) => {
      const next = new Set(previous);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  return (
    <div className="ribbon">
      {RIBBON_GROUPS.map((group) => (
        <Fragment key={group.group}>
          <div className="ribbon-group">
            {group.buttons.map((button) => {
              const id = `${group.group}-${button.label}`;
              return (
                <button
                  key={id}
                  type="button"
                  className={`ribbon-button${pressedIds.has(id) ? " ribbon-button-pressed" : ""}`}
                  onClick={() => toggle(id)}
                >
                  <span className={`mdi ${button.icon}`} aria-hidden="true" />
                  <span className="ribbon-button-label">{button.label}</span>
                </button>
              );
            })}
          </div>
          {/* Undo/Redo take Edit's old slot, right after File. */}
          {group.group === "File" && <RibbonHistoryGroup />}
        </Fragment>
      ))}
      {/* The selection-driven part: whatever the backend says applies to the current selection. */}
      <RibbonContextualGroups />
    </div>
  );
}
