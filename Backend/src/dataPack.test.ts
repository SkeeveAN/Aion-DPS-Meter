import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { buildManifestOf, dataFilePath, dataManifest, isAllowedDataPath } from "./dataPack.js";

test("allowed paths: data of aion2 and the interface texts, nothing the client trusts, no way out of the folder", () => {
  assert.equal(isAllowedDataPath("aion2/pets/regions.json"), true);
  assert.equal(isAllowedDataPath("aion2/maps/verteron/3_4.jpg"), true);
  assert.equal(isAllowedDataPath("i18n/ui_strings.json"), true);
  assert.equal(isAllowedDataPath("feedback/public.pem"), false);
  assert.equal(isAllowedDataPath("app/aiondps.ico"), false);
  assert.equal(isAllowedDataPath("sounds/cube.wav"), false);
  assert.equal(isAllowedDataPath("aion2/../app/aiondps.ico"), false);
  assert.equal(isAllowedDataPath("aion2/x.exe"), false);
  assert.equal(isAllowedDataPath("/etc/passwd"), false);
  assert.equal(isAllowedDataPath("aion2/a b.json"), false);
  assert.equal(dataFilePath("../package.json"), undefined);
});

test("manifest: path, hash, size and layout of every allowed file, the version changes with a file", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "datapack-"));
  try {
    mkdirSync(path.join(dir, "aion2", "pets"), { recursive: true });
    mkdirSync(path.join(dir, "app"), { recursive: true });
    writeFileSync(path.join(dir, "aion2", "pets", "regions.json"), "{\"groups\":[]}");
    writeFileSync(path.join(dir, "aion2", "empty.json"), "");
    writeFileSync(path.join(dir, "app", "aiondps.ico"), "icon");
    const first = buildManifestOf(dir);
    assert.deepEqual(first.files.map((f) => f.path), ["aion2/pets/regions.json"]);
    assert.equal(first.files[0].size, 13);
    assert.equal(first.files[0].format, 1);
    assert.match(first.files[0].sha256, /^[0-9a-f]{64}$/);
    writeFileSync(path.join(dir, "aion2", "pets", "regions.json"), "{\"groups\":[1]}");
    const second = buildManifestOf(dir);
    assert.notEqual(second.version, first.version);
    assert.notEqual(second.files[0].sha256, first.files[0].sha256);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("manifest: the shipped assets of the repository are listed, and every listed file can be read", () => {
  const manifest = dataManifest(Date.now() + 1_000_000);
  assert.ok(manifest.files.length > 1000);
  assert.ok(manifest.files.some((f) => f.path === "aion2/pets/regions.json"));
  assert.ok(manifest.files.some((f) => f.path === "i18n/ui_strings.json"));
  assert.ok(manifest.files.every((f) => isAllowedDataPath(f.path) && dataFilePath(f.path) !== undefined));
  assert.ok(manifest.files.every((f) => !f.path.startsWith("feedback/")));
});
