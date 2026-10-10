import { test } from "node:test";
import assert from "node:assert/strict";
import jpeg from "jpeg-js";
import { cutOutBackground, encodePng, portraitFromJpeg, type Rgba } from "./portrait.js";

/** 64x64 black image with a coloured square (16..47) that has a black 4x4 detail in its middle. */
function synthetic(): Rgba {
  const w = 64;
  const data = new Uint8Array(w * w * 4);
  for (let y = 0; y < w; y++) {
    for (let x = 0; x < w; x++) {
      const p = (y * w + x) * 4;
      const inside = x >= 16 && x < 48 && y >= 16 && y < 48;
      const hole = x >= 30 && x < 34 && y >= 30 && y < 34;
      const c = inside && !hole ? [200, 120, 60] : [0, 0, 0];
      data.set([...c, 255], p);
    }
  }
  return { width: w, height: w, data };
}
const alphaAt = (i: Rgba, x: number, y: number) => i.data[(y * i.width + x) * 4 + 3];

test("black border becomes transparent, the figure stays opaque", () => {
  const img = cutOutBackground(synthetic());
  assert.equal(alphaAt(img, 0, 0), 0);
  assert.equal(alphaAt(img, 63, 63), 0);
  assert.equal(alphaAt(img, 8, 32), 0);
  assert.equal(alphaAt(img, 24, 24), 255);
});

test("black detail inside the figure is not connected to the border and stays opaque", () => {
  const img = cutOutBackground(synthetic());
  assert.ok(alphaAt(img, 31, 31) > 200, `alpha ${alphaAt(img, 31, 31)}`);
});

test("the edge is eroded and softened", () => {
  const img = cutOutBackground(synthetic());
  // x = 16 is the first figure pixel; erosion by one pixel plus the blur make it partly transparent.
  const edge = alphaAt(img, 16, 32);
  assert.ok(edge > 0 && edge < 255, `edge alpha ${edge}`);
});

test("a bright pixel on the border is not background", () => {
  const img = synthetic();
  for (let y = 0; y < 3; y++) for (let x = 0; x < 3; x++) img.data.set([255, 255, 255, 255], (y * 64 + x) * 4);
  cutOutBackground(img, 34, 0.5);
  assert.ok(alphaAt(img, 0, 0) > 0);
});

test("PNG encoder and JPEG path produce a valid PNG with alpha", () => {
  const png = encodePng(synthetic());
  assert.deepEqual([...png.subarray(0, 8)], [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
  assert.equal(png.readUInt32BE(16), 64);
  assert.equal(png[25], 6);
  const src = synthetic();
  const jpg = jpeg.encode({ width: 64, height: 64, data: Buffer.from(src.data) }, 95).data;
  const out = portraitFromJpeg(jpg);
  assert.equal(out.readUInt32BE(16), 64);
  assert.throws(() => portraitFromJpeg(Buffer.from("not a jpeg")));
});
