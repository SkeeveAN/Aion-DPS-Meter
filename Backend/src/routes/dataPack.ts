import { createReadStream } from "node:fs";
import type { FastifyInstance } from "fastify";
import { contentTypeOf, dataFilePath, dataManifest } from "../dataPack.js";

export async function dataPackRoutes(app: FastifyInstance) {
  // The list of the data files the client may take from the server (see dataPack.ts): path, SHA-256, size, layout number. The version is the ETag, so a client
  // that has the current list gets an empty 304. Plain GET without any identifier; part of the permission to update, asked in restricted mode too.
  app.get("/api/data/manifest", async (request, reply) => {
    const manifest = dataManifest();
    const etag = `"${manifest.version}"`;
    reply.header("ETag", etag).header("Cache-Control", "no-cache");
    if (request.headers["if-none-match"] === etag) {
      return reply.code(304).send();
    }

    return manifest;
  });

  // One file of the list; the client checks its SHA-256 against the list, so a cached or cut answer is never taken in.
  app.get<{ Querystring: { path?: string } }>("/api/data/file", async (request, reply) => {
    const full = typeof request.query.path === "string" ? dataFilePath(request.query.path) : undefined;
    if (!full) {
      return reply.code(404).send({ error: "no such file" });
    }

    return reply.header("Cache-Control", "public, max-age=300").type(contentTypeOf(request.query.path!)).send(createReadStream(full));
  });
}
