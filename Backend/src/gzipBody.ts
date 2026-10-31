import { createGunzip } from "node:zlib";
import { pipeline, type Readable } from "node:stream";
import type { FastifyInstance } from "fastify";

/**
 * Upload bodies may come gzip-compressed (`Content-Encoding: gzip`): a world boss with 700 players is 600 KB of JSON and 80 KB zipped, and the
 * client's old 15 s limit was hit on a slow uplink. The body is unpacked before it is parsed; the body limit of the route counts the UNPACKED bytes,
 * so a small zip cannot grow past it. Only the upload routes take it, everything else keeps answering a zipped body as before.
 */
export function registerGzipBodies(app: FastifyInstance): void {
  app.addHook("preParsing", async (request, _reply, payload) => {
    if (request.method !== "POST" || !request.url.startsWith("/api/uploads") || String(request.headers["content-encoding"] ?? "").toLowerCase() !== "gzip") {
      return payload;
    }
    const unpacked = pipeline(payload, createGunzip(), () => undefined) as Readable & { receivedEncodedLength?: number };
    // Fastify compares the Content-Length of the request with what arrived on the wire: the packed size, not the unpacked one.
    unpacked.receivedEncodedLength = 0;
    payload.on("data", (chunk: Buffer) => {
      unpacked.receivedEncodedLength = (unpacked.receivedEncodedLength ?? 0) + chunk.length;
    });
    delete request.headers["content-encoding"];
    return unpacked;
  });
}
