/** A run of prompt text; `placeholder` runs are `{token}` markers the worker fills at dispatch time. */
export interface PromptSegment {
  readonly text: string;
  readonly placeholder: boolean;
}

const PLACEHOLDER_PATTERN = /\{[A-Za-z][A-Za-z0-9]*\}/g;

/**
 * Splits prompt text into literal and placeholder segments so the template can highlight
 * placeholders with element bindings instead of HTML injection. Concatenating every segment's
 * `text` reproduces the input exactly.
 */
export function splitPromptPlaceholders(text: string): PromptSegment[] {
  const segments: PromptSegment[] = [];
  let cursor = 0;

  for (const match of text.matchAll(PLACEHOLDER_PATTERN)) {
    const start = match.index;
    if (start > cursor) {
      segments.push({ text: text.slice(cursor, start), placeholder: false });
    }
    segments.push({ text: match[0], placeholder: true });
    cursor = start + match[0].length;
  }

  if (cursor < text.length) {
    segments.push({ text: text.slice(cursor), placeholder: false });
  }

  return segments;
}
