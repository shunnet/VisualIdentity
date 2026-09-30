import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createServer } from "node:http";
import { chromium } from "playwright";

const scriptRoot = new URL("../../Snet.Yolo.Tasks.Shared/wwwroot/js/", import.meta.url);
const geometry = await import("data:text/javascript;base64," + Buffer.from(await readFile(new URL("polygon-path.js", scriptRoot))).toString("base64"));
const square = () => ({ pointsX: [50, 450, 450, 50], pointsY: [100, 100, 450, 450], curves: [null, null, null, null] });

test("cubic insertion preserves both halves, including the closing edge", () => {
  for (const edge of [0, 3]) {
    const r = square(); geometry.toggleCurve(r, edge);
    r.curves[edge][1] = 20; r.curves[edge][3] = 40;
    const old = geometry.copyPath(r), t = 0.37;
    const index = geometry.insertVertex(r, edge, t);
    const evaluate = (path, e, u) => {
      const j = (e + 1) % path.pointsX.length, c = path.curves[e];
      return geometry.splitCubic([path.pointsX[e], path.pointsY[e]], c.slice(0, 2), c.slice(2), [path.pointsX[j], path.pointsY[j]], u).point;
    };
    assert.equal(index, edge + 1);
    for (let k = 0; k <= 100; k++) {
      const u = k / 100, expected = evaluate(old, edge, u);
      const actual = u <= t ? evaluate(r, edge, u / t) : evaluate(r, index, (u - t) / (1 - t));
      assert.ok(Math.hypot(expected[0] - actual[0], expected[1] - actual[1]) < 1e-9);
    }
  }
});

test("vertex deletion respects minimum size and joins neighbors with a line", () => {
  const r = square(); geometry.toggleCurve(r, 3); geometry.toggleCurve(r, 0);
  assert.equal(geometry.deleteVertex(r, 0), true);
  assert.equal(r.curves[2], null);
  assert.equal(geometry.deleteVertex(r, 0), false);
  assert.equal(r.pointsX.length, 3);
});

test("canvas interaction, server echoes, cancellation, undo/redo and reload", { timeout: 60_000 }, async () => {
  const source = await readFile(new URL("ls-canvas.js", scriptRoot));
  const helper = await readFile(new URL("polygon-path.js", scriptRoot));
  const fixture = `<!doctype html><style>body{margin:0}#host{width:524px;height:524px}</style><div id="host"><canvas id="canvas"></canvas></div>
    <script type="module">
      import * as canvas from '/ls-canvas.js';
      window.engine=canvas; window.events=[]; window.regions=[]; window.editHistory=[]; window.editRedo=[];
      const sleep=ms=>new Promise(r=>setTimeout(r,ms));
      const clone=x=>structuredClone(x);
      const sync=()=>canvas.pushState('canvas',{regions:clone(window.regions)});
      const dotnet={async invokeMethodAsync(name,...args){
        window.events.push({name,args:clone(args)});
        if(name==='OnPolygonFinished'){
          window.regions=[{id:'polygon',type:'polygonlabels',color:'#40a0ff',selected:true,pointsX:args[0],pointsY:args[1],curves:args[0].map(()=>null)}]; sync();
        }else if(name==='OnRegionClicked'){
          await sleep(40); window.regions.forEach(r=>r.selected=r.id===args[0]); sync();
        }else if(name==='OnPolygonPathEdited'){
          window.editHistory.push(clone(window.regions)); window.editRedo=[];
          const r=window.regions.find(r=>r.id===args[0]);
          Object.assign(r,{pointsX:args[1],pointsY:args[2],curves:args[3]});
          await sleep(40); sync();
        }else if(name==='OnKey'){
          if(args[0]==='undo'&&window.editHistory.length){window.editRedo.push(clone(window.regions));window.regions=window.editHistory.pop();sync();}
          if(args[0]==='redo'&&window.editRedo.length){window.editHistory.push(clone(window.regions));window.regions=window.editRedo.pop();sync();}
        }
      }};
      window.reload=async()=>{canvas.destroy('canvas');await canvas.init('canvas',image,dotnet);sync();};
      const image='data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="500" height="500"><rect width="500" height="500" fill="#18212c"/></svg>');
      await canvas.init('canvas',image,dotnet); window.ready=true;
    </script>`;
  const server = createServer((req, res) => {
    res.setHeader("Content-Type", req.url.endsWith(".js") ? "text/javascript" : "text/html");
    res.end(req.url === "/ls-canvas.js" ? source : req.url === "/polygon-path.js" ? helper : fixture);
  });
  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  let browser;
  try {
    browser = await chromium.launch({ headless: true });
    const page = await browser.newPage(); const errors = [];
    page.setDefaultTimeout(5000);
    page.on("pageerror", e => errors.push(e.message));
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    try { await page.waitForFunction(() => window.ready); }
    catch (error) { throw new Error(`Canvas fixture failed: ${errors.join("; ") || error.message}`); }
    const click = (x, y) => page.mouse.click(x + 12, y + 12);
    const mode = m => page.evaluate(m => window.engine.setPolygonEditMode("canvas", m), m);
    const path = () => page.evaluate(() => structuredClone(window.regions[0]));
    const waitCommit = count => page.waitForFunction(n => window.events.filter(e => e.name === "OnPolygonPathEdited").length === n, count);
    const settle = () => page.waitForTimeout(100);
    await page.evaluate(() => window.engine.setMode("canvas", "polygon"));
    await click(50, 100); await click(450, 100); await click(450, 450); await click(50, 450); await click(50, 100);
    await page.waitForFunction(() => window.regions.length === 1);
    assert.equal((await path()).pointsX.length, 4);

    await mode("curve"); await click(250, 100); await waitCommit(1); await settle();
    assert.ok((await path()).curves[0]);
    await mode("reshape");
    await page.mouse.move(50 + 400 / 3 + 12, 112); await page.mouse.down();
    await page.mouse.move(190 + 12, 20 + 12, { steps: 10 }); await page.mouse.up();
    await waitCommit(2); await settle();
    assert.equal((await path()).curves[0][1], 20);
    // 曲线中点不在锚点连线内，验证按曲线而非直线命中。
    const r = await path();
    const midpoint = geometry.splitCubic([50,100], r.curves[0].slice(0,2),r.curves[0].slice(2),[450,100],0.5).point;
    await mode("insert"); await click(...midpoint); await waitCommit(3); await settle();
    assert.equal((await path()).pointsX.length, 5);
    await mode("delete"); const inserted = await path(); await click(inserted.pointsX[1], inserted.pointsY[1]); await waitCommit(4); await settle();
    assert.equal((await path()).pointsX.length, 4);
    await page.keyboard.press("Control+z"); await settle(); assert.equal((await path()).pointsX.length, 5);
    await page.keyboard.press("Control+Shift+z"); await settle(); assert.equal((await path()).pointsX.length, 4);

    await mode("reshape"); const before = await path();
    await page.mouse.move(62,112); await page.mouse.down(); await page.mouse.move(100,150);
    await page.keyboard.press("Escape"); await page.mouse.up(); await settle();
    assert.deepEqual(await path(), before);
    assert.equal(await page.evaluate(() => window.events.filter(e=>e.name==='OnPolygonPathEdited').length), 4);
    // 拖动时模拟旧选中状态回传，几何不能被覆盖。
    await page.mouse.move(62,112); await page.mouse.down(); await page.mouse.move(82,132);
    await page.evaluate(() => window.engine.pushState('canvas',{regions:structuredClone(window.regions)}));
    await page.mouse.up(); await waitCommit(5); await settle();
    assert.equal((await path()).pointsX[0], 70);
    await mode('curve'); await click(450,275); await waitCommit(6); await settle();
    await mode('reshape'); await page.mouse.move(462,100+350/3+12); await page.mouse.down();
    await page.mouse.move(492,232,{steps:5}); await page.mouse.up(); await waitCommit(7); await settle();
    const saved = await path(); assert.ok(saved.curves[1]);
    await page.evaluate(() => window.reload()); await settle(); assert.deepEqual(await path(), saved);
    await page.evaluate(() => window.engine.viewportAction('canvas','zoomIn'));
    await page.mouse.move(saved.pointsX[0]*1.25-50.5,saved.pointsY[0]*1.25-50.5); await page.mouse.down();
    await page.mouse.move(saved.pointsX[0]*1.25-50.5+25,saved.pointsY[0]*1.25-50.5-12.5); await page.mouse.up();
    await waitCommit(8); await settle();
    const zoomed = await path();
    assert.ok(Math.abs(zoomed.pointsX[0]-saved.pointsX[0]-20)<1);
    assert.ok(Math.abs(zoomed.pointsY[0]-saved.pointsY[0]+10)<1);
    await page.evaluate(() => window.engine.destroy('canvas'));
    await page.keyboard.press('Control+z'); await settle(); assert.deepEqual(await path(), zoomed);
    assert.deepEqual(errors, []);
  } finally { if (browser) { await browser.close(); } await new Promise(resolve => server.close(resolve)); }
});
