/**
 * A tag expression - `energy and (transport or industry)` - parsed and matched.
 *
 * The grammar is the one the canvas filter declares, and nothing more:
 *
 *     expr   := term ("or" term)*
 *     term   := factor ("and" factor)*
 *     factor := tag | "(" expr ")"
 *
 * so `and` binds tighter than `or`, and `a or b and c` is `a or (b and c)`. A tag is any run of
 * characters that is not white space and not a parenthesis, other than the two keywords; keywords
 * and tags alike are compared without regard to case.
 *
 * <b>Nothing here throws.</b> A malformed expression is a result naming what went wrong and where,
 * because the filter box shows it under what the reader is typing while the last good filter stays
 * applied - an exception would have nowhere useful to go.
 */

export type TagExpression =
  | { kind: "tag"; tag: string }
  | { kind: "and" | "or"; left: TagExpression; right: TagExpression };

export type TagExpressionResult =
  | { ok: true; expression: TagExpression | null }
  | { ok: false; message: string; position: number };

interface Token {
  kind: "open" | "close" | "and" | "or" | "tag" | "end";
  text: string;
  /** Where the token starts, as a 0-based index into the text. */
  position: number;
}

function tokensOf(text: string): Token[] {
  const tokens: Token[] = [];
  const pattern = /\s*(?:(\()|(\))|([^\s()]+))/gy;
  let match: RegExpExecArray | null;
  while ((match = pattern.exec(text)) !== null && match[0].length > 0) {
    const position = match.index + (match[0].length - (match[1] ?? match[2] ?? match[3] ?? "").length);
    if (match[1] !== undefined) {
      tokens.push({ kind: "open", text: "(", position });
    } else if (match[2] !== undefined) {
      tokens.push({ kind: "close", text: ")", position });
    } else {
      const word = match[3]!;
      const lower = word.toLowerCase();
      tokens.push({ kind: lower === "and" ? "and" : lower === "or" ? "or" : "tag", text: word, position });
    }
  }

  tokens.push({ kind: "end", text: "", position: text.length });
  return tokens;
}

class ParseError {
  constructor(readonly message: string, readonly position: number) {}
}

/**
 * The expression a text says, or where it stops making sense. Empty or blank text is a valid
 * expression that matches everything - a cleared filter box shows the whole diagram.
 */
export function parseTagExpression(text: string): TagExpressionResult {
  const tokens = tokensOf(text);
  if (tokens.length === 1) {
    return { ok: true, expression: null };
  }

  let index = 0;
  const peek = () => tokens[index]!;
  const take = () => tokens[index++]!;

  const describe = (token: Token) => (token.kind === "end" ? "the end" : `"${token.text}"`);

  function expression(): TagExpression {
    let left = term();
    while (peek().kind === "or") {
      take();
      left = { kind: "or", left, right: term() };
    }
    return left;
  }

  function term(): TagExpression {
    let left = factor();
    while (peek().kind === "and") {
      take();
      left = { kind: "and", left, right: factor() };
    }
    return left;
  }

  function factor(): TagExpression {
    const token = take();
    if (token.kind === "tag") {
      return { kind: "tag", tag: token.text.toLowerCase() };
    }

    if (token.kind === "open") {
      const inner = expression();
      const close = peek();
      if (close.kind !== "close") {
        throw new ParseError(`The parenthesis at ${token.position + 1} is never closed.`, token.position);
      }
      take();
      return inner;
    }

    throw new ParseError(`Expected a tag or "(" at ${token.position + 1}, found ${describe(token)}.`, token.position);
  }

  try {
    const parsed = expression();
    const rest = peek();
    if (rest.kind !== "end") {
      const message = rest.kind === "close"
        ? `The parenthesis at ${rest.position + 1} closes nothing.`
        : `Expected "and", "or" or the end at ${rest.position + 1}, found ${describe(rest)}.`;
      return { ok: false, message, position: rest.position };
    }
    return { ok: true, expression: parsed };
  } catch (error) {
    if (error instanceof ParseError) {
      return { ok: false, message: error.message, position: error.position };
    }
    throw error;
  }
}

/** Whether a set of tags satisfies an expression. No expression matches everything. */
export function matchesTags(expression: TagExpression | null, tags: readonly string[]): boolean {
  if (expression === null) {
    return true;
  }

  switch (expression.kind) {
    case "tag":
      return tags.some((tag) => tag.toLowerCase() === expression.tag);
    case "and":
      return matchesTags(expression.left, tags) && matchesTags(expression.right, tags);
    case "or":
      return matchesTags(expression.left, tags) || matchesTags(expression.right, tags);
  }
}
