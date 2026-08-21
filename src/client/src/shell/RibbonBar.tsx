import { useState } from "react";

interface RibbonButtonDef {
  icon: string;
  label: string;
}

interface RibbonGroupDef {
  group: string;
  buttons: RibbonButtonDef[];
}

const RIBBON_GROUPS: RibbonGroupDef[] = [
  {
    group: "File",
    buttons: [
      { icon: "mdi-content-save-outline", label: "Save" },
      { icon: "mdi-file-plus-outline", label: "New" },
    ],
  },
  {
    group: "Edit",
    buttons: [
      { icon: "mdi-undo", label: "Undo" },
      { icon: "mdi-redo", label: "Redo" },
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
        <div className="ribbon-group" key={group.group}>
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
      ))}
    </div>
  );
}
