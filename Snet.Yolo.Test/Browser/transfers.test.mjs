import assert from 'node:assert/strict';
import test from 'node:test';
import { createServer } from 'node:http';
import { readFile, mkdir } from 'node:fs/promises';
import { chromium } from 'playwright';

const script = await readFile(new URL('../../Snet.Yolo.Tasks.Shared/wwwroot/js/transfers.js', import.meta.url));
const bytes = Buffer.alloc(13 * 1024 * 1024 + 37, 71);
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

// 只模拟传输协议边界；客户端脚本、IndexedDB、File、刷新、导航和浏览器下载均实际运行。
async function fixture() {
  const job = { id: 'sample', state: 'uploading', files: [{ name: 'sample.bmp', size: bytes.length }], offsets: [0] };
  const writes = [], received = [], downloads = new Map();
  let completions = 0;
  const server = createServer(async (req, res) => {
    try {
    const url = new URL(req.url, 'http://localhost');
    const json = value => { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(value)); };
    if (url.pathname === '/transfers.js') { res.setHeader('Content-Type', 'text/javascript'); res.end(script); return; }
    if (url.pathname === '/api/transfers/token') { json({ token: 'synthetic-csrf' }); return; }
    if (url.pathname === '/api/transfers/uploads') { json([]); return; }
    if (url.pathname === '/api/transfers/uploads/sample/cancel') { job.state = 'cancelled'; json(job); return; }
    if (url.pathname === '/api/transfers/uploads/sample/complete') { if (job.state === 'uploading') completions++; job.state = 'processing'; json(job); return; }
    if (url.pathname === '/api/transfers/uploads/sample/0') {
      const offset = Number(url.searchParams.get('offset'));
      const chunks = []; for await (const chunk of req) chunks.push(chunk);
      await delay(200);
      if (res.destroyed) return;
      if (offset !== job.offsets[0] || job.state !== 'uploading') { res.statusCode = 409; json({ error: 'offset' }); return; }
      assert.equal(req.headers.requestverificationtoken, 'synthetic-csrf');
      const body = Buffer.concat(chunks); writes.push(offset); received.push(body); job.offsets[0] += body.length; json(job); return;
    }
    if (url.pathname === '/api/transfers/uploads/sample') { json(job); return; }
    if (url.pathname === '/api/projects/demo/export') {
      const key = url.searchParams.get('transfer');
      if (!downloads.has(key)) downloads.set(key, { id: key, state: 'preparing', polls: 0 });
      json(downloads.get(key)); return;
    }
    if (url.pathname.match(/^\/api\/transfers\/downloads\/[^/]+$/)) {
      const item = [...downloads.values()].find(item => item.id === url.pathname.split('/').at(-1));
      item.polls++; if (item.polls >= 2) item.state = 'ready'; json(item); return;
    }
    if (url.pathname.endsWith('/file')) {
      res.setHeader('Content-Disposition', 'attachment; filename="fixture.zip"');
      res.setHeader('Content-Type', 'application/zip'); res.end('persistent-download-fixture'); return;
    }
    res.setHeader('Content-Type', 'text/html; charset=utf-8');
    res.end(`<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"></head><body><h1>持久传输回归</h1><input id="ls-upload-images" type="file"><label for="ls-upload-images">上传</label><a href="/api/projects/demo/export" download>下载</a>
      <script type="module">import { initialize, queueUpload } from '/transfers.js';
      await initialize(new URL(location.href).searchParams.get('owner') || 'alice');
      document.getElementById('ls-upload-images').onchange=event=>queueUpload('sample',event.target.id);
      document.body.dataset.ready='true';</script></body></html>`);
    } catch (error) {
      if (!req.aborted && !res.destroyed) { res.statusCode = 500; res.end(error.message); }
    }
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ acceptDownloads: true });
  const page = await context.newPage();
  const origin = 'http://127.0.0.1:' + server.address().port;
  return { job, writes, received, downloads, page, context, origin, completions: () => completions,
    async open(path = '/') { await page.goto(origin + path); await page.waitForSelector('body[data-ready="true"]'); },
    async close() { await browser.close(); server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); } };
}

async function waitFor(check) {
  const until = Date.now() + 10000;
  while (!check()) { if (Date.now() > until) throw new Error('Timed out waiting for transfer'); await delay(25); }
}

test('real File and IndexedDB survive full navigation and reload, resuming only unacknowledged chunks', { timeout: 25000 }, async () => {
  const f = await fixture();
  try {
    await f.open();
    await f.page.locator('input').setInputFiles({ name: 'sample.bmp', mimeType: 'image/bmp', buffer: bytes });
    await waitFor(() => f.job.offsets[0] > 0);
    await f.open('/other'); await f.page.reload(); await f.page.waitForSelector('body[data-ready="true"]');
    await waitFor(() => f.job.state === 'processing');
    assert.equal(f.completions(), 1); assert.equal(f.writes.filter(offset => offset === 0).length, 1);
    assert.deepEqual(Buffer.concat(f.received), bytes);
    await mkdir(new URL('../bin/transfer-browser-fixtures/', import.meta.url), { recursive: true });
    await f.page.screenshot({ path: new URL('../bin/transfer-browser-fixtures/refresh-resumed.png', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1') });
  } finally { await f.close(); }
});

test('cancel removes persisted sources and refresh cannot restart the cancelled upload', { timeout: 20000 }, async () => {
  const f = await fixture();
  try {
    await f.open(); await f.page.locator('input').setInputFiles({ name: 'sample.bmp', mimeType: 'image/bmp', buffer: bytes });
    await f.page.getByRole('button', { name: '取消', exact: true }).click();
    await waitFor(() => f.job.state === 'cancelled');
    const count = f.writes.length;
    await f.open('/after-cancel'); await delay(500);
    assert.equal(f.writes.length, count); assert.equal(f.completions(), 0);
  } finally { await f.close(); }
});

test('another signed-in owner cannot resume or discard the previous owners cached file', { timeout: 20000 }, async () => {
  const f = await fixture();
  try {
    await f.open(); await f.page.locator('input').setInputFiles({ name: 'sample.bmp', mimeType: 'image/bmp', buffer: bytes });
    await waitFor(() => f.job.offsets[0] > 0);
    await f.open('/?owner=bob'); const offset = f.job.offsets[0]; await delay(500); assert.equal(f.job.offsets[0], offset);
    await f.open('/?owner=alice'); await waitFor(() => f.job.state === 'processing'); assert.deepEqual(Buffer.concat(f.received), bytes);
  } finally { await f.close(); }
});

test('storage failure cancels the server task before any upload and displays an actionable error', { timeout: 15000 }, async () => {
  const f = await fixture();
  try {
    await f.page.addInitScript(() => { indexedDB.open = () => { throw new DOMException('storage denied', 'SecurityError'); }; });
    await f.open(); await f.page.locator('input').setInputFiles({ name: 'sample.bmp', mimeType: 'image/bmp', buffer: bytes });
    await waitFor(() => f.job.state === 'cancelled');
    await f.page.getByText(/未开始上传：无法保存续传来源/).waitFor(); assert.equal(f.writes.length, 0);
  } finally { await f.close(); }
});

test('download preparation survives reload without recreating the artifact and finishes in browser download manager', { timeout: 20000 }, async () => {
  const f = await fixture();
  try {
    await f.open(); await f.page.getByRole('link', { name: '下载' }).click(); await waitFor(() => f.downloads.size === 1);
    const promise = f.page.waitForEvent('download', { timeout: 8000 });
    await f.page.reload(); await f.page.waitForSelector('body[data-ready="true"]');
    const download = await promise; const path = await download.path();
    assert.equal(await readFile(path, 'utf8'), 'persistent-download-fixture'); assert.equal(f.downloads.size, 1);
  } finally { await f.close(); }
});

test('known insufficient cache quota cancels before storing the file or sending a chunk', { timeout: 15000 }, async () => {
  const f = await fixture();
  try {
    await f.page.addInitScript(() => { navigator.storage.estimate = async () => ({ quota: 8 * 1024 * 1024, usage: 7 * 1024 * 1024 }); });
    await f.open(); await f.page.locator('input').setInputFiles({ name: 'sample.bmp', mimeType: 'image/bmp', buffer: bytes });
    await waitFor(() => f.job.state === 'cancelled');
    await f.page.getByText(/浏览器缓存空间不足/).waitFor(); assert.equal(f.writes.length, 0);
    const entries = await f.page.evaluate(() => new Promise((resolve, reject) => {
      const request = indexedDB.open('snet.transfers');
      request.onsuccess = () => { const read = request.result.transaction('jobs').objectStore('jobs').getAll(); read.onsuccess = () => { resolve(read.result); request.result.close(); }; read.onerror = () => reject(read.error); };
    }));
    assert.equal(entries.length, 0);
  } finally { await f.close(); }
});

for (const mode of ['unavailable', 'throws']) test(`cache estimate ${mode} still permits a real atomic file cache`, { timeout: 20000 }, async () => {
  const f = await fixture();
  try {
    await f.page.addInitScript(mode => { navigator.storage.estimate = mode === 'unavailable' ? undefined : async () => { throw new TypeError('estimate unavailable'); }; }, mode);
    await f.open(); await f.page.locator('input').setInputFiles({ name: 'sample.bmp', mimeType: 'image/bmp', buffer: bytes });
    await waitFor(() => f.job.state === 'processing'); assert.deepEqual(Buffer.concat(f.received), bytes);
  } finally { await f.close(); }
});

test('persisted real file handles bypass full-file cache quota and resume after reload', { timeout: 20000 }, async () => {
  const f = await fixture();
  try {
    await f.open();
    await f.page.evaluate(async length => {
      const root = await navigator.storage.getDirectory();
      const handle = await root.getFileHandle('sample.bmp', { create: true });
      const writable = await handle.createWritable(); await writable.write(new Uint8Array(length).fill(71)); await writable.close();
      window.showOpenFilePicker = async () => [handle];
      window.quotaCalls = 0; navigator.storage.estimate = async () => { window.quotaCalls++; return { quota: 0, usage: 0 }; };
      document.querySelector('input').id = 'ls-upload-archive'; document.querySelector('label').htmlFor = 'ls-upload-archive';
    }, bytes.length);
    await f.page.getByText('上传', { exact: true }).click();
    await waitFor(() => f.job.offsets[0] > 0);
    assert.equal(await f.page.evaluate(() => window.quotaCalls), 0);
    await f.open('/after-handle-refresh'); await waitFor(() => f.job.state === 'processing');
    assert.deepEqual(Buffer.concat(f.received), bytes); assert.equal(f.completions(), 1);
  } finally { await f.close(); }
});
