import { createKey, listKeys, revokeKey } from "../src/partnerKeys.js";

// Manages the keys of the partner API.
//   pnpm tsx scripts/partner-key.ts create "<partner name>" [requests per minute]
//   pnpm tsx scripts/partner-key.ts list
//   pnpm tsx scripts/partner-key.ts revoke <id>
const [command, ...rest] = process.argv.slice(2);

if (command === "create" && rest[0]) {
  const rate = rest[1] ? Number(rest[1]) : undefined;
  if (rate !== undefined && (!Number.isInteger(rate) || rate < 1)) {
    console.error("The limit must be a whole number of requests per minute.");
    process.exit(1);
  }
  const { id, key } = createKey(rest[0], rate);
  console.log(`Key ${id} for "${rest[0]}" (shown only now, it cannot be read back):\n${key}`);
} else if (command === "list") {
  console.table(listKeys());
} else if (command === "revoke" && Number.isInteger(Number(rest[0]))) {
  console.log(revokeKey(Number(rest[0])) ? `Key ${rest[0]} revoked.` : `Key ${rest[0]} not found or already revoked.`);
} else {
  console.error('Usage: partner-key.ts create "<name>" [per minute] | list | revoke <id>');
  process.exit(1);
}
