import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import disText from "../definition/supply-chain.dis?raw";
import { parseDisl } from "@client/canvas/library/disl/disTypes";

/**
 * The bundled specification's theme tokens are the colours the canvas paints with, in both themes.
 *
 * The compiled definition leaves colour to the stylesheets, so the specification's tokens and the custom
 * properties the module paints with are two statements of one palette; this holds them equal. A token the
 * module paints through a property of its own (`--supply-chain-*` in `supply-chain.css`) is followed to the
 * host property that property reads, which that stylesheet is also held to here.
 */

const CLIENT = join(__dirname, "..", "..", "..", "client", "src");

/** The custom property each theme token of the specification is painted with. */
const TOKEN_PROPERTIES: Readonly<Record<string, string>> = {
  "color.text": "--color-text",
  "color.text.muted": "--color-text-muted",
  "color.surface": "--color-surface",
  "color.border": "--color-border",
  "color.supply.source": "--color-diagram-supply-chain-source",
  "color.supply.processor": "--color-diagram-supply-chain-processor",
  "color.supply.producer": "--color-diagram-supply-chain-producer",
  "color.supply.integrator": "--color-diagram-supply-chain-integrator",
  "color.supply.hub": "--color-diagram-supply-chain-hub",
  "color.supply.outlet": "--color-diagram-supply-chain-outlet",
  "color.supply.consumer": "--color-diagram-supply-chain-consumer",
  "color.supply.on-stage": "--color-diagram-supply-chain-on-stage",
  "color.supply.trace": "--color-border-strong",
  "color.supply.flow": "--color-text-muted",
  "color.supply.goods": "--color-surface",
  "color.supply.group-fill": "--color-surface-raised",
  "color.supply.shadow": "--supply-chain-shadow",
};

/** The module's own properties that stand for a host one, as `supply-chain.css` declares them. */
const MODULE_PROPERTIES: Readonly<Record<string, string>> = {
  "--supply-chain-flow": "--color-text-muted",
  "--supply-chain-goods": "--color-surface",
  "--supply-chain-group-fill": "--color-surface-raised",
};

type Theme = Record<string, string>;

/** The custom properties a block declares, by name. */
function customPropertiesIn(block: string): Theme {
  return Object.fromEntries([...block.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)].map((match) => [match[1]!, match[2]!.trim().toLowerCase()]));
}

/** A stylesheet's text, without its comments. */
function stylesheet(...path: string[]): string {
  return readFileSync(join(...path), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
}

/** The first block of `selector`, and the one under `prefers-color-scheme: dark`, of a stylesheet. */
function lightAndDark(css: string, selector: string): { light: Theme; dark: Theme } {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const light = new RegExp(`^${escaped}\\s*\\{([^}]*)\\}`, "m").exec(css);
  const dark = new RegExp(`@media \\(prefers-color-scheme: dark\\)\\s*\\{\\s*${escaped}\\s*\\{([^}]*)\\}`).exec(css);
  if (light === null || dark === null) {
    throw new Error(`The stylesheet no longer has the light ${selector} block and the dark one this test reads.`);
  }

  return { light: customPropertiesIn(light[1]!), dark: customPropertiesIn(dark[1]!) };
}

/** A colour as red, green, blue and an alpha to two places, whether written as hex or as `rgb()`. */
function rgbaOf(colour: string): string {
  const hex = /^#([0-9a-f]{6})([0-9a-f]{2})?$/.exec(colour);
  if (hex !== null) {
    const channels = [0, 2, 4].map((at) => parseInt(hex[1]!.slice(at, at + 2), 16));
    const alpha = hex[2] === undefined ? 1 : parseInt(hex[2], 16) / 255;
    return `${channels.join(",")},${alpha.toFixed(2)}`;
  }

  const rgb = /^rgb\(\s*(\d+)\s+(\d+)\s+(\d+)\s*(?:\/\s*([\d.]+)%)?\s*\)$/.exec(colour);
  if (rgb !== null) {
    const alpha = rgb[4] === undefined ? 1 : Number(rgb[4]) / 100;
    return `${rgb[1]},${rgb[2]},${rgb[3]},${alpha.toFixed(2)}`;
  }

  throw new Error(`"${colour}" is not a colour this test reads.`);
}

const HOST = lightAndDark(stylesheet(CLIENT, "index.css"), ":root");
const MODULE = lightAndDark(stylesheet(__dirname, "supply-chain.css"), ".supply-chain-canvas");

/** What a property is in a theme: the host's value, the module's own over it, the dark block over the light. */
function valueIn(theme: "light" | "dark", property: string): string | undefined {
  const own = { ...MODULE.light, ...(theme === "dark" ? MODULE.dark : {}) }[property];
  return own ?? { ...HOST.light, ...(theme === "dark" ? HOST.dark : {}) }[property];
}

describe("the supply chain's theme tokens", () => {
  const theme = parseDisl(disText).notation.theme!;

  it("are each painted by a custom property", () => {
    expect(Object.keys(theme.tokens).sort()).toEqual(Object.keys(TOKEN_PROPERTIES).sort());
  });

  it("are painted through the module's own properties only where those read the host's", () => {
    for (const [property, host] of Object.entries(MODULE_PROPERTIES)) {
      expect(MODULE.light[property], property).toMatch(new RegExp(`^var\\(${host}(,|\\))`));
    }
  });

  for (const mode of ["light", "dark"] as const) {
    it(`equal the custom properties in the ${mode} theme`, () => {
      const tokens = { ...theme.tokens, ...theme.modes?.[mode] };
      for (const [token, property] of Object.entries(TOKEN_PROPERTIES)) {
        const value = valueIn(mode, property);
        expect(value, `${token} (${property}) is declared`).toBeDefined();
        expect(rgbaOf(value!), `${token} (${property})`).toBe(rgbaOf(tokens[token]!.toLowerCase()));
      }
    });
  }
});
