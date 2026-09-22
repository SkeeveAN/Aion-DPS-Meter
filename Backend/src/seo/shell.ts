import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const INDEX_PATH = path.join(__dirname, "..", "..", "..", "Web-Frontend", "index.html");

const HEAD_BLOCK = /<!--SEO:HEAD-->[\s\S]*?<!--\/SEO:HEAD-->/;
const APP_MARKER = "<!--SEO:APP-->";

let cachedTemplate: string | null = null;

function template(): string {
  if (cachedTemplate === null) {
    cachedTemplate = fs.readFileSync(INDEX_PATH, "utf8");
  }
  return cachedTemplate;
}

/**
 * The SPA's index.html doubles as the server-side page template: the default <head> block between
 * the SEO markers is what a crawler sees on "/" and what the client shows before JS runs. Pages that
 * know more (a boss, an instance) swap that block and drop a pre-rendered fragment into <main>.
 */
export function renderShell(options: { head?: string; app?: string } = {}): string {
  let html = template();
  if (options.head !== undefined) {
    html = html.replace(HEAD_BLOCK, options.head);
  }
  if (options.app !== undefined) {
    html = html.replace(APP_MARKER, options.app);
  }
  return html;
}
