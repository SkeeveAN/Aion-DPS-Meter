import { test } from "node:test";
import assert from "node:assert/strict";
import { createServer } from "node:net";
import { probeEndpoint } from "./serverStatus.js";

const listen = (onConn: (s: import("node:net").Socket) => void) =>
  new Promise<{ port: number; close: () => void }>((resolve) => {
    const srv = createServer(onConn).listen(0, "127.0.0.1", () => resolve({ port: (srv.address() as { port: number }).port, close: () => srv.close() }));
  });

test("probe: a server that keeps the connection is up and open, one that resets it shortly after accepting is still up", async () => {
  const keep = await listen(() => undefined);
  const reset = await listen((s) => void setTimeout(() => s.resetAndDestroy(), 100));
  const a = await probeEndpoint("127.0.0.1", keep.port);
  const b = await probeEndpoint("127.0.0.1", reset.port);
  keep.close();
  reset.close();
  assert.equal(a.up, true);
  assert.equal(a.behavior, "open");
  assert.equal(b.up, true);
  assert.equal(b.behavior, "reset");
});

test("probe: nothing listening is down", async () => {
  const srv = await listen(() => undefined);
  const { port } = srv;
  srv.close();
  await new Promise((r) => setTimeout(r, 50));
  const c = await probeEndpoint("127.0.0.1", port);
  assert.equal(c.up, false);
  assert.equal(c.behavior, null);
});
