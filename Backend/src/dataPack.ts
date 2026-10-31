import { createHash } from "node:crypto";
import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

/**
 * The data the client ships (Client/assets) and may take from the server instead, so a changed table, a new map or a new instance needs no client release:
 * the client starts with its own set, asks for this manifest now and then and takes every file that differs from its own in a folder of its data; the files of
 * that folder win over the shipped ones. The server's source is the checkout of the repository itself (Client/assets), so there is one copy and nothing can drift:
 * a change is pushed and deployed, and the next release carries it as the new shipped set.
 *
 * Only data may leave this way: files of aion2/ (tables, names, pictures) and the interface texts (i18n/), never anything the client trusts (the public key of the
 * feedback, the icon, the sounds). A file the client's code reads in another layout than before gets a higher number in DATA_FORMATS: a client that only knows
 * the older layout leaves it alone.
 */

export type DataFile = { path: string; sha256: string; size: number; format: number };
export type DataManifest = { version: string; files: DataFile[] };

const ASSETS_ROOT = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "Client", "assets");
const ALLOWED = /^(aion2|i18n)\/[A-Za-z0-9_][A-Za-z0-9_.\-/]*\.(json|png|jpg|wav)$/;
const MAX_FILE_BYTES = 20 * 1024 * 1024;
const MANIFEST_TTL_MS = 30_000;

/** The layout number of files whose layout changed in a way an older client cannot read (default 1). Raise it together with the client change that reads the new layout. */
export const DATA_FORMATS: Record<string, number> = {};

export function isAllowedDataPath(relative: string): boolean {
  return ALLOWED.test(relative) && !relative.split("/").some((part) => part === "." || part === "..");
}

export function dataFormatOf(relative: string): number {
  return DATA_FORMATS[relative] ?? 1;
}

function walk(root: string, relative: string, out: string[]): void {
  for (const entry of readdirSync(path.join(root, relative), { withFileTypes: true })) {
    const next = relative ? `${relative}/${entry.name}` : entry.name;
    if (entry.isDirectory()) {
      walk(root, next, out);
    } else if (entry.isFile() && isAllowedDataPath(next)) {
      out.push(next);
    }
  }
}

const hashes = new Map<string, { mtime: number; size: number; sha256: string }>();

function hashOf(root: string, relative: string, mtime: number, size: number): string {
  const key = `${root}\n${relative}`;
  const known = hashes.get(key);
  if (known && known.mtime === mtime && known.size === size) {
    return known.sha256;
  }

  const sha256 = createHash("sha256").update(readFileSync(path.join(root, relative))).digest("hex");
  hashes.set(key, { mtime, size, sha256 });
  return sha256;
}

/** The manifest of a folder (a pure function of the files in it); `version` changes when any file, size or layout number changes. */
export function buildManifestOf(root: string): DataManifest {
  const names: string[] = [];
  walk(root, "", names);
  const files: DataFile[] = [];
  for (const relative of names.sort()) {
    const stat = statSync(path.join(root, relative));
    if (stat.size === 0 || stat.size > MAX_FILE_BYTES) {
      continue;
    }

    files.push({ path: relative, sha256: hashOf(root, relative, stat.mtimeMs, stat.size), size: stat.size, format: dataFormatOf(relative) });
  }

  const version = createHash("sha256").update(files.map((f) => `${f.path}:${f.sha256}:${f.format}`).join("\n")).digest("hex").slice(0, 16);
  return { version, files };
}

let cached: { at: number; manifest: DataManifest } | undefined;

/** The manifest of the shipped assets, rebuilt at most every 30 seconds (a deploy replaces the files, the next request sees them). */
export function dataManifest(now = Date.now()): DataManifest {
  if (!cached || now - cached.at > MANIFEST_TTL_MS) {
    cached = { at: now, manifest: buildManifestOf(ASSETS_ROOT) };
  }

  return cached.manifest;
}

/** The file of an allowed path inside the assets folder, or undefined (not allowed, not there, too large). */
export function dataFilePath(relative: string): string | undefined {
  if (!isAllowedDataPath(relative)) {
    return undefined;
  }

  const full = path.join(ASSETS_ROOT, relative);
  try {
    const stat = statSync(full);
    return stat.isFile() && stat.size > 0 && stat.size <= MAX_FILE_BYTES ? full : undefined;
  } catch {
    return undefined;
  }
}

export function contentTypeOf(relative: string): string {
  if (relative.endsWith(".json")) return "application/json; charset=utf-8";
  if (relative.endsWith(".png")) return "image/png";
  if (relative.endsWith(".jpg")) return "image/jpeg";
  return "audio/wav";
}
