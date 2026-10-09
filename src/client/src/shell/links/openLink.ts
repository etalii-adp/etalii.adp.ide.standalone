import { raiseLocalNotice } from "@client/shell/context/localNotices";

/**
 * Opening a link a document carries - a web page, or a place in the project.
 *
 * <b>A link is content somebody else wrote.</b> An agent writes the file an agent activity diagram
 * shows, and a link in it reaches the page as text. So the rule here is an allow-list rather than a
 * block-list: two schemes go to the browser, a path inside the open project goes to the workspace
 * tree, and everything else is shown and never followed (agent-activity-diagram Requirements 5.3
 * to 5.6). `javascript:`, `data:` and `file:` are refused by not being on the list, which is the
 * only way to refuse the scheme nobody has thought of yet.
 */
export type LinkTarget =
  /** An `http` or `https` address. */
  | { kind: "web"; href: string }
  /** A path inside the open project, as project-relative segments. */
  | { kind: "project"; segments: readonly string[] }
  /** A file location this page cannot open: an absolute path, or one that leaves the project. */
  | { kind: "elsewhere"; location: string }
  /** Neither a web address nor a file location. */
  | { kind: "refused"; location: string };

const SCHEME = /^[a-z][a-z0-9+.-]*:/i;
const DRIVE = /^[a-z]:[\\/]/i;

/**
 * What a link is, without opening anything.
 *
 * @param link The link as the document holds it.
 * @param documentPath The project-relative path of the document that carries it; a relative link
 * is relative to that document's folder.
 */
export function classifyLink(link: string, documentPath: readonly string[]): LinkTarget {
  const location = link.trim();
  if (location.length === 0) {
    return { kind: "refused", location };
  }

  if (/^https?:/i.test(location)) {
    try {
      const url = new URL(location);
      // Asked of the parsed address, not of the text: `http:evil` and friends parse to something
      // other than what they look like, and it is the parsed protocol the browser acts on.
      return url.protocol === "http:" || url.protocol === "https:" ? { kind: "web", href: url.href } : { kind: "refused", location };
    } catch {
      return { kind: "refused", location };
    }
  }

  // A drive letter looks like a scheme and is not one.
  if (DRIVE.test(location) || location.startsWith("/") || location.startsWith("\\")) {
    return { kind: "elsewhere", location };
  }

  if (SCHEME.test(location)) {
    return { kind: "refused", location };
  }

  const segments = [...documentPath.slice(0, -1)];
  for (const part of location.split(/[\\/]+/)) {
    if (part === "" || part === ".") {
      continue;
    }
    if (part === "..") {
      if (segments.length === 0) {
        return { kind: "elsewhere", location };
      }
      segments.pop();
      continue;
    }
    segments.push(part);
  }
  return segments.length === 0 ? { kind: "elsewhere", location } : { kind: "project", segments };
}

/** What opening needs from the page, supplied so a test can stand in for each. */
export interface LinkOpening {
  revealPath: (segments: string[]) => void;
  openWindow?: (href: string) => void;
  notify?: (text: string, copy?: string) => void;
}

/**
 * Opens a link, or says why it was not opened.
 *
 * A web address opens in a new tab that is handed no reference back to this page. A project path
 * is revealed in the workspace tree, which opens it when it is a file ADP has a tool for. Anything
 * else is shown in a notice with its location offered for copying, and nothing is followed.
 */
export function openLink(link: string, documentPath: readonly string[], opening: LinkOpening): LinkTarget {
  const target = classifyLink(link, documentPath);
  const notify = opening.notify ?? raiseLocalNotice;
  switch (target.kind) {
    case "web":
      (opening.openWindow ?? openInNewTab)(target.href);
      break;
    case "project":
      opening.revealPath([...target.segments]);
      break;
    case "elsewhere":
      notify(`${target.location} is outside this project, so it cannot be opened from here.`, target.location);
      break;
    case "refused":
      notify(`${target.location || "This link"} is neither a web address nor a file location, so it was not opened.`, target.location || undefined);
      break;
  }
  return target;
}

function openInNewTab(href: string): void {
  // `noopener` withholds `window.opener` from the page that opens, and `noreferrer` the address it
  // came from: a link an agent wrote gets no handle on the project it was written in.
  window.open(href, "_blank", "noopener,noreferrer");
}
