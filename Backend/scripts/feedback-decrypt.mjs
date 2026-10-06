// Decrypts a feedback recording (feedback/<id>/recording.bin.enc from the private data repo).
//   node scripts/feedback-decrypt.mjs <recording.bin.enc> [out.jsonl] [private.pem]
// The private key stays on the maintainer's machine (default ~/.config/aiondps-feedback/private.pem);
// the matching public key ships in the client (Client/assets/feedback/public.pem).
// Format: "ADFB1" | u16 BE length of the RSA-OAEP(SHA-256) wrapped AES key | wrapped key | 12-byte
// nonce | 16-byte GCM tag | AES-256-GCM ciphertext of the gzip-compressed JSON-lines capture.
import { createDecipheriv, privateDecrypt, constants } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { homedir } from "node:os";
import { gunzipSync } from "node:zlib";

const [input, output = "recording.jsonl", keyPath = `${homedir()}/.config/aiondps-feedback/private.pem`] = process.argv.slice(2);
if (!input) {
  console.error("usage: feedback-decrypt.mjs <recording.bin.enc> [out.jsonl] [private.pem]");
  process.exit(2);
}
const blob = readFileSync(input);
if (blob.subarray(0, 5).toString("latin1") !== "ADFB1") throw new Error("not an ADFB1 recording");
const keyLen = blob.readUInt16BE(5);
let pos = 7;
const wrapped = blob.subarray(pos, (pos += keyLen));
const nonce = blob.subarray(pos, (pos += 12));
const tag = blob.subarray(pos, (pos += 16));
const cipher = blob.subarray(pos);
const aesKey = privateDecrypt(
  { key: readFileSync(keyPath), padding: constants.RSA_PKCS1_OAEP_PADDING, oaepHash: "sha256" },
  wrapped,
);
const decipher = createDecipheriv("aes-256-gcm", aesKey, nonce);
decipher.setAuthTag(tag);
const plain = gunzipSync(Buffer.concat([decipher.update(cipher), decipher.final()]));
writeFileSync(output, plain);
console.log(`${plain.length} bytes -> ${output}`);
