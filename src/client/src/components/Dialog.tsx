import { useEffect, useRef, useId, type ReactNode } from "react";

export type DialogButtonColor = "primary" | "danger" | "neutral";

export interface DialogButton {
  key: string;
  label: string;
  color?: DialogButtonColor;
  disabled?: boolean;
  autoFocus?: boolean;
  onClick: () => void;
}

export interface DialogProps {
  open: boolean;
  icon?: string;
  title: ReactNode;
  buttons: DialogButton[];
  onClose: () => void;
  children: ReactNode;
}

const FOCUSABLE_SELECTOR =
  'button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])';

/**
 * Reusable modal dialog primitive: a themed header (icon + title), an arbitrary
 * body (`children`), and a row of configurable footer buttons. `ConfirmDialog`
 * builds the common confirm/cancel case on top of it; a custom dialog is just
 * `Dialog` rendered with developer-supplied `children` in place of a plain
 * message. Since `buttons[].disabled` is plain data owned by whoever renders
 * `Dialog`, that consumer wires it to their embedded content's own callbacks
 * (e.g. an `onValidityChange` prop) to enable/disable buttons in response.
 */
export function Dialog({ open, icon, title, buttons, onClose, children }: DialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const previouslyFocusedRef = useRef<HTMLElement | null>(null);
  const titleId = useId();

  useEffect(() => {
    if (!open) {
      return;
    }

    previouslyFocusedRef.current = document.activeElement as HTMLElement | null;

    const dialogElement = dialogRef.current;
    if (dialogElement && !dialogElement.contains(document.activeElement)) {
      const autoFocusTarget = dialogElement.querySelector<HTMLElement>("[data-dialog-autofocus]");
      const fallbackTarget = dialogElement.querySelector<HTMLElement>(FOCUSABLE_SELECTOR);
      (autoFocusTarget ?? fallbackTarget)?.focus();
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose();
        return;
      }
      if (event.key !== "Tab" || !dialogElement) {
        return;
      }
      const focusable = Array.from(dialogElement.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR));
      if (focusable.length === 0) {
        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      previouslyFocusedRef.current?.focus();
    };
  }, [open, onClose]);

  if (!open) {
    return null;
  }

  return (
    <div
      className="dialog-overlay"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <div className="dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} ref={dialogRef}>
        <div className="dialog-header">
          {icon && <span className={`mdi ${icon} dialog-header-icon`} aria-hidden="true" />}
          <span className="dialog-header-title" id={titleId}>
            {title}
          </span>
        </div>
        <div className="dialog-body">{children}</div>
        <div className="dialog-footer">
          {buttons.map((button) => (
            <button
              key={button.key}
              type="button"
              className={`dialog-button dialog-button-${button.color ?? "neutral"}`}
              disabled={button.disabled}
              data-dialog-autofocus={button.autoFocus ? "" : undefined}
              onClick={button.onClick}
            >
              {button.label}
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}
