import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "..");

// The privacy text exists twice: in the client's dialog (Client/assets/i18n/ui_strings.json, "Privacy.Text") and on the website (section 11 of
// Web-Frontend/locales/<language>.js). They must say the same, in every language: a change made in one place only is a wrong promise in the other.
test("privacy text: the client's dialog and section 11 of the privacy page are the same text in every language", () => {
  const client = JSON.parse(readFileSync(path.join(ROOT, "Client", "assets", "i18n", "ui_strings.json"), "utf8"))["Privacy.Text"] as Record<string, string>;
  const languages = Object.keys(client);
  assert.equal(languages.length, 10);
  for (const language of languages) {
    const page = readFileSync(path.join(ROOT, "Web-Frontend", "locales", `${language}.js`), "utf8");
    const match = /"legal\.privacySection11Body":\s*("(?:[^"\\]|\\.)*")/.exec(page);
    assert.ok(match, `section 11 missing in ${language}`);
    assert.equal(JSON.parse(match[1]), client[language], `the privacy texts differ in ${language}`);
  }
});
