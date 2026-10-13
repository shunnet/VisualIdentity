import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createServer } from "node:http";
import { chromium } from "playwright";

// 使用实际浏览器检查 Server 的 base64/LSB/连续位布局和预览缩放，不下载模型。
test("validation segmentation follows the native packed-mask contract at both scales", async () => {
  const source = await readFile(new URL("../../Snet.Yolo.Tasks.Shared/wwwroot/js/val-draw.js", import.meta.url));
  const server = createServer((req, res) => {
    res.setHeader("Content-Type", req.url === "/draw.js" ? "text/javascript" : "text/html");
    res.end(req.url === "/draw.js" ? source : '<canvas id="canvas"></canvas>');
  });
  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  const browser = await chromium.launch({ headless: true });
  try {
    const page = await browser.newPage();
    await page.goto("http://127.0.0.1:" + server.address().port);
    const actual = await page.evaluate(async () => {
      const engine = await import("/draw.js");
      const width = 11, height = 80, mask = new Uint8Array(Math.ceil(width * height / 8));
      for (let y = 24; y < 48; y++) for (let x = 2; x < 9; x++) {
        const index = y * width + x; mask[index >> 3] |= 1 << (index & 7);
      }
      const json = bytes => JSON.stringify({ ResultData: [{ BoundingBox: { X: 50, Y: 40, Width: width, Height: height },
        Label: { Name: "mask" }, BitPackedPixelMask: bytes }] });
      const results = [];
      for (const scale of [1, .5]) {
        const size = 180 * scale;
        const url = "data:image/svg+xml," + encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}"><rect width="${size}" height="${size}" fill="black"/></svg>`);
        const read = (x, y) => Array.from(document.getElementById("canvas").getContext("2d").getImageData(Math.floor(x * scale), Math.floor(y * scale), 1, 1).data);
        await engine.draw("canvas", url, json(Array.from(mask)), "Segmentation", true, 180, 180);
        const numeric = read(55, 70);
        await engine.draw("canvas", url, json(btoa(String.fromCharCode(...mask))), "Segmentation", true, 180, 180);
        results.push({ numeric, base64: read(55, 70), background: read(55, 100) });
      }
      return results;
    });
    for (const result of actual) {
      assert.deepEqual(result.base64, result.numeric);
      assert.ok(result.base64[0] > result.background[0] + 50, JSON.stringify(result));
    }
  } finally {
    await browser.close(); await new Promise(resolve => server.close(resolve));
  }
});
