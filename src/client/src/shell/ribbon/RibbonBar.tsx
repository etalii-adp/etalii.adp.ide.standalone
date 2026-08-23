import { Fragment, useState } from "react";
import { RibbonContextualGroups } from "./RibbonContextualGroups";
import { RibbonHistoryGroup } from "./RibbonHistoryGroup";
import { useProjectShortcuts } from "../context/useProjectShortcuts";
import { useDiagramViewControls } from "../panels/DiagramViewContext";

interface RibbonButtonDef {
  icon: string;
  label: string;
}

interface RibbonGroupDef {
  group: string;
  buttons: RibbonButtonDef[];
}

// File keeps its mock buttons, belonging to other specs; Edit's Undo/Redo are real through
// RibbonHistoryGroup (diagram-undo-redo Requirement 6.4), and View's zoom and fit now drive
// whichever diagram canvas is open, through DiagramViewContext.
const RIBBON_GROUPS: RibbonGroupDef[] = [
  {
    group: "File",
    buttons: [
      { icon: "mdi-content-save-outline", label: "Save" },
      { icon: "mdi-file-plus-outline", label: "New" },
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
      <RibbonViewGroup />
      {/* The selection-driven part: whatever the backend says applies to the current selection. */}
      <RibbonContextualGroups />
    </div>
  );
}

/**
 * Zoom and fit for the open diagram. A pure passthrough to the mounted canvas's own view
 * controls; with no diagram open there is nothing to zoom, and the buttons say so by being
 * disabled rather than by disappearing.
 */
function RibbonViewGroup() {
  const controls = useDiagramViewControls();
  const buttons = [
    { icon: "mdi-magnify-plus-outline", label: "Zoom In", run: controls?.zoomIn },
    { icon: "mdi-magnify-minus-outline", label: "Zoom Out", run: controls?.zoomOut },
    { icon: "mdi-fit-to-page-outline", label: "Fit to View", run: controls?.fitToView },
  ];

  return (
    <div className="ribbon-group">
      {buttons.map((button) => (
        <button
          key={button.label}
          type="button"
          className="ribbon-button"
          title={controls === null ? "Open a diagram to use this." : button.label}
          disabled={controls === null}
          onClick={() => button.run?.()}
        >
          <span className={`mdi ${button.icon}`} aria-hidden="true" />
          <span className="ribbon-button-label">{button.label}</span>
        </button>
      ))}
    </div>
  );
}
