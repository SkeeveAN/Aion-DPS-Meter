import { createInterface } from "node:readline/promises";
import { createKey, listKeys, revokeKey, setRate, DEFAULT_RATE_PER_MINUTE } from "../src/partnerKeys.js";

// A small text screen for the keys of the partner API (no dependencies; needs a terminal, so over SSH use "ssh -t").
//   pnpm tsx scripts/partner-keys-tui.ts
// Up/Down choose a key, n = new key, r = revoke, l = change the limit, q = quit.

const ESC = "\x1b[";
const bold = (s: string) => `${ESC}1m${s}${ESC}0m`;
const dim = (s: string) => `${ESC}2m${s}${ESC}0m`;
const green = (s: string) => `${ESC}32m${s}${ESC}0m`;
const red = (s: string) => `${ESC}31m${s}${ESC}0m`;
const yellow = (s: string) => `${ESC}33m${s}${ESC}0m`;
const inverse = (s: string) => `${ESC}7m${s}${ESC}0m`;

if (!process.stdin.isTTY || !process.stdout.isTTY) {
  console.error("This needs a terminal (over SSH: ssh -t).");
  process.exit(1);
}

let selected = 0;
let message = "";

const pad = (s: string, n: number) => (s.length > n ? s.slice(0, n - 1) + "…" : s.padEnd(n));
/** Stored times are UTC "YYYY-MM-DD HH:MM:SS" or ISO; shown as local "YYYY-MM-DD HH:MM". */
function when(value: string | null): string {
  if (!value) {
    return "never";
  }
  const d = new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(value) ? value : value.replace(" ", "T") + "Z");
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

function draw() {
  const keys = listKeys();
  selected = Math.min(Math.max(selected, 0), Math.max(keys.length - 1, 0));
  const lines: string[] = [];
  lines.push(bold("Aion DPS – partner API keys"), "");
  lines.push(dim(`${pad("Id", 4)}${pad("Partner", 28)}${pad("Per min", 9)}${pad("Created", 18)}${pad("Last used", 18)}State`));
  if (keys.length === 0) {
    lines.push("", dim("  No keys yet. Press n to make one."));
  }
  keys.forEach((k, i) => {
    const row = `${pad(String(k.id), 4)}${pad(k.name, 28)}${pad(String(k.ratePerMinute), 9)}${pad(when(k.createdAt), 18)}${pad(when(k.lastUsedAt), 18)}${k.revokedAt ? red("revoked " + when(k.revokedAt)) : green("active")}`;
    lines.push(i === selected ? inverse(" " + row.replace(/\x1b\[[0-9;]*m/g, "") + " ") + (k.revokedAt ? "" : "") : " " + row);
  });
  lines.push("", `${bold("n")} new   ${bold("r")} revoke   ${bold("l")} limit   ${bold("↑↓")} choose   ${bold("q")} quit`);
  if (message) {
    lines.push("", message);
  }
  process.stdout.write(`${ESC}2J${ESC}H${lines.join("\n")}\n`);
}

async function ask(question: string): Promise<string> {
  process.stdin.setRawMode(false);
  const rl = createInterface({ input: process.stdin, output: process.stdout });
  const answer = (await rl.question(question)).trim();
  rl.close();
  process.stdin.setRawMode(true);
  process.stdin.resume();
  return answer;
}

function waitForKey(): Promise<void> {
  return new Promise((resolve) => process.stdin.once("data", () => resolve()));
}

async function onKey(key: string): Promise<boolean> {
  const keys = listKeys();
  const current = keys[selected];
  message = "";
  if (key === "q" || key === "\x03") {
    return false;
  }
  if (key === `${ESC}A`) {
    selected--;
  } else if (key === `${ESC}B`) {
    selected++;
  } else if (key === "n") {
    process.stdout.write(`${ESC}2J${ESC}H`);
    const name = await ask("Partner name (empty = cancel): ");
    if (name) {
      const rateText = await ask(`Requests per minute [${DEFAULT_RATE_PER_MINUTE}]: `);
      const rate = rateText === "" ? DEFAULT_RATE_PER_MINUTE : Number(rateText);
      if (!Number.isInteger(rate) || rate < 1) {
        message = red("The limit must be a whole number of at least 1.");
      } else {
        const made = createKey(name, rate);
        process.stdout.write(`${ESC}2J${ESC}H${bold("Key " + made.id + " for " + name)}\n\n${yellow(made.key)}\n\nShown only now: only its hash is stored, it cannot be read back.\nCopy it, then press any key.\n`);
        await waitForKey();
        message = green(`Key ${made.id} made for ${name}.`);
        selected = listKeys().length - 1;
      }
    }
  } else if (current && key === "r") {
    if (current.revokedAt) {
      message = yellow("That key is revoked already.");
    } else if ((await ask(`Revoke key ${current.id} (${current.name})? Type y to confirm: `)).toLowerCase() === "y") {
      revokeKey(current.id);
      message = green(`Key ${current.id} revoked.`);
    }
  } else if (current && key === "l") {
    if (current.revokedAt) {
      message = yellow("A revoked key has no limit to change.");
    } else {
      const text = await ask(`New limit for key ${current.id} (${current.name}), now ${current.ratePerMinute} per minute: `);
      const rate = Number(text);
      if (text !== "" && Number.isInteger(rate) && rate >= 1) {
        setRate(current.id, rate);
        message = green(`Key ${current.id}: ${rate} per minute.`);
      } else if (text !== "") {
        message = red("The limit must be a whole number of at least 1.");
      }
    }
  }
  return true;
}

process.stdin.setRawMode(true);
process.stdin.resume();
process.stdin.setEncoding("utf8");
process.stdout.write(`${ESC}?1049h${ESC}?25l`); // own screen, no cursor
draw();
let busy = false;
process.stdin.on("data", async (data: string) => {
  if (busy) {
    return;
  }
  busy = true;
  const go = await onKey(String(data));
  busy = false;
  if (!go) {
    process.stdout.write(`${ESC}?25h${ESC}?1049l`);
    process.exit(0);
  }
  process.stdout.write(`${ESC}?25l`);
  draw();
});
