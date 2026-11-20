import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createServer } from "node:http";
import test from "node:test";
import { chromium } from "playwright";

test("validation viewers preserve native image detail when zoomed from fit", async () => {
  const assets = new URL("../../Snet.Yolo.Tasks.Shared/wwwroot/", import.meta.url);
  const [draw, css] = await Promise.all([
    readFile(new URL("js/val-draw.js", assets)),
    readFile(new URL("css/ls.css", assets)),
  ]);
  const server = createServer((req, res) => {
    if (req.url === "/draw.js") {
      res.setHeader("Content-Type", "text/javascript");
      res.end(draw);
    } else if (req.url === "/viewer.css") {
      res.setHeader("Content-Type", "text/css");
      res.end(css);
    } else {
      res.setHeader("Content-Type", "text/html");
      res.end('<!doctype html><link rel="stylesheet" href="/viewer.css"><div id="stage" class="ls-viewer-stage" style="width:320px;height:256px"></div>');
    }
  });
  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  let browser;
  try {
    browser = await chromium.launch({ headless: true });
    const page = await browser.newPage({ deviceScaleFactor: 1 });
    await page.goto("http://127.0.0.1:" + server.address().port);
    for (const kind of ["canvas", "image with overlay"]) {
      await page.evaluate(async kind => {
        const viewer = await import("/draw.js");
        const chart = document.createElement("canvas");
        chart.width = chart.height = 1024;
        const ctx = chart.getContext("2d");
        ctx.fillRect(0, 0, 1024, 1024);
        ctx.fillStyle = "white";
        for (let x = 0; x < 1024; x += 2) { ctx.fillRect(x, 0, 1, 1024); }

        const stage = document.getElementById("stage");
        if (kind === "canvas") {
          stage.replaceChildren(chart);
          chart.id = "media";
          chart.className = "ls-viewer-canvas";
        } else {
          stage.innerHTML = '<div id="media" class="ls-viewer-canvas ls-validation-viewer-image"><img alt=""><svg class="ls-validation-image-annotation" viewBox="0 0 1024 1024"><rect x="550" y="550" width="12" height="12" fill="red"/></svg></div>';
          const img = stage.querySelector("img");
          img.src = chart.toDataURL();
          await img.decode();
        }
        viewer.attachImageViewer("stage", "media", "");
      }, kind);
      const stage = await page.locator("#stage").boundingBox();
      const x = stage.x + stage.width / 2;
      const y = stage.y + stage.height / 2;
      const clip = { x: x - 32, y, width: 64, height: 1 };
      await page.screenshot();
      await page.mouse.move(x, y);
      await page.mouse.wheel(0, -Math.log(4) / .0015);
      await page.waitForFunction(() => Math.abs(document.getElementById("media").getBoundingClientRect().height - 1024) < 1);
      const zoomed = await page.screenshot();
      const contrast = await page.evaluate(async ({ png, clip }) => {
        const image = new Image();
        image.src = "data:image/png;base64," + png;
        await image.decode();
        const canvas = document.createElement("canvas");
        canvas.width = clip.width;
        canvas.height = clip.height;
        const ctx = canvas.getContext("2d");
        ctx.drawImage(image, -clip.x, -clip.y);
        const pixels = ctx.getImageData(0, 0, canvas.width, 1).data;
        let difference = 0;
        for (let offset = 4; offset < pixels.length; offset += 4) {
          difference += Math.abs(pixels[offset] - pixels[offset - 4]);
        }
        return difference / (canvas.width - 1);
      }, { png: zoomed.toString("base64"), clip });
      assert.ok(contrast > 230, `${kind} native-pixel contrast was ${contrast}/255`);
    }
  } finally {
    await browser?.close();
    await new Promise(resolve => server.close(resolve));
  }
});
