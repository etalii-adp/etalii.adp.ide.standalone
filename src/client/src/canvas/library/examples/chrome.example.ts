import type { ChromeDeclaration } from "../definition/chrome";

/**
 * Canvas chrome - the text around the diagram rather than the diagram.
 *
 * **No shipped module declares `chrome` today**, measured by parsing every module's
 * `DiagramDefinition`. The three modules with loading and unavailable states render them in their
 * own JSX instead, which is what `client-centralization` task 3 moves into the library; when that
 * lands, this example is replaced by the module that adopts it first.
 *
 * Every text here is a `TemplateBinding` - a literal string. The other binding forms read a value
 * from the model instead, which is what makes a title track the document rather than repeat a
 * constant.
 */
export const CHROME_EXAMPLE: ChromeDeclaration = {
  loading: { text: { template: "Loading\u2026" } },
  unavailable: { text: { template: "This build cannot draw this diagram." } },
  title: { text: { template: "Dependency graph" } },
};
