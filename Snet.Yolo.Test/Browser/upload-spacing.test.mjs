import assert from 'node:assert/strict';
import test from 'node:test';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { chromium } from 'playwright';

const root = new URL('../../Snet.Yolo.Tasks.Shared/wwwroot/', import.meta.url);
const styles = await Promise.all(['lib/bootstrap/css/bootstrap.min.css', 'css/ls.css'].map(path => readFile(new URL(path, root), 'utf8')));

/** 按公共组件的阶段生成进度框 DOM，保留真实动画及按钮尺寸。 */
function banner(state) {
  const active = !['paused', 'failed'].includes(state);
  return `<div class="ls-upload-status ${state === 'failed' ? 'ls-upload-status-error' : ''}" role="status">
    <div class="ls-upload-status-head"><span><span class="${active ? 'spinner-border spinner-border-sm' : 'bi bi-pause-circle'} me-2"></span>上传文件 289 MB / 975 MB</span><strong>29%</strong></div>
    <div class="progress ls-progress ls-upload-progress"><div class="progress-bar ${state === 'preparing' ? 'ls-upload-source-preparing' : active ? 'progress-bar-striped progress-bar-animated' : ''}" style="${state === 'preparing' ? '' : 'width:29%'}"></div></div>
    <div class="ls-upload-actions"><span>3 / 13</span><button class="btn btn-outline-secondary btn-sm">取消</button></div></div>`;
}

/** 使用各入口的真实容器类及相邻次序，覆盖双横幅与窄屏参数换行。 */
function markup(kind, state) {
  const status = banner(state), toolbar = '<div class="ls-val-toolbar">识别</div>';
  const fields = '<div class="ls-val-fields"><div class="ls-param"><label>置信度:</label><input class="form-control form-control-sm" value="0.2"></div></div>';
  const body = '<div class="ls-val-body"><div class="ls-val-result-panel">异常区域</div><div class="ls-val-image-panel">文件数量</div><div class="ls-val-imgs">图片预览</div></div>';
  if (['yolo', 'anomalib', 'ffmpeg'].includes(kind)) {
    const params = `<div class="ls-val-params">${fields}</div>`;
    const options = '<div class="ls-anomalib-inference-options">判定阈值与最小异常面积</div>';
    return `<div class="ls-refined-page ls-validation-page"><main class="ls-val-console">${toolbar}${kind === 'anomalib' ? options : ''}${status}${kind === 'ffmpeg' ? status : ''}${kind === 'anomalib' ? '' : params}${body}</main></div>`;
  }
  if (kind === 'joint') return `<div class="ls-refined-page ls-joint-page"><section class="ls-card ls-workspace-card ls-joint-preview-card"><div class="ls-joint-actions"><button class="btn btn-primary btn-sm">联合识别</button></div>${status}<div class="ls-joint-preview-body"><div>文件数量</div><div>图片预览</div></div></section></div>`;
  if (kind === 'project') return `<div class="ls-content"><div class="ls-page-head">项目图片</div>${status}<div class="ls-card">已上传图片</div></div>`;
  if (kind === 'model') return `<div class="ls-refined-page"><div class="modal show d-block"><div class="modal-dialog"><div class="modal-content"><div class="modal-body"><label class="ls-upload-zone">模型文件</label>${status}</div></div></div></div></div>`;
  return `<div class="ls-refined-page ls-settings-page"><div class="ls-workspace-card-body"><label class="btn btn-outline-secondary btn-sm">上传品牌图片</label>${status}<p class="ls-hint mt-2">PNG、JPEG 或 WebP</p></div></div>`;
}

test('upload banners keep equal insets and one layout gap in every upload container', { timeout: 40000 }, async () => {
  const server = createServer((req, res) => {
    const url = new URL(req.url, 'http://localhost');
    res.setHeader('Content-Type', 'text/html; charset=utf-8');
    res.end(`<!doctype html><html data-bs-theme="${url.searchParams.get('theme')}"><meta charset="utf-8"><style>${styles.join('\n')}</style><body style="padding:12px">${markup(url.searchParams.get('kind'), url.searchParams.get('state'))}</body></html>`);
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const browser = await chromium.launch({ headless: true });
  const evidence = [], failures = [];
  const folder = new URL('../bin/upload-spacing/', import.meta.url);
  await mkdir(folder, { recursive: true });
  try {
    const page = await browser.newPage();
    for (const width of [1987, 390]) for (const theme of ['dark', 'light']) for (const kind of ['yolo', 'anomalib', 'joint', 'project', 'model', 'logo', 'ffmpeg']) for (const state of ['preparing', 'uploading', 'processing', 'paused', 'failed']) {
      await page.setViewportSize({ width, height: 950 });
      await page.goto(`http://127.0.0.1:${server.address().port}/?kind=${kind}&theme=${theme}&state=${state}`);
      // 排除页面入场 transform 对父子坐标的瞬时影响，上传动画仍继续运行。
      await page.locator('.ls-upload-status').first().waitFor();
      await page.evaluate(async () => {
        for (const animation of document.getAnimations()) if (Number.isFinite(animation.effect.getComputedTiming().iterations)) animation.finish();
        await new Promise(requestAnimationFrame);
      });
      const measured = await page.locator('.ls-upload-status').evaluateAll(banners => banners.map(banner => {
        const css = getComputedStyle(banner), rect = banner.getBoundingClientRect();
        const parent = banner.parentElement, box = parent.getBoundingClientRect(), parentCss = getComputedStyle(parent);
        const previous = banner.previousElementSibling, next = banner.nextElementSibling;
        const nextContent = next?.firstElementChild?.getBoundingClientRect();
        return {
          padding: ['Top', 'Right', 'Bottom', 'Left'].map(side => parseFloat(css['padding' + side])),
          top: rect.top - previous.getBoundingClientRect().bottom,
          left: rect.left - box.left - parseFloat(parentCss.borderLeftWidth),
          right: box.right - rect.right - parseFloat(parentCss.borderRightWidth),
          bottom: next ? next.getBoundingClientRect().top - rect.bottom : null,
          contentGap: nextContent ? nextContent.top - rect.bottom : null,
          overflow: banner.scrollWidth > banner.clientWidth,
        };
      }));
      evidence.push({ width, theme, kind, state, measured });
      try {
        for (const item of measured) {
          assert.ok(item.padding.every(value => Math.abs(value - 12) < .1), `padding ${item.padding}`);
          assert.equal(item.overflow, false, 'banner must fit narrow screens');
          if (['yolo', 'anomalib', 'ffmpeg'].includes(kind)) {
            for (const side of ['top', 'left', 'right', 'bottom']) assert.ok(Math.abs(item[side] - 12) < .1, `${side}: ${item[side]}`);
            if (item.contentGap !== null && kind !== 'ffmpeg') assert.ok(Math.abs(item.contentGap - 12) < .1, `content gap: ${item.contentGap}`);
          }
        }
      } catch (error) { failures.push(`${kind}/${theme}/${width}: ${error.message}`); }
      if (state === 'uploading' && theme === 'dark' && ['yolo', 'anomalib', 'joint'].includes(kind))
        await page.screenshot({ path: new URL(`${kind}-${width}.png`, folder).pathname.replace(/^\/([A-Za-z]:)/, '$1') });
    }
    await writeFile(new URL('measurements.json', folder), JSON.stringify(evidence, null, 2));
    assert.deepEqual(failures, []);
  } finally { await browser.close(); server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
});
