import assert from 'node:assert/strict';
import test from 'node:test';
import { createServer } from 'node:http';
import { readFile, mkdir } from 'node:fs/promises';
import { chromium } from 'playwright';

const script = await readFile(new URL('../../Snet.Yolo.Tasks.Shared/wwwroot/js/transfers.js', import.meta.url));
const bytes = Buffer.alloc(13 * 1024 * 1024 + 37, 71);
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

// 只模拟传输协议边界；客户端脚本、IndexedDB、File、刷新、导航和浏览器下载均实际运行。
async function fixture({ declaredSize = bytes.length } = {}) {
  const job = { id: 'sample', state: 'uploading', sourcePhase: 'preparing', files: [{ name: 'sample.bmp', size: declaredSize }], offsets: [0] };
  const writes = [], received = [], downloads = new Map();
  let completions = 0;
  const server = createServer(async (req, res) => {
    try {
    const url = new URL(req.url, 'http://localhost');
    const json = value => { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(value)); };
    if (url.pathname === '/transfers.js') { res.setHeader('Content-Type', 'text/javascript'); res.end(script); return; }
    if (url.pathname === '/api/transfers/token') { json({ token: 'synthetic-csrf' }); return; }
    if (url.pathname === '/api/transfers/uploads') { json([]); return; }
    if (url.pathname === '/api/transfers/uploads/sample/source-state') {
      job.sourcePhase = url.searchParams.get('phase'); json(job); return;
    }
    if (url.pathname === '/api/transfers/uploads/sample/0/verify') {
      const chunks = []; for await (const chunk of req) chunks.push(chunk);
      const stored = Buffer.concat(received), length = Math.min(64 * 1024, job.offsets[0]);
      const expected = Buffer.concat([stored.subarray(0, length), stored.subarray(job.offsets[0] - length, job.offsets[0])]);
      if (!Buffer.concat(chunks).equals(expected)) { res.statusCode = 400; json({ error: '所选文件内容与已上传分块不一致，请选择原文件。' }); return; }
      json({ verified: true }); return;
    }
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
    if (url.pathname.match(/^\/api\/transfers\/downloads\/[^/]+\/cancel$/)) {
      const item = downloads.get(url.pathname.split('/').at(-2)); item.state = 'cancelled'; json(item); return;
    }
    if (url.pathname.match(/^\/api\/transfers\/downloads\/[^/]+$/)) {
      const item = [...downloads.values()].find(item => item.id === url.pathname.split('/').at(-1));
      item.polls++; item.preparedBytes = 128 * 1024 ** 2;
      if (item.polls >= 2 && item.state === 'preparing') item.state = 'ready'; json(item); return;
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

test('download generation reports disk bytes and cancellation removes the job without starting a browser download', { timeout: 15000 }, async () => {
  const f = await fixture(); let downloads = 0;
  f.page.on('download', () => downloads++);
  try {
    await f.open(); await f.page.getByRole('link', { name: '下载' }).click();
    await f.page.getByText(/正在生成下载文件，已生成 128.00 MiB/).waitFor();
    await f.page.getByRole('button', { name: '取消', exact: true }).click();
    await waitFor(() => [...f.downloads.values()][0]?.state === 'cancelled');
    await f.page.locator('#snet-transfer-center').getByRole('button').waitFor({ state: 'detached' });
    assert.equal(downloads, 0);
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

for (const slot of ['images', 'archive', 'onnx', 'package', 'logo']) test(`${slot}: real file handles bypass full-file cache quota and resume after reload`, { timeout: 20000 }, async () => {
  const f = await fixture();
  try {
    await f.open();
    await f.page.evaluate(async ({ length, slot }) => {
      const root = await navigator.storage.getDirectory();
      const handle = await root.getFileHandle('sample.bmp', { create: true });
      const writable = await handle.createWritable(); await writable.write(new Uint8Array(length).fill(71)); await writable.close();
      window.showOpenFilePicker = async () => [handle];
      window.quotaCalls = 0; navigator.storage.estimate = async () => { window.quotaCalls++; return { quota: 0, usage: 0 }; };
      document.querySelector('input').id = 'ls-upload-' + slot; document.querySelector('label').htmlFor = 'ls-upload-' + slot;
    }, { length: bytes.length, slot });
    await f.page.getByText('上传', { exact: true }).click();
    await waitFor(() => f.job.offsets[0] > 0);
    assert.equal(await f.page.evaluate(() => window.quotaCalls), 0);
    await f.open('/after-handle-refresh'); await waitFor(() => f.job.state === 'processing');
    assert.deepEqual(Buffer.concat(f.received), bytes); assert.equal(f.completions(), 1);
  } finally { await f.close(); }
});

test('a 24 GiB source starts sending chunks without storing a complete File in IndexedDB', { timeout: 15000 }, async () => {
  const size = 24 * 1024 ** 3;
  const f = await fixture({ declaredSize: size });
  try {
    await f.page.addInitScript(size => {
      Object.defineProperty(File.prototype, 'size', { get() { return size; } });
      const put = IDBObjectStore.prototype.put;
      IDBObjectStore.prototype.put = function(value, ...args) {
        if (value?.kind === 'upload' && value.files.some(item => item.file)) throw new Error('must not cache the complete large File');
        return put.call(this, value, ...args);
      };
    }, size);
    await f.open(); await f.page.locator('input').setInputFiles({ name: 'sample.bmp', mimeType: 'image/bmp', buffer: bytes });
    await waitFor(() => f.job.offsets[0] > 0);
    assert.equal(f.job.state, 'uploading');
    assert.equal(f.job.sourcePhase, 'uploading');
    await f.page.getByRole('button', { name: '取消', exact: true }).click();
    await waitFor(() => f.job.state === 'cancelled');
  } finally { await f.close(); }
});

test('large HTTP sources wait after reload and verify reselected bytes before continuing the checkpoint', { timeout: 25000 }, async () => {
  const f = await fixture({ declaredSize: 24 * 1024 ** 3 });
  try {
    await f.page.addInitScript(() => {
      Object.defineProperty(File.prototype, 'size', { get() { return 24 * 1024 ** 3; } });
      Object.defineProperty(File.prototype, 'lastModified', { get() { return 1; } });
    });
    await f.open(); await f.page.locator('input').setInputFiles({ name: 'sample.bmp', mimeType: 'image/bmp', buffer: bytes });
    await waitFor(() => f.job.offsets[0] > 0);
    await f.open('/after-large-refresh');
    await f.page.getByRole('button', { name: '重新选择原文件' }).waitFor();
    const checkpoint = f.job.offsets[0]; await delay(350);
    assert.equal(f.job.offsets[0], checkpoint); assert.equal(f.job.sourcePhase, 'awaiting-source');
    const reselect = async buffer => {
      const chooser = f.page.waitForEvent('filechooser');
      await f.page.getByRole('button', { name: '重新选择原文件' }).click();
      await (await chooser).setFiles({ name: 'sample.bmp', mimeType: 'image/bmp', buffer });
    };
    await reselect(Buffer.alloc(bytes.length, 72));
    await f.page.getByText(/所选文件内容与已上传分块不一致/).waitFor();
    assert.equal(f.job.offsets[0], checkpoint);
    await reselect(bytes); await waitFor(() => f.job.offsets[0] > checkpoint);
    assert.equal(f.writes.filter(offset => offset === 0).length, 1);
    await f.page.getByRole('button', { name: '取消', exact: true }).click();
    await waitFor(() => f.job.state === 'cancelled');
  } finally { await f.close(); }
});

// 持续发出真实 IndexedDB 读请求，模拟来源复制卡住而事务尚未完成。
async function holdSourceTransaction(page, shortenTimeout) {
  await page.addInitScript(shortenTimeout => {
    const put = IDBObjectStore.prototype.put;
    IDBObjectStore.prototype.put = function(value, ...args) {
      if (value?.kind !== 'upload') return put.call(this, value, ...args);
      const keepAlive = () => { const request = this.get('__hold'); request.onsuccess = keepAlive; return request; };
      return keepAlive();
    };
    if (shortenTimeout) {
      const timeout = window.setTimeout;
      window.setTimeout = (callback, ms, ...args) => timeout(callback, ms === 30000 ? 150 : ms, ...args);
    }
  }, shortenTimeout);
}

test('a stalled source transaction times out explicitly instead of spinning at zero forever', { timeout: 15000 }, async () => {
  const f = await fixture();
  try {
    await holdSourceTransaction(f.page, true); await f.open();
    await f.page.locator('input').setInputFiles({ name: 'sample.bmp', mimeType: 'image/bmp', buffer: bytes });
    await waitFor(() => f.job.state === 'cancelled');
    await f.page.getByText(/准备续传来源超时/).waitFor(); assert.equal(f.writes.length, 0);
  } finally { await f.close(); }
});

test('cancel interrupts source preparation before the first network chunk', { timeout: 15000 }, async () => {
  const f = await fixture();
  try {
    await holdSourceTransaction(f.page, false); await f.open();
    await f.page.locator('input').setInputFiles({ name: 'sample.bmp', mimeType: 'image/bmp', buffer: bytes });
    await f.page.getByText('正在准备续传来源，尚未开始网络上传…').waitFor();
    await f.page.getByRole('button', { name: '取消', exact: true }).click();
    await waitFor(() => f.job.state === 'cancelled');
    await f.page.locator('#snet-transfer-center').getByRole('button').waitFor({ state: 'detached' });
    assert.equal(f.writes.length, 0);
  } finally { await f.close(); }
});
