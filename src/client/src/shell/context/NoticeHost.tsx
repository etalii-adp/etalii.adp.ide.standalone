import { useContextNotices } from "./ContextConnectionProvider";
import "./NoticeHost.css";

/**
 * What the backend says happened alongside an edit that succeeded.
 *
 * A drag whose new position could not be recorded is the case this exists for: the element
 * moved, so refusing the edit would have lost work the user can see, and saying nothing meant
 * the position was quietly missing the next time the diagram was opened. Neither is acceptable,
 * so the edit lands and this says what did not.
 *
 * Deliberately not the errors-and-warnings panel: that panel is owned by validation, keyed by
 * file and replaced wholesale on the next pass, so a notice put there would be wiped by the
 * validator moments later. These are transient and belong to the moment they happened.
 *
 * Dismissed by hand rather than on a timer. A message that removes itself is a message the user
 * may never have read, and the thing it reports has already been lost - re-doing the drag is the
 * only remedy, and they cannot choose that if the notice vanished while they looked elsewhere.
 */
export function NoticeHost() {
  const { notices, dismiss } = useContextNotices();
  if (notices.length === 0) {
    return null;
  }

  return (
    // `status` rather than `alert`: nothing failed and nothing needs immediate attention, so a
    // screen reader should finish its sentence before announcing this.
    <div className="notice-host" role="status" aria-live="polite">
      {notices.map((notice) => (
        <div key={notice.id} className="notice">
          <span className="notice-text">{notice.text}</span>
          <button
            type="button"
            className="notice-dismiss"
            onClick={() => dismiss(notice.id)}
            aria-label="Dismiss this message"
          >
            ×
          </button>
        </div>
      ))}
    </div>
  );
}
