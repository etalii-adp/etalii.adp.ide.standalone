import type { DislBox, DislPathSegment, GeomExpr } from "./disTypes";

/**
 * A GeomExpr evaluator for the shape context, for TESTS ONLY.
 *
 * <b>Why it exists.</b> A custom shape in a `.dis` states its geometry as CEL over `w`, `h` and the
 * shape's parameters `p` (DISL §6.8), while the canvas draws a library shape the module's binding
 * names instead. Nothing at run time reads the GeomExprs, so nothing at run time could notice the
 * two drifting apart. A test evaluates both at a grid of sizes and parameters and requires the same
 * points: that is the proof the binding is honest.
 *
 * <b>Why it refuses so much.</b> It reads exactly the subset the bundled custom shapes use - numbers,
 * `w`, `h`, `p.<name>`, `+ - * /`, unary minus, comparisons, `&&`, `||`, `!`, the conditional and
 * `min`/`max` - and throws on anything else, naming it. An evaluator that guessed at an unknown
 * function would make the proof say "equal" about an expression it never understood.
 */

/** The shape context: the box's width and height, and the shape's parameters. */
export interface GeomScope {
  w: number;
  h: number;
  p: Readonly<Record<string, number>>;
}

type Value = number | boolean;

type Token =
  | { kind: "number"; value: number }
  | { kind: "name"; value: string }
  | { kind: "op"; value: string };

const OPERATORS = ["==", "!=", ">=", "<=", "&&", "||", "+", "-", "*", "/", "(", ")", ",", "?", ":", ">", "<", "!", "."];

function tokenize(text: string): Token[] {
  const tokens: Token[] = [];
  let index = 0;
  while (index < text.length) {
    const character = text[index]!;
    if (/\s/.test(character)) {
      index++;
      continue;
    }

    const number = /^\d+(\.\d+)?([eE][-+]?\d+)?/.exec(text.slice(index));
    if (number !== null) {
      tokens.push({ kind: "number", value: Number(number[0]) });
      index += number[0].length;
      continue;
    }

    const name = /^[A-Za-z_][A-Za-z0-9_]*/.exec(text.slice(index));
    if (name !== null) {
      tokens.push({ kind: "name", value: name[0] });
      index += name[0].length;
      continue;
    }

    const operator = OPERATORS.find((candidate) => text.startsWith(candidate, index));
    if (operator === undefined) {
      throw new Error(`GeomExpr "${text}": "${character}" is outside the shape-context subset.`);
    }

    tokens.push({ kind: "op", value: operator });
    index += operator.length;
  }

  return tokens;
}

/** A recursive-descent reader over one expression's tokens, evaluating as it goes. */
class Reader {
  private position = 0;

  constructor(
    private readonly text: string,
    private readonly tokens: readonly Token[],
    private readonly scope: GeomScope,
  ) {}

  read(): Value {
    const value = this.conditional();
    if (this.position !== this.tokens.length) {
      this.fail(`unexpected "${this.describe(this.tokens[this.position])}"`);
    }

    return value;
  }

  private conditional(): Value {
    const condition = this.or();
    if (!this.accept("?")) {
      return condition;
    }

    const whenTrue = this.conditional();
    this.expect(":");
    const whenFalse = this.conditional();
    return this.bool(condition) ? whenTrue : whenFalse;
  }

  private or(): Value {
    let value = this.and();
    while (this.accept("||")) {
      const right = this.and();
      value = this.bool(value) || this.bool(right);
    }

    return value;
  }

  private and(): Value {
    let value = this.comparison();
    while (this.accept("&&")) {
      const right = this.comparison();
      value = this.bool(value) && this.bool(right);
    }

    return value;
  }

  private comparison(): Value {
    const left = this.additive();
    for (const operator of ["==", "!=", ">=", "<=", ">", "<"]) {
      if (this.accept(operator)) {
        const a = this.num(left);
        const b = this.num(this.additive());
        switch (operator) {
          case "==":
            return a === b;
          case "!=":
            return a !== b;
          case ">=":
            return a >= b;
          case "<=":
            return a <= b;
          case ">":
            return a > b;
          default:
            return a < b;
        }
      }
    }

    return left;
  }

  private additive(): Value {
    let value = this.multiplicative();
    for (;;) {
      if (this.accept("+")) {
        value = this.num(value) + this.num(this.multiplicative());
      } else if (this.accept("-")) {
        value = this.num(value) - this.num(this.multiplicative());
      } else {
        return value;
      }
    }
  }

  private multiplicative(): Value {
    let value = this.unary();
    for (;;) {
      if (this.accept("*")) {
        value = this.num(value) * this.num(this.unary());
      } else if (this.accept("/")) {
        value = this.num(value) / this.num(this.unary());
      } else {
        return value;
      }
    }
  }

  private unary(): Value {
    if (this.accept("-")) {
      return -this.num(this.unary());
    }

    if (this.accept("!")) {
      return !this.bool(this.unary());
    }

    return this.primary();
  }

  private primary(): Value {
    const token = this.tokens[this.position];
    if (token === undefined) {
      return this.fail("the expression ends early");
    }

    this.position++;
    if (token.kind === "number") {
      return token.value;
    }

    if (token.kind === "op" && token.value === "(") {
      const value = this.conditional();
      this.expect(")");
      return value;
    }

    if (token.kind === "name") {
      switch (token.value) {
        case "w":
          return this.scope.w;
        case "h":
          return this.scope.h;
        case "true":
          return true;
        case "false":
          return false;
        case "p": {
          this.expect(".");
          const name = this.tokens[this.position];
          if (name?.kind !== "name") {
            return this.fail("a parameter name must follow \"p.\"");
          }

          this.position++;
          const value = this.scope.p[name.value];
          if (value === undefined) {
            return this.fail(`the parameter "p.${name.value}" has no value in this scope`);
          }

          return value;
        }
        case "min":
        case "max": {
          this.expect("(");
          const a = this.num(this.conditional());
          this.expect(",");
          const b = this.num(this.conditional());
          this.expect(")");
          return token.value === "min" ? Math.min(a, b) : Math.max(a, b);
        }
        default:
          return this.fail(`"${token.value}" is outside the shape-context subset`);
      }
    }

    return this.fail(`unexpected "${token.value}"`);
  }

  private accept(operator: string): boolean {
    const token = this.tokens[this.position];
    if (token?.kind === "op" && token.value === operator) {
      this.position++;
      return true;
    }

    return false;
  }

  private expect(operator: string): void {
    if (!this.accept(operator)) {
      this.fail(`expected "${operator}"`);
    }
  }

  private num(value: Value): number {
    if (typeof value !== "number") {
      return this.fail("a number was expected where a condition stands");
    }

    return value;
  }

  private bool(value: Value): boolean {
    if (typeof value !== "boolean") {
      return this.fail("a condition was expected where a number stands");
    }

    return value;
  }

  private describe(token: Token | undefined): string {
    return token === undefined ? "end" : String(token.value);
  }

  private fail(message: string): never {
    throw new Error(`GeomExpr "${this.text}": ${message}.`);
  }
}

function evaluate(expression: GeomExpr | boolean, scope: GeomScope): Value {
  if (typeof expression === "number" || typeof expression === "boolean") {
    return expression;
  }

  return new Reader(expression, tokenize(expression), scope).read();
}

/** A GeomExpr's number in a scope; a literal number is itself. */
export function evaluateGeom(expression: GeomExpr, scope: GeomScope): number {
  const value = evaluate(expression, scope);
  if (typeof value !== "number") {
    throw new Error(`GeomExpr "${String(expression)}" is a condition, not a number.`);
  }

  return value;
}

/** A part's `when` in a scope; an absent `when` holds. */
export function evaluateCondition(expression: string | undefined, scope: GeomScope): boolean {
  if (expression === undefined) {
    return true;
  }

  const value = evaluate(expression, scope);
  if (typeof value !== "boolean") {
    throw new Error(`GeomExpr "${expression}" is a number, not a condition.`);
  }

  return value;
}

/** One resolved path command: a point to move or draw to, or an arc to it, or the close. */
export type ResolvedSegment =
  | { op: "M" | "L"; x: number; y: number }
  | { op: "A"; rx: number; ry: number; rotation: number; largeArc: boolean; sweep: boolean; x: number; y: number }
  | { op: "Z" };

/** A custom shape's path with every GeomExpr evaluated. */
export function resolvePath(segments: readonly DislPathSegment[], scope: GeomScope): readonly ResolvedSegment[] {
  return segments.map((segment): ResolvedSegment => {
    switch (segment.op) {
      case "M":
      case "L":
        return { op: segment.op, x: evaluateGeom(segment.x, scope), y: evaluateGeom(segment.y, scope) };
      case "A":
        return {
          op: "A",
          rx: evaluateGeom(segment.rx, scope),
          ry: evaluateGeom(segment.ry, scope),
          rotation: evaluateGeom(segment.rotation, scope),
          largeArc: segment.largeArc,
          sweep: segment.sweep,
          x: evaluateGeom(segment.x, scope),
          y: evaluateGeom(segment.y, scope),
        };
      case "Z":
        return { op: "Z" };
      default:
        throw new Error(`Path command "${(segment as { op: string }).op}" is outside the shape-context subset.`);
    }
  });
}

/** A part's box with every GeomExpr evaluated. */
export function resolveBox(box: DislBox, scope: GeomScope): { x: number; y: number; w: number; h: number } {
  return { x: evaluateGeom(box.x, scope), y: evaluateGeom(box.y, scope), w: evaluateGeom(box.w, scope), h: evaluateGeom(box.h, scope) };
}
