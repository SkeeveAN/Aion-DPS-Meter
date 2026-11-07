import { createHash } from "node:crypto";

const IP_HASH_SALT = process.env.IP_HASH_SALT ?? "dpsmeter-dev-salt";

/** The same keyed hash for every place that must not store a raw address (uploads, download clicks). */
export function hashIp(ip: string): string {
  return createHash("sha256").update(IP_HASH_SALT).update(ip).digest("hex");
}

/** The keyed hash of the random id a visitor's browser keeps for its thumbs (setups.ts); the id itself is never stored. */
export function hashVoter(id: string): string {
  return createHash("sha256").update(IP_HASH_SALT).update("voter|").update(id).digest("hex");
}
