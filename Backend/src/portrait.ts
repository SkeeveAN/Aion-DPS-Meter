import { deflateSync } from "node:zlib";
import jpeg from "jpeg-js";

// Cut-out of the official character portrait: NC delivers a 512x512 JPEG on a black background; the website wants
// the figure alone, so the black that is connected to the image border (and the JPEG noise next to it) becomes transparent. Pure JS (jpeg-js + zlib),
// no native module: the production server has no compiler, no Pillow, no ImageMagick.

export type Rgba = { width: number; height: number; data: Uint8Array };

/** Pixels darker than this (max of R, G, B) that connect to the border are certainly background. NC's background is pure black (exactly 2); black coats and dark hair are only a few levels above it, so this limit must stay close to the background. */
export const BACKGROUND_MAX = 3;
/** NC's JPEG leaves blocky near-black noise (values up to ~25) around the figure. Within this many pixels of the certain background, pixels below GROW_MAX fade out instead of staying as dark blocks. */
export const GROW_RADIUS = 12;
export const GROW_MAX = 30;

/**
 * Flood fill from all four image borders over pixels with max(R,G,B) < threshold (4-neighbourhood): that is the certain background.
 * From there the background grows by GROW_RADIUS pixels through pixels below GROW_MAX, and inside the grown area the alpha follows the
 * brightness (fully transparent at the threshold, opaque from GROW_MAX), so the JPEG noise around hair and shoulders fades away without a
 * hard blocky edge. A black coat further from the background than the radius, or dark detail inside the figure, is not touched. The alpha
 * is softened slightly (Gaussian). Works in place on `image` and returns it.
 */
export function cutOutBackground(image: Rgba, threshold = BACKGROUND_MAX, sigma = 1): Rgba {
  const { width: w, height: h, data } = image;
  const level = (p: number) => Math.max(data[p * 4], data[p * 4 + 1], data[p * 4 + 2]);
  const bg = new Uint8Array(w * h);
  const stack: number[] = [];
  const push = (x: number, y: number) => {
    const p = y * w + x;
    if (!bg[p] && level(p) < threshold) {
      bg[p] = 1;
      stack.push(p);
    }
  };
  for (let x = 0; x < w; x++) {
    push(x, 0);
    push(x, h - 1);
  }
  for (let y = 0; y < h; y++) {
    push(0, y);
    push(w - 1, y);
  }
  while (stack.length > 0) {
    const p = stack.pop()!;
    const x = p % w;
    const y = (p - x) / w;
    if (x > 0) push(x - 1, y);
    if (x < w - 1) push(x + 1, y);
    if (y > 0) push(x, y - 1);
    if (y < h - 1) push(x, y + 1);
  }

  // Grow the background ring by ring through the dark pixels next to it.
  const region = new Uint8Array(bg);
  let front: number[] = [];
  for (let p = 0; p < w * h; p++) if (bg[p]) front.push(p);
  for (let ring = 0; ring < GROW_RADIUS && front.length > 0; ring++) {
    const next: number[] = [];
    for (const p of front) {
      const x = p % w;
      const y = (p - x) / w;
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        const xx = x + dx;
        const yy = y + dy;
        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
        const q = yy * w + xx;
        if (!region[q] && level(q) < GROW_MAX) {
          region[q] = 1;
          next.push(q);
        }
      }
    }
    front = next;
  }

  let alpha: Float32Array = new Float32Array(w * h);
  for (let p = 0; p < w * h; p++) {
    alpha[p] = region[p] ? Math.max(0, Math.min(255, ((level(p) - threshold) / (GROW_MAX - threshold)) * 255)) : 255;
  }
  alpha = gaussianBlur(alpha, w, h, sigma);
  for (let p = 0; p < w * h; p++) data[p * 4 + 3] = Math.max(0, Math.min(255, Math.round(alpha[p])));
  return image;
}

function gaussianBlur(src: Float32Array, w: number, h: number, sigma: number): Float32Array {
  const radius = Math.max(1, Math.ceil(sigma * 3));
  const kernel = new Float32Array(radius * 2 + 1);
  let sum = 0;
  for (let i = -radius; i <= radius; i++) {
    kernel[i + radius] = Math.exp(-(i * i) / (2 * sigma * sigma));
    sum += kernel[i + radius];
  }
  for (let i = 0; i < kernel.length; i++) kernel[i] /= sum;
  const clamp = (v: number, max: number) => (v < 0 ? 0 : v > max ? max : v);
  const tmp = new Float32Array(w * h);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      let acc = 0;
      for (let i = -radius; i <= radius; i++) acc += src[y * w + clamp(x + i, w - 1)] * kernel[i + radius];
      tmp[y * w + x] = acc;
    }
  }
  const out = new Float32Array(w * h);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      let acc = 0;
      for (let i = -radius; i <= radius; i++) acc += tmp[clamp(y + i, h - 1) * w + x] * kernel[i + radius];
      out[y * w + x] = acc;
    }
  }
  return out;
}

const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();

function crc32(buf: Buffer): number {
  let c = 0xffffffff;
  for (const b of buf) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type: string, body: Buffer): Buffer {
  const head = Buffer.alloc(8);
  head.writeUInt32BE(body.length, 0);
  head.write(type, 4, "latin1");
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), body])), 0);
  return Buffer.concat([head, body, crc]);
}

/** Encodes RGBA as an 8-bit PNG (filter 0 on every row). */
export function encodePng(image: Rgba): Buffer {
  const { width: w, height: h, data } = image;
  const raw = Buffer.alloc((w * 4 + 1) * h);
  for (let y = 0; y < h; y++) {
    raw[y * (w * 4 + 1)] = 0;
    Buffer.from(data.buffer, data.byteOffset + y * w * 4, w * 4).copy(raw, y * (w * 4 + 1) + 1);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0);
  ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 6; // RGBA
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", ihdr),
    chunk("IDAT", deflateSync(raw, { level: 9 })),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

/** JPEG bytes from NC -> transparent PNG bytes. Throws on anything that is not a decodable image of plausible size. */
export function portraitFromJpeg(jpegBytes: Buffer): Buffer {
  const decoded = jpeg.decode(jpegBytes, { useTArray: true, formatAsRGBA: true, maxResolutionInMP: 4, maxMemoryUsageInMB: 128 });
  if (decoded.width < 64 || decoded.height < 64) {
    throw new Error("portrait too small");
  }
  return encodePng(cutOutBackground({ width: decoded.width, height: decoded.height, data: decoded.data }));
}
