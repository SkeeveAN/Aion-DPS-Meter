// Minimal HTML templating for the server-rendered page fragments: a tagged template that escapes
// every interpolated value unless it is already Raw (the output of another html`` call).
export class Raw {
  constructor(public readonly value: string) {}
  toString(): string {
    return this.value;
  }
}

export const raw = (value: string): Raw => new Raw(value);

export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function render(value: unknown): string {
  if (value instanceof Raw) {
    return value.value;
  }
  if (Array.isArray(value)) {
    return value.map(render).join("");
  }
  if (value === null || value === undefined || value === false) {
    return "";
  }
  return escapeHtml(String(value));
}

export function html(strings: TemplateStringsArray, ...values: unknown[]): Raw {
  let out = "";
  strings.forEach((chunk, i) => {
    out += chunk;
    if (i < values.length) {
      out += render(values[i]);
    }
  });
  return new Raw(out);
}

/** Text form of a number for SSR tables - en-US grouping, matching what most of the audience sees in-game. */
export function formatInt(n: number): string {
  return Math.round(n).toLocaleString("en-US");
}
