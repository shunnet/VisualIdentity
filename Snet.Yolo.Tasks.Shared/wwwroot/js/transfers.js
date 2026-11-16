// 持久上传：缓存文件/权限句柄，按服务端确认的偏移分块；不绑定 Blazor 电路。
let owner = '', database, token, initialized = false, caching = 0;
const selected = new Map(), running = new Set(), controllers = new Map();
const chunkBytes = 4 * 1024 * 1024;

/** 打开仅保存本应用传输来源和下载记录的数据库。 */
function db() {
  return database ??= new Promise((resolve, reject) => {
    const request = indexedDB.open('snet.transfers', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('jobs', { keyPath: 'id' });
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

/** 事务完成才视为来源已持久保存，不能仅等待 put 请求成功。 */
async function store(mode, action) {
  const connection = await db();
  return new Promise((resolve, reject) => {
    const transaction = connection.transaction('jobs', mode);
    const request = action(transaction.objectStore('jobs'));
    transaction.oncomplete = () => resolve(request?.result);
    transaction.onerror = transaction.onabort = () => reject(transaction.error ?? new Error('浏览器无法持久保存上传来源。'));
  });
}

/** 携带同源身份和防伪令牌；认证失败明确暂停，不能把登录 HTML 当作成功响应。 */
async function api(url, options = {}) {
  if (options.method && options.method !== 'GET') {
    if (!token) {
      const response = await fetch('/api/transfers/token', { credentials: 'same-origin', redirect: 'error' });
      if (!response.ok) throw new Error('请重新登录后继续传输。');
      token = (await response.json()).token;
    }
    options.headers = { ...options.headers, RequestVerificationToken: token };
  }
  const response = await fetch(url, { credentials: 'same-origin', redirect: 'error', ...options });
  if (!response.ok) {
    if (response.status === 400 || response.status === 401 || response.status === 403) token = null;
    const error = new Error(`传输请求失败（HTTP ${response.status}）：${(await response.text()).slice(0, 240)}`);
    error.status = response.status; throw error;
  }
  return response.json();
}

/** 独立的小型传输提示；只在有等待/错误/下载准备任务时显示，不伪装成识别状态。 */
function notice(id, text, action, label = '继续') {
  let panel = document.getElementById('snet-transfer-center');
  if (!panel) {
    panel = document.createElement('aside'); panel.id = 'snet-transfer-center';
    panel.className = 'ls-transfer-center'; panel.setAttribute('aria-label', '文件传输'); document.body.append(panel);
  }
  let row = document.getElementById(`transfer-${id}`);
  if (!row) { row = document.createElement('div'); row.id = `transfer-${id}`; row.className = 'ls-transfer-item'; panel.append(row); }
  row.replaceChildren(); const message = document.createElement('span'); message.textContent = text; row.append(message);
  if (action) {
    const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-outline-secondary btn-sm';
    button.textContent = label; button.onclick = action; row.append(button);
  }
}

/** 移除已结束的传输提示。 */
function clearNotice(id) { document.getElementById(`transfer-${id}`)?.remove(); }

/** 同一标签页只调度一次；HTTP 内网使用 IndexedDB 文件，安全上下文优先保存文件句柄。 */
export async function initialize(user) {
  owner = user;
  if (initialized) return;
  initialized = true;
  document.addEventListener('change', event => {
    if (event.target.id?.startsWith('ls-upload-')) {
      const previous = selected.get(event.target.id);
      if (!previous?.handles) selected.set(event.target.id, { files: [...event.target.files] });
    }
  }, true);
  document.addEventListener('click', async event => {
    const link = event.target.closest('a[download]');
    if (link && new URL(link.href).origin === location.origin && new URL(link.href).pathname.startsWith('/api/')) {
      event.preventDefault(); queueDownload(link.href); return;
    }
    const label = event.target.closest('label[for^="ls-upload-"]');
    if (!label || label.classList.contains('disabled') || !window.showOpenFilePicker
        || !['ls-upload-archive', 'ls-upload-package', 'ls-upload-onnx'].includes(label.htmlFor)) return;
    event.preventDefault();
    const input = document.getElementById(label.htmlFor);
    try {
      const handles = await window.showOpenFilePicker({ multiple: input.multiple });
      const files = await Promise.all(handles.map(handle => handle.getFile()));
      selected.set(input.id, { files, handles });
      const transfer = new DataTransfer(); files.forEach(file => transfer.items.add(file));
      input.files = transfer.files; input.dispatchEvent(new Event('change', { bubbles: true }));
    } catch (error) { if (error.name !== 'AbortError') notice('picker', error.message); }
  }, true);
  window.addEventListener('online', restore);
  window.addEventListener('beforeunload', event => {
    if (caching > 0) { event.preventDefault(); event.returnValue = ''; }
  });
  await restore();
}

/** 恢复当前登录用户的任务，其他用户的本地记录不会被读取或提交。 */
async function restore() {
  try {
    const entries = await store('readonly', storage => storage.getAll());
    const uploads = await api('/api/transfers/uploads');
    for (const job of uploads.filter(job => job.state === 'uploading' && !entries.some(entry => entry.owner === owner && entry.id === job.id))) {
      notice(job.id, '上传来源尚未保存；请保留原上传页面。来源已丢失时，请取消后重新选择文件。', () => cancelUpload(job.id), '取消');
    }
    for (const entry of entries.filter(item => item.owner === owner)) {
      if (entry.kind === 'download') resumeDownload(entry);
      else resumeUpload(entry);
    }
  } catch (error) { notice('storage', `传输恢复不可用：${error.message}`); }
}

/** 保存来源后开始传输；立即返回 .NET，长任务不会占用 JSInterop 超时窗口。 */
export function queueUpload(id, inputId) {
  const selection = selected.get(inputId);
  persistAndUpload(id, selection).catch(async error => {
    try { await api(`/api/transfers/uploads/${id}/cancel`, { method: 'POST' }); } catch { }
    notice(id, `未开始上传：无法保存续传来源。${error.message}。大文件请使用 HTTPS 和支持文件权限的浏览器。`);
  });
}

/** 文件来源存储完成后才发送第一块，防止刷新后只能显示一个空任务。 */
async function persistAndUpload(id, selection) {
  if (!selection) throw new Error('文件选择已失效，请重新选择。');
  const job = await api(`/api/transfers/uploads/${id}`);
  const available = selection.files.map((file, index) => ({ file, handle: selection.handles?.[index] }));
  const files = job.files.map(meta => {
    const index = available.findIndex(item => item.file.name === meta.name && item.file.size === meta.size);
    if (index < 0) throw new Error('文件选择与任务不一致。');
    const item = available.splice(index, 1)[0];
    return item.handle ? { handle: item.handle, name: item.file.name, size: item.file.size, modified: item.file.lastModified }
      : { file: item.file, name: item.file.name, size: item.file.size, modified: item.file.lastModified };
  });
  const entry = { id, owner, kind: 'upload', files };
  await checkCacheSpace(files);
  caching++;
  try { await store('readwrite', storage => storage.put(entry)); }
  finally { caching--; }
  selected.delete([...selected].find(([, value]) => value === selection)?.[0]);
  resumeUpload(entry);
}

/** 只为需要复制的 File 预估配额；句柄仅保存元数据，不要求另一份大包空间。 */
async function checkCacheSpace(files) {
  const required = files.filter(item => !item.handle).reduce((total, item) => total + item.size, 0);
  if (!required) return;
  let estimate;
  try { estimate = await navigator.storage?.estimate?.(); } catch { /* 不支持估算时由原子缓存事务验证。 */ }
  if (!Number.isFinite(estimate?.quota) || !Number.isFinite(estimate?.usage)) return;
  const available = Math.max(0, estimate.quota - estimate.usage);
  if (required <= available) return;
  const size = value => value >= 1024 ** 3 ? `${(value / 1024 ** 3).toFixed(2)} GiB` : `${Math.ceil(value / 1024 ** 2)} MiB`;
  throw new Error(`浏览器缓存空间不足：本批约需 ${size(required)}，估算可用 ${size(available)}`);
}

/** 获取持久文件；权限需要手势时明确等待用户继续，不静默重新从零上传。 */
async function sourceOf(item, askPermission) {
  if (!item.handle) return item.file;
  let permission = await item.handle.queryPermission({ mode: 'read' });
  if (permission !== 'granted' && askPermission) permission = await item.handle.requestPermission({ mode: 'read' });
  if (permission !== 'granted') throw new Error('需要重新允许读取原文件，点击继续即可续传。');
  const file = await item.handle.getFile();
  if (file.size !== item.size || file.lastModified !== item.modified) throw new Error('原文件已被修改，请取消后重新上传。');
  return file;
}

/** 按权威偏移恢复，断开的分块不算完成；服务器也会截断未确认的尾部。 */
async function resumeUpload(entry, askPermission = false) {
  if (running.has(entry.id) || entry.owner !== owner) return;
  running.add(entry.id);
  const controller = new AbortController(); controllers.set(entry.id, controller);
  try {
    let job = await api(`/api/transfers/uploads/${entry.id}`);
    if (job.state !== 'uploading') {
      await store('readwrite', storage => storage.delete(entry.id)); clearNotice(entry.id); return;
    }
    const sources = [];
    // 在第一个 await 之后权限请求仍由用户的按钮操作触发；不在无手势恢复时弹窗。
    for (const item of entry.files) sources.push(await sourceOf(item, askPermission));
    for (let index = 0; index < sources.length; index++) {
      while (job.offsets[index] < sources[index].size) {
        const offset = job.offsets[index];
        try {
          job = await api(`/api/transfers/uploads/${entry.id}/${index}?offset=${offset}`, {
            method: 'PUT', body: sources[index].slice(offset, offset + chunkBytes), signal: controller.signal
          });
        } catch (error) {
          if (error.status !== 409) throw error;
          job = await api(`/api/transfers/uploads/${entry.id}`);
          if (job.state !== 'uploading') throw new Error('任务已停止或已进入导入阶段。');
        }
        const received = job.offsets.reduce((sum, count) => sum + count, 0);
        const total = entry.files.reduce((sum, file) => sum + file.size, 0);
        notice(entry.id, `上传文件 ${Math.floor(received * 100 / total)}%`, () => cancelUpload(entry.id), '取消');
      }
    }
    await api(`/api/transfers/uploads/${entry.id}/complete`, { method: 'POST' });
    await store('readwrite', storage => storage.delete(entry.id)); clearNotice(entry.id);
  } catch (error) {
    try {
      const current = await api(`/api/transfers/uploads/${entry.id}`);
      if (current.state !== 'uploading') {
        await store('readwrite', storage => storage.delete(entry.id)); clearNotice(entry.id); return;
      }
    } catch { }
    if (error.name !== 'AbortError') {
      notice(entry.id, `上传已暂停：${error.message}`, () => resumeUpload(entry, true));
      if (error.name === 'TypeError' || error.status >= 500) setTimeout(() => resumeUpload(entry), 3000);
    }
  } finally { running.delete(entry.id); controllers.delete(entry.id); }
}

/** 取消服务器任务并删除浏览器持久来源。 */
async function cancelUpload(id) {
  controllers.get(id)?.abort();
  try {
    try { await api(`/api/transfers/uploads/${id}/cancel`, { method: 'POST' }); }
    catch (error) { if (error.status !== 404) throw error; }
    await store('readwrite', storage => storage.delete(id)); clearNotice(id);
  } catch (error) { notice(id, error.message, () => cancelUpload(id), '取消'); }
}

/** 注册后台下载生成任务；浏览器下载管理器持有最终数据传输。 */
export function queueDownload(url) {
  const parsed = new URL(url, location.href);
  parsed.searchParams.set('persistent', '1');
    parsed.searchParams.set('transfer', crypto.randomUUID ? crypto.randomUUID().replaceAll('-', '') : Array.from(crypto.getRandomValues(new Uint8Array(16)), n => n.toString(16).padStart(2, '0')).join(''));
  const id = `download-${Date.now()}-${Math.random().toString(16).slice(2)}`;
  const entry = { id, owner, kind: 'download', url: parsed.pathname + parsed.search };
  store('readwrite', storage => storage.put(entry)).then(() => resumeDownload(entry))
    .catch(error => notice(id, error.message));
}

/** 页面刷新后继续等待服务端生成，完成后只通过原生链接交给浏览器。 */
async function resumeDownload(entry) {
  if (running.has(entry.id)) return;
  running.add(entry.id);
  try {
    notice(entry.id, '正在准备下载文件…');
    let result = await api(entry.url);
    while (result.state === 'preparing') {
      notice(entry.id, '正在准备下载文件…', async () => {
        await api(`/api/transfers/downloads/${result.id}/cancel`, { method: 'POST' });
      }, '取消');
      await new Promise(resolve => setTimeout(resolve, 1500));
      result = await api(`/api/transfers/downloads/${result.id}`);
    }
    if (result.state !== 'ready') throw new Error(result.error ?? '下载准备失败。');
    const link = document.createElement('a'); link.href = `/api/transfers/downloads/${result.id}/file`;
    link.style.display = 'none'; document.body.append(link); link.click(); link.remove();
    await store('readwrite', storage => storage.delete(entry.id)); clearNotice(entry.id);
  } catch (error) { notice(entry.id, `下载已暂停：${error.message}`, () => resumeDownload(entry)); }
  finally { running.delete(entry.id); }
}
