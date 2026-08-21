import type { ReactNode } from "react";
import { Dialog, type DialogButtonColor } from "./Dialog";

export interface ConfirmDialogProps {
  open: boolean;
  icon?: string;
  title: ReactNode;
  message: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  confirmColor?: DialogButtonColor;
  onConfirm: () => void;
  onCancel: () => void;
}

/** Reusable yes/no confirmation built on {@link Dialog}; use `Dialog` directly for a custom body. */
export function ConfirmDialog({
  open,
  icon = "mdi-help-circle-outline",
  title,
  message,
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  confirmColor = "primary",
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  return (
    <Dialog
      open={open}
      icon={icon}
      title={title}
      onClose={onCancel}
      buttons={[
        { key: "cancel", label: cancelLabel, color: "neutral", autoFocus: true, onClick: onCancel },
        { key: "confirm", label: confirmLabel, color: confirmColor, onClick: onConfirm },
      ]}
    >
      <p className="dialog-message">{message}</p>
    </Dialog>
  );
}
