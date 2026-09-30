
// Snet.Yolo.Tasks 标注画布引擎。几何均为图像像素空间；视口变换仅作用于绘制；服务端持状态真源。
// 工具：select / rect / polygon / keypoint / ellipse / pan。
import { copyPath, makePath, nearestEdge, toggleCurve, insertVertex, deleteVertex, moveVertex } from "./polygon-path.js";
const instances = new Map();
const preloadCache = new Map();
const maxPreloadEntries = 12;

// 仅持久化界面偏好，不保存图片、提示点或未确认的标注。
export function loadSamPreferences(key) {
  try {
    const value = JSON.parse(localStorage.getItem(key));
    if (!value || !Number.isInteger(value.model) || value.model < 0 || value.model > 2147483647 || typeof value.enabled !== "boolean" ||
        (value.gpuId !== null && (!Number.isInteger(value.gpuId) || value.gpuId < 0 || value.gpuId > 2147483647))) { return null; }
    return value;
  } catch { return null; }
}

export function saveSamPreferences(key, value) {
  try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* 浏览器禁用存储时不阻断标注。 */ }
}

function maskImage(state, url) {
  if (!url || !url.startsWith("data:image/png;base64,")) { return null; }
  let image = state.maskImages.get(url);
  if (!image) {
    image = new Image(); state.maskImages.set(url, image);
    image.onload = () => { if (!state.destroyed) { state.render(); } };
    image.src = url;
  }
  return image.complete && image.naturalWidth > 0 ? image : null;
}

function rectOfCanvas(canvas) { return canvas.getBoundingClientRect(); }

function createInstance(canvasId, imageUrl, dotnetRef) {
  const canvas = document.getElementById(canvasId);
  if (!canvas) { throw new Error("canvas not found: " + canvasId); }
  const ctx = canvas.getContext("2d");
  const state = {
    canvas, ctx, dotnet: dotnetRef, image: null, naturalWidth: 0, naturalHeight: 0, imageReady: false,
    scale: 1, fitScale: 1, renderBoost: 1.5, ox: 0, oy: 0, cssWidth: 0, cssHeight: 0, mode: "select", regions: [], drag: null, overlayOpacity: 0.25,
    spaceKey: false, keyListener: null, resizeObserver: null, polygonEditMode: "reshape", polygonPending: false,
    samEnabled: false, samBusy: false, samPending: false, samPreview: null, maskImages: new Map(),
  };
  const pathCache = new WeakMap();
  function requestRender() {
    if (state.renderFrame) { return; }
    state.renderFrame = requestAnimationFrame(() => { state.renderFrame = null; if (!state.destroyed) { render(); } });
  }
  function polygonPath(region) {
    let path = pathCache.get(region);
    if (!path) { path = makePath(region); pathCache.set(region, path); }
    return path;
  }

  /**
   * 同步画布 CSS 尺寸：以容器（.ls-canvas-host）为准，并把画布自身的 CSS 尺寸显式写成像素。
   * 必须显式钉住 —— canvas 的"固有尺寸"就是后备分辨率，若不固定，提高分辨率会反过来撑大布局，
   * 下一轮又测到更大的尺寸，分辨率越滚越大。
   */
  function syncCssSize() {
    const host = canvas.parentElement ?? canvas;
    const rect = host.getBoundingClientRect();
    state.cssWidth = Math.max(1, Math.round(rect.width));
    state.cssHeight = Math.max(1, Math.round(rect.height));
    canvas.style.width = state.cssWidth + "px";
    canvas.style.height = state.cssHeight + "px";
  }
  /** 画布后备分辨率倍率 = DPR × 渲染倍率。 */
  function pixelRatio() { return (window.devicePixelRatio || 1) * state.renderBoost; }

  /**
   * 渲染倍率：始终超采样（缩小显示更锐利），并随放大级别提高 —— 否则放大只是把
   * "屏幕分辨率"的位图拉大，5120 的图放大了也是糊的。上限 4 倍，避免内存暴涨。
   */
  function applyBackingSize() {
    const ratio = pixelRatio();
    const width = Math.max(1, Math.round(state.cssWidth * ratio));
    const height = Math.max(1, Math.round(state.cssHeight * ratio));
    if (canvas.width === width && canvas.height === height) { return; }
    canvas.width = width;
    canvas.height = height;
  }

  /** 根据当前缩放倍数调整后备分辨率；变化时重建画布并重绘。 */
  /** 后备画布的总像素上限（约 1600 万像素 ≈ 64 MB），避免高分屏 + 高倍放大时内存过大。 */
  const MAX_BACKING_PIXELS = 16 * 1000 * 1000;

  function ensureRenderBoost() {
    const fit = state.fitScale > 0 ? state.fitScale : 1;
    const zoom = state.scale / fit;
    const dpr = window.devicePixelRatio || 1;
    const area = Math.max(1, state.cssWidth * state.cssHeight * dpr * dpr);
    const areaCap = Math.max(1, Math.sqrt(MAX_BACKING_PIXELS / area));
    const desired = Math.max(1.5, Math.round(zoom * 2) / 2);   // 至少 1.5 倍超采样：缩小显示更锐利
    const next = Math.max(1, Math.min(4, areaCap, desired));
    if (next === state.renderBoost) { return; }
    state.renderBoost = next;
    applyBackingSize();
    render();
  }

  function resize() {
    const dpr = window.devicePixelRatio || 1;
    syncCssSize();
    applyBackingSize();
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    if (state.imageReady) { fitView(); } else { render(); }
  }

  function toImage(clientX, clientY) {
    const rect = rectOfCanvas(canvas);
    const cssX = clientX - rect.left;
    const cssY = clientY - rect.top;
    return { x: (cssX - state.ox) / state.scale, y: (cssY - state.oy) / state.scale };
  }

  function applyTransform() {
    const ratio = pixelRatio();
    ctx.setTransform(ratio * state.scale, 0, 0, ratio * state.scale, ratio * state.ox, ratio * state.oy);
  }

  function render() {
    ctx.save();
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.restore();
    if (!state.imageReady) { return; }
    applyTransform();
    ctx.imageSmoothingEnabled = true;
    ctx.drawImage(state.image, 0, 0);
    const stroke = 1.5 / state.scale;
    for (const region of state.regions) { drawRegion(region, stroke); }
    if (state.samPreview) {
      const p = state.samPreview, mask = maskImage(state, p.dataUrl);
      ctx.save(); ctx.globalAlpha = state.overlayOpacity;
      if (mask) { ctx.drawImage(mask, 0, 0, state.naturalWidth, state.naturalHeight); }
      ctx.globalAlpha = 1; ctx.strokeStyle = "#79a3ff"; ctx.lineWidth = stroke;
      if (p.tool === "rect") { ctx.strokeRect(p.x, p.y, p.width, p.height); }
      else if (p.pointsX?.length >= 3) { ctx.stroke(makePath(p)); }
      ctx.restore();
    }
    drawPreview(stroke);
  }

  function drawRegion(region, stroke) {
    const color = region.color || "#40a0ff";
    ctx.save();
    ctx.strokeStyle = color;
    ctx.fillStyle = color;
    if (region.type === "polygonlabels") {
      if (!region.pointsX || region.pointsX.length < 2) { ctx.restore(); return; }
      ctx.globalAlpha = state.overlayOpacity;
      const path = polygonPath(region);
      ctx.fill(path);
      ctx.globalAlpha = 1;
      ctx.lineWidth = stroke;
      ctx.stroke(path);
      if (region.selected) { drawCurveHandles(region); drawVertexHandles(region.pointsX, region.pointsY); }
    } else if (region.type === "keypointlabels") {
      const r = 5 / state.scale;
      ctx.lineWidth = stroke;
      ctx.beginPath();
      ctx.arc(region.kx, region.ky, r, 0, Math.PI * 2);
      ctx.globalAlpha = state.overlayOpacity;
      ctx.fill();
      ctx.globalAlpha = 1;
      ctx.stroke();
      if (region.selected) {
        ctx.beginPath();
        ctx.moveTo(region.kx - r * 1.8, region.ky); ctx.lineTo(region.kx + r * 1.8, region.ky);
        ctx.moveTo(region.kx, region.ky - r * 1.8); ctx.lineTo(region.kx, region.ky + r * 1.8);
        ctx.stroke();
      }
    } else if (region.type === "brushlabels") {
      if (region.maskDataUrl) {
        const mask = maskImage(state, region.maskDataUrl);
        ctx.globalAlpha = state.overlayOpacity;
        if (mask) { ctx.drawImage(mask, 0, 0, state.naturalWidth, state.naturalHeight); }
        if (region.selected && region.pointsX?.length >= 3) { ctx.globalAlpha = 1; ctx.lineWidth = stroke; ctx.stroke(polygonPath(region)); }
      } else if (region.pointsX && region.pointsX.length >= 2) {
        ctx.globalAlpha = state.overlayOpacity;
        ctx.lineWidth = Math.max(1, region.brushSize || 8);
        ctx.lineCap = "round";
        ctx.lineJoin = "round";
        ctx.beginPath();
        ctx.moveTo(region.pointsX[0], region.pointsY[0]);
        for (let i = 1; i < region.pointsX.length; i++) { ctx.lineTo(region.pointsX[i], region.pointsY[i]); }
        ctx.stroke();
        ctx.globalAlpha = 1;
      }
    } else if (region.type === "ellipselabels") {
      ctx.globalAlpha = state.overlayOpacity;
      beginEllipse(region);
      ctx.fill();
      ctx.globalAlpha = 1;
      ctx.lineWidth = stroke;
      beginEllipse(region);
      ctx.stroke();
      if (region.selected) { drawRadiusHandles(region); }
    } else {
      const cx = region.x + region.width / 2;
      const cy = region.y + region.height / 2;
      ctx.translate(cx, cy);
      ctx.rotate(((region.rotation || 0) * Math.PI) / 180);
      ctx.translate(-cx, -cy);
      ctx.globalAlpha = state.overlayOpacity;
      ctx.fillRect(region.x, region.y, region.width, region.height);
      ctx.globalAlpha = 1;
      ctx.lineWidth = stroke;
      ctx.strokeRect(region.x, region.y, region.width, region.height);
      if (region.selected) {
        const hs = 5 / state.scale;
        ctx.fillStyle = "#ffffff";
        const corners = [[region.x, region.y], [region.x + region.width, region.y], [region.x, region.y + region.height], [region.x + region.width, region.y + region.height]];
        for (const c of corners) { ctx.fillRect(c[0] - hs, c[1] - hs, hs * 2, hs * 2); ctx.strokeRect(c[0] - hs, c[1] - hs, hs * 2, hs * 2); }
      }
    }
    ctx.restore();
  }

  function drawCurveHandles(region) {
    const radius = 4 / state.scale;
    for (let i = 0; i < (region.curves?.length ?? 0); i++) {
      const c = region.curves[i]; if (!c) { continue; }
      const j = (i + 1) % region.pointsX.length;
      ctx.save(); ctx.lineWidth = 1 / state.scale; ctx.setLineDash([3 / state.scale, 3 / state.scale]);
      ctx.beginPath(); ctx.moveTo(region.pointsX[i], region.pointsY[i]); ctx.lineTo(c[0], c[1]);
      ctx.moveTo(region.pointsX[j], region.pointsY[j]); ctx.lineTo(c[2], c[3]); ctx.stroke();
      ctx.setLineDash([]); ctx.fillStyle = "#ffffff";
      for (let k = 0; k < 4; k += 2) { ctx.beginPath(); ctx.arc(c[k], c[k + 1], radius, 0, Math.PI * 2); ctx.fill(); ctx.stroke(); }
      ctx.restore();
    }
  }

  function beginEllipse(region) {
    ctx.beginPath();
    ctx.ellipse(region.ex, region.ey, Math.max(1, region.rx), Math.max(1, region.ry), ((region.rotation || 0) * Math.PI) / 180, 0, Math.PI * 2);
  }

  // 将局部向量按角度旋转到画布坐标系。
  function rotateVector(x, y, degrees) {
    const angle = (degrees || 0) * Math.PI / 180;
    const cosine = Math.cos(angle);
    const sine = Math.sin(angle);
    return { x: x * cosine - y * sine, y: x * sine + y * cosine };
  }

  // 围绕指定中心旋转一个画布点。
  function rotatePoint(x, y, centerX, centerY, degrees) {
    const vector = rotateVector(x - centerX, y - centerY, degrees);
    return [centerX + vector.x, centerY + vector.y];
  }

  // 返回旋转后矩形四角，顺序为左上、右上、右下、左下。
  function rectangleHandles(region) {
    const centerX = region.x + region.width / 2;
    const centerY = region.y + region.height / 2;
    return [
      rotatePoint(region.x, region.y, centerX, centerY, region.rotation),
      rotatePoint(region.x + region.width, region.y, centerX, centerY, region.rotation),
      rotatePoint(region.x + region.width, region.y + region.height, centerX, centerY, region.rotation),
      rotatePoint(region.x, region.y + region.height, centerX, centerY, region.rotation),
    ];
  }

  // 返回旋转后椭圆四个半径把手，顺序为上、右、下、左。
  function ellipseHandles(region) {
    const rotation = region.rotation || 0;
    return [
      rotatePoint(region.ex, region.ey - region.ry, region.ex, region.ey, rotation),
      rotatePoint(region.ex + region.rx, region.ey, region.ex, region.ey, rotation),
      rotatePoint(region.ex, region.ey + region.ry, region.ex, region.ey, rotation),
      rotatePoint(region.ex - region.rx, region.ey, region.ex, region.ey, rotation),
    ];
  }

  function drawVertexHandles(xs, ys) {
    const hs = 4 / state.scale;
    ctx.fillStyle = "#ffffff";
    for (let i = 0; i < xs.length; i++) { ctx.fillRect(xs[i] - hs, ys[i] - hs, hs * 2, hs * 2); ctx.strokeRect(xs[i] - hs, ys[i] - hs, hs * 2, hs * 2); }
  }

  function drawRadiusHandles(region) {
    const hs = 4 / state.scale;
    ctx.fillStyle = "#ffffff";
    for (const handle of ellipseHandles(region)) {
      ctx.fillRect(handle[0] - hs, handle[1] - hs, hs * 2, hs * 2);
      ctx.strokeRect(handle[0] - hs, handle[1] - hs, hs * 2, hs * 2);
    }
  }

  function drawPreview(stroke) {
    if (!state.drag) { return; }
    const d = state.drag;
    ctx.save();
    ctx.globalAlpha = 0.35;
    ctx.strokeStyle = "#2f7cf6";
    ctx.lineWidth = stroke;
    if (d.type === "rect") {
      const x = Math.min(d.startX, d.curX), y = Math.min(d.startY, d.curY);
      const w = Math.abs(d.curX - d.startX), h = Math.abs(d.curY - d.startY);
      ctx.strokeRect(x, y, w, h);
    } else if (d.type === "ellipse") {
      ctx.beginPath();
      ctx.ellipse(d.centerX, d.centerY, Math.max(2, d.radiusX), Math.max(2, d.radiusY), 0, 0, Math.PI * 2);
      ctx.stroke();
    } else if (d.type === "brush") {
      if (d.points.length >= 2) {
        ctx.lineWidth = 12;
        ctx.lineCap = "round";
        ctx.lineJoin = "round";
        ctx.beginPath();
        ctx.moveTo(d.points[0].x, d.points[0].y);
        for (let i = 1; i < d.points.length; i++) { ctx.lineTo(d.points[i].x, d.points[i].y); }
        ctx.stroke();
      }
    } else if (d.type === "polygon") {
      if (d.points.length >= 2) {
        ctx.beginPath();
        ctx.moveTo(d.points[0].x, d.points[0].y);
        for (let i = 1; i < d.points.length; i++) { ctx.lineTo(d.points[i].x, d.points[i].y); }
        if (d.preview) { ctx.lineTo(d.preview.x, d.preview.y); }
        ctx.stroke();
      }
      const hs = 2.5 / state.scale;
      ctx.fillStyle = "#2f7cf6";
      for (const pt of d.points) { ctx.fillRect(pt.x - hs, pt.y - hs, hs * 2, hs * 2); }
    }
    ctx.restore();
  }

  function notify(method) {
    const args = Array.prototype.slice.call(arguments, 1);
    try {
      state.dotnet.invokeMethodAsync.apply(state.dotnet, [method].concat(args)).catch(function (err) {
        console.error("notify fail[" + method + "]: " + (err && err.message ? err.message : err));
      });
    } catch (err) {
      console.error("notify throw[" + method + "]: " + (err && err.message ? err.message : err));
    }
  }

  function setLocalSelected(id) {
    for (const r of state.regions) { r.selected = r.id === id; }
  }

  function polygonContains(r, x, y) {
    ctx.save(); ctx.setTransform(1, 0, 0, 1, 0, 0);
    const hit = ctx.isPointInPath(polygonPath(r), x, y);
    ctx.restore(); return hit;
  }

  function hitTest(x, y) {
    const margin = 6 / state.scale;
    for (let i = state.regions.length - 1; i >= 0; i--) {
      const r = state.regions[i];
      if (r.type === "rectanglelabels") {
        const centerX = r.x + r.width / 2;
        const centerY = r.y + r.height / 2;
        const local = rotatePoint(x, y, centerX, centerY, -(r.rotation || 0));
        if (local[0] >= r.x - margin && local[0] <= r.x + r.width + margin && local[1] >= r.y - margin && local[1] <= r.y + r.height + margin) { return r; }
      } else if (r.type === "polygonlabels") {
        if (r.pointsX && r.pointsX.length >= 3 && polygonContains(r, x, y)) { return r; }
      } else if (r.type === "keypointlabels") {
        if (Math.hypot(x - r.kx, y - r.ky) <= Math.max(8 / state.scale, 5)) { return r; }
      } else if (r.type === "brushlabels" && r.pointsX && r.pointsX.length >= 2) {
        const minX = Math.min.apply(null, r.pointsX), maxX = Math.max.apply(null, r.pointsX), minY = Math.min.apply(null, r.pointsY), maxY = Math.max.apply(null, r.pointsY);
        if (x >= minX - margin && x <= maxX + margin && y >= minY - margin && y <= maxY + margin) { return r; }
      } else if (r.type === "ellipselabels") {
        const local = rotatePoint(x, y, r.ex, r.ey, -(r.rotation || 0));
        const nx = (local[0] - r.ex) / Math.max(1, r.rx);
        const ny = (local[1] - r.ey) / Math.max(1, r.ry);
        if (nx * nx + ny * ny <= 1.15) { return r; }
      }
    }
    return null;
  }

  // 角点命中：selected 矩形/椭圆的角把手（返回 {region, corner}）
  function hitCorner(x, y) {
    const hs = 8 / state.scale;
    const sel = state.regions.find((r) => r.selected);
    if (!sel) { return null; }
    if (sel.type === "rectanglelabels") {
      const corners = rectangleHandles(sel);
      for (let k = 0; k < 4; k++) { if (Math.abs(x - corners[k][0]) <= hs && Math.abs(y - corners[k][1]) <= hs) { return { region: sel, corner: k }; } }
    } else if (sel.type === "ellipselabels") {
      const corners = ellipseHandles(sel);
      for (let k = 0; k < 4; k++) { if (Math.abs(x - corners[k][0]) <= hs && Math.abs(y - corners[k][1]) <= hs) { return { region: sel, corner: k }; } }
    } else if (sel.type === "polygonlabels" && sel.pointsX && sel.pointsX.length >= 3) {
      for (let k = 0; k < sel.pointsX.length; k++) { if (Math.abs(x - sel.pointsX[k]) <= hs && Math.abs(y - sel.pointsY[k]) <= hs) { return { region: sel, vertex: k }; } }
    }
    return null;
  }

  function updateResize(r, d, curX, curY) {
    if (r.type === "rectanglelabels") {
      const original = { x: d.origX, y: d.origY, width: d.origW, height: d.origH, rotation: d.origRotation };
      const opposite = rectangleHandles(original)[[2, 3, 0, 1][d.corner]];
      const signs = [[-1, -1], [1, -1], [1, 1], [-1, 1]][d.corner];
      const local = rotateVector(curX - opposite[0], curY - opposite[1], -d.origRotation);
      const width = Math.max(2, signs[0] * local.x);
      const height = Math.max(2, signs[1] * local.y);
      const centerOffset = rotateVector(signs[0] * width / 2, signs[1] * height / 2, d.origRotation);
      const centerX = opposite[0] + centerOffset.x;
      const centerY = opposite[1] + centerOffset.y;
      r.x = centerX - width / 2;
      r.y = centerY - height / 2;
      r.width = width;
      r.height = height;
    } else if (r.type === "ellipselabels") {
      const original = { ex: d.origCx, ey: d.origCy, rx: d.origRx, ry: d.origRy, rotation: d.origRotation };
      const opposite = ellipseHandles(original)[[2, 3, 0, 1][d.corner]];
      const signs = [[0, -1], [1, 0], [0, 1], [-1, 0]][d.corner];
      const local = rotateVector(curX - opposite[0], curY - opposite[1], -d.origRotation);
      if (signs[0] !== 0) {
        r.rx = Math.max(4, signs[0] * local.x / 2);
        const offset = rotateVector(signs[0] * r.rx, 0, d.origRotation);
        r.ex = opposite[0] + offset.x;
        r.ey = opposite[1] + offset.y;
      } else {
        r.ry = Math.max(4, signs[1] * local.y / 2);
        const offset = rotateVector(0, signs[1] * r.ry, d.origRotation);
        r.ex = opposite[0] + offset.x;
        r.ey = opposite[1] + offset.y;
      }
    }
  }

  function startPan(clientX, clientY, cssX, cssY) {
    state.drag = { type: "pan", startX: clientX, startY: clientY, curX: clientX, curY: clientY, moved: false, startCssX: cssX, startCssY: cssY };
  }

  function pointerDown(event) {
    if (event.button !== 0 || state.polygonPending || !state.imageReady) { return; }
    const img = toImage(event.clientX, event.clientY);
    const rect = rectOfCanvas(canvas);
    const cssX = event.clientX - rect.left;
    const cssY = event.clientY - rect.top;
    if (state.spaceKey || state.mode === "pan") { startPan(event.clientX, event.clientY, cssX, cssY); canvas.setPointerCapture(event.pointerId); return; }
    if (state.samEnabled && ["rect", "polygon", "brush"].includes(state.mode)) {
      if (state.samBusy || state.samPending) { warnSamBusy(); return; }
      if (img.x < 0 || img.y < 0 || img.x >= state.naturalWidth || img.y >= state.naturalHeight) { return; }
      state.samPending = true;
      state.dotnet.invokeMethodAsync("OnSamPoint", img.x, img.y, !event.shiftKey)
        .catch(error => console.error("SAM callback failed", error)).finally(() => { state.samPending = false; });
      return;
    }
    if (state.mode === "rect") {
      state.drag = { type: "rect", startX: img.x, startY: img.y, curX: img.x, curY: img.y, moved: false, startCssX: cssX, startCssY: cssY };
      canvas.setPointerCapture(event.pointerId);
      return;
    }
    if (state.mode === "ellipse") {
      state.drag = { type: "ellipse", centerX: img.x, centerY: img.y, radiusX: 0, radiusY: 0, moved: false, startCssX: cssX, startCssY: cssY };
      canvas.setPointerCapture(event.pointerId);
      return;
    }
    if (state.mode === "polygon") {
      if (!state.drag || state.drag.type !== "polygon") { state.drag = { type: "polygon", points: [], preview: null }; }
      if (state.drag.points.length >= 3 && Math.hypot(img.x - state.drag.points[0].x, img.y - state.drag.points[0].y) <= 8 / state.scale) { finishPolygon(); return; }
      if (state.drag.points.length >= 4096) { return; }
      state.drag.points.push({ x: img.x, y: img.y });
      render();
      return;
    }
    if (state.mode === "keypoint") {
      notify("OnKeyPoint", img.x, img.y);
      return;
    }
    if (state.mode === "brush") {
      if (!state.drag || state.drag.type !== "brush") { state.drag = { type: "brush", points: [] }; }
      state.drag.points.push({ x: img.x, y: img.y });
      canvas.setPointerCapture(event.pointerId);
      render();
      return;
    }
    // 角点缩放优先（select 模式下拖动选中区域角把手）
    if (state.mode === "select") {
      const selected = state.regions.find(r => r.selected && r.type === "polygonlabels");
      if (selected) {
        const before = copyPath(selected);
        if (state.polygonEditMode === "insert" || state.polygonEditMode === "curve") {
          const edge = nearestEdge(selected, img.x, img.y, 8 / state.scale);
          if (edge) {
            if (state.polygonEditMode === "curve") { toggleCurve(selected, edge.edge); commitPolygon(selected, before); return; }
            const vertex = insertVertex(selected, edge.edge, edge.t);
            if (vertex >= 0) {
              state.drag = { type: "vertexDrag", id: selected.id, vertex, before, original: copyPath(selected), changed: true, startCssX: cssX, startCssY: cssY };
              pathCache.delete(selected); canvas.setPointerCapture(event.pointerId); render();
            }
            return;
          }
        }
        if (state.polygonEditMode === "reshape") {
          for (let edge = 0; edge < before.curves.length; edge++) {
            const c = before.curves[edge]; if (!c) { continue; }
            for (let offset = 0; offset < 4; offset += 2) {
              if (Math.hypot(img.x - c[offset], img.y - c[offset + 1]) <= 8 / state.scale) {
                state.drag = { type: "controlDrag", id: selected.id, edge, offset, before, startCssX: cssX, startCssY: cssY };
                canvas.setPointerCapture(event.pointerId); return;
              }
            }
          }
        }
      }
      const cornerHit = hitCorner(img.x, img.y);
      if (cornerHit && cornerHit.vertex !== undefined) {
        const r = cornerHit.region, before = copyPath(r);
        if (state.polygonEditMode === "delete") {
          if (deleteVertex(r, cornerHit.vertex)) { commitPolygon(r, before); }
          return;
        }
        if (state.polygonEditMode !== "reshape") { return; }
        state.drag = { type: "vertexDrag", id: r.id, vertex: cornerHit.vertex, before, original: before, startCssX: cssX, startCssY: cssY };
        canvas.setPointerCapture(event.pointerId); render(); return;
      }
      if (cornerHit) {
        const r = cornerHit.region;
        setLocalSelected(r.id);
        state.drag = { type: "cornerResize", id: r.id, corner: cornerHit.corner, origX: r.x, origY: r.y, origW: r.width || (r.rx * 2), origH: r.height || (r.ry * 2), origCx: r.ex, origCy: r.ey, origRx: r.rx, origRy: r.ry, origRotation: r.rotation || 0, startImgX: img.x, startImgY: img.y };
        canvas.setPointerCapture(event.pointerId);
        render();
        return;
      }
    }
    const hit = hitTest(img.x, img.y);
    if (hit) {
      setLocalSelected(hit.id);
      if (hit.maskDataUrl) { notify("OnRegionClicked", hit.id); render(); return; }
      state.drag = { type: "shapeMove", id: hit.id, before: hit.type === "polygonlabels" ? copyPath(hit) : null, startImgX: img.x, startImgY: img.y, moved: false, startCssX: cssX, startCssY: cssY, origX: hit.x, origY: hit.y, origPtsX: hit.pointsX ? hit.pointsX.slice() : null, origPtsY: hit.pointsY ? hit.pointsY.slice() : null, origKx: hit.kx, origKy: hit.ky, origEx: hit.ex, origEy: hit.ey, dx: 0, dy: 0 };
      notify("OnRegionClicked", hit.id);
      canvas.setPointerCapture(event.pointerId);
      render();
      return;
    }
    setLocalSelected(null);
    notify("OnRegionClicked", null);
    render();
  }

  function pointerMove(event) {
    if (!state.drag) { return; }
    const d = state.drag;
    if (d.type === "pan") {
      state.ox += event.clientX - d.curX;
      state.oy += event.clientY - d.curY;
      d.curX = event.clientX;
      d.curY = event.clientY;
      render();
      return;
    }
    const img = toImage(event.clientX, event.clientY);
    const rect = rectOfCanvas(canvas);
    const dxCss = event.clientX - rect.left - d.startCssX;
    const dyCss = event.clientY - rect.top - d.startCssY;
    d.moved = d.moved || Math.abs(dxCss) > 2 || Math.abs(dyCss) > 2;
    if (d.type === "rect") {
      d.curX = img.x;
      d.curY = img.y;
    } else if (d.type === "ellipse") {
      d.radiusX = Math.abs(img.x - d.centerX);
      d.radiusY = Math.abs(img.y - d.centerY);
    } else if (d.type === "brush") {
      const last = d.points[d.points.length - 1];
      if (!last || Math.hypot(img.x - last.x, img.y - last.y) > 1.5) { d.points.push({ x: img.x, y: img.y }); }
    } else if (d.type === "polygon") {
      d.preview = img;
    } else if (d.type === "vertexDrag") {
      const r = state.regions.find((q) => q.id === d.id);
      if (r && d.moved) { moveVertex(r, d.original, d.vertex, Math.max(0, Math.min(state.naturalWidth, img.x)), Math.max(0, Math.min(state.naturalHeight, img.y)), state.naturalWidth, state.naturalHeight); pathCache.delete(r); }
    } else if (d.type === "controlDrag") {
      const r = state.regions.find(q => q.id === d.id);
      if (r && d.moved) { r.curves[d.edge][d.offset] = Math.max(0, Math.min(state.naturalWidth, img.x)); r.curves[d.edge][d.offset + 1] = Math.max(0, Math.min(state.naturalHeight, img.y)); pathCache.delete(r); }
    } else if (d.type === "cornerResize") {
      const r = state.regions.find((q) => q.id === d.id);
      if (r) { updateResize(r, d, img.x, img.y); }
    } else if (d.type === "shapeMove" && d.moved) {
      let dx = img.x - d.startImgX;
      let dy = img.y - d.startImgY;
      if (d.before) {
        const xs = [...d.before.pointsX], ys = [...d.before.pointsY];
        for (const c of d.before.curves) { if (c) { xs.push(c[0], c[2]); ys.push(c[1], c[3]); } }
        dx = Math.max(-Math.min(...xs), Math.min(state.naturalWidth - Math.max(...xs), dx));
        dy = Math.max(-Math.min(...ys), Math.min(state.naturalHeight - Math.max(...ys), dy));
      }
      d.dx = dx; d.dy = dy;
      const region = state.regions.find((r) => r.id === d.id);
      if (region) {
        if (region.type === "rectanglelabels") { region.x = d.origX + dx; region.y = d.origY + dy; }
        else if ((region.type === "polygonlabels" || region.type === "brushlabels") && d.origPtsX) {
          region.pointsX = d.origPtsX.map((v) => v + dx);
          region.pointsY = d.origPtsY.map((v) => v + dy);
          if (d.before) { region.curves = d.before.curves.map(c => c?.map((v, k) => v + (k % 2 === 0 ? dx : dy)) ?? null); pathCache.delete(region); }
        } else if (region.type === "keypointlabels") { region.kx = d.origKx + dx; region.ky = d.origKy + dy; }
        else if (region.type === "ellipselabels") { region.ex = d.origEx + dx; region.ey = d.origEy + dy; }
      }
    }
    requestRender();
  }

  function pointerUp(event) {
    if (!state.drag) { return; }
    const d = state.drag;
    if (d.type === "vertexDrag" || d.type === "controlDrag") {
      const r = state.regions.find((q) => q.id === d.id);
      state.drag = null;
      try { canvas.releasePointerCapture(event.pointerId); } catch (err) { /* ignore */ }
      if (r && (d.moved || d.changed)) { commitPolygon(r, d.before); }
      render(); return;
    }
    if (d.type === "cornerResize") {
      state.drag = null;
      try { canvas.releasePointerCapture(event.pointerId); } catch (err) { /* ignore */ }
      const r = state.regions.find((q) => q.id === d.id);
      if (r) {
        if (r.type === "rectanglelabels") { notify("OnRegionResized", r.id, r.x, r.y, r.width, r.height); }
        else if (r.type === "ellipselabels") { notify("OnRegionResized", r.id, r.ex - r.rx, r.ey - r.ry, r.rx * 2, r.ry * 2); }
      }
      render(); return;
    }
    if (d.type === "polygon") { render(); return; }
    if (d.type === "brush") {
      const points = d.points;
      state.drag = null;
      try { canvas.releasePointerCapture(event.pointerId); } catch (err) { /* ignore */ }
      if (points.length >= 2) {
        notify("OnBrushStroke", points.map((p) => p.x), points.map((p) => p.y), 12);
      }
      render();
      return;
    }
    state.drag = null;
    try { canvas.releasePointerCapture(event.pointerId); } catch (err) { /* ignore */ }
    if (d.type === "rect") {
      const w = Math.abs(d.curX - d.startX), h = Math.abs(d.curY - d.startY);
      if (w > 3 && h > 3) { notify("OnRectDrawn", d.startX, d.startY, d.curX, d.curY); }
    } else if (d.type === "ellipse") {
      if (d.radiusX > 3 && d.radiusY > 3) { notify("OnEllipseDrawn", d.centerX, d.centerY, d.radiusX, d.radiusY); }
    } else if (d.type === "shapeMove" && d.moved) {
      if (Math.abs(d.dx) > 0.5 || Math.abs(d.dy) > 0.5) {
        const r = state.regions.find(q => q.id === d.id);
        if (r && d.before) { commitPolygon(r, d.before); }
        else { notify("OnShapeMoved", d.id, d.dx, d.dy); }
      } else if (d.before) {
        const r = state.regions.find(q => q.id === d.id);
        if (r) { Object.assign(r, d.before); pathCache.delete(r); }
      }
    }
    render();
  }

  async function commitPolygon(r, before) {
    pathCache.delete(r); render(); state.polygonPending = true;
    try { await state.dotnet.invokeMethodAsync("OnPolygonPathEdited", r.id, r.pointsX.slice(), r.pointsY.slice(), copyPath(r).curves); }
    catch (error) { Object.assign(r, before); pathCache.delete(r); console.error("polygon edit failed", error); }
    finally {
      state.polygonPending = false;
      if (!state.destroyed) {
        if (state.deferredPayload) { const payload = state.deferredPayload; state.deferredPayload = null; pushState(canvasId, payload); }
        else { render(); }
      }
    }
  }
  function cancelDrag() {
    const d = state.drag;
    if (d?.before) {
      const r = state.regions.find(q => q.id === d.id);
      if (r) { Object.assign(r, d.before); pathCache.delete(r); }
    }
    state.drag = null; render();
  }
  state.cancelDrag = cancelDrag;

  function finishPolygon() {
    if (state.drag && state.drag.type === "polygon" && state.drag.points.length >= 3) {
      const threshold = Math.max(1, 2 / state.scale);
      const points = state.drag.points.filter((point, index, all) => index === 0 || Math.hypot(point.x - all[index - 1].x, point.y - all[index - 1].y) > threshold);
      if (points.length > 2 && Math.hypot(points[0].x - points[points.length - 1].x, points[0].y - points[points.length - 1].y) <= threshold) { points.pop(); }
      state.drag = null;
      if (points.length >= 3) { notify("OnPolygonFinished", points.map((point) => point.x), points.map((point) => point.y)); }
      render();
    }
  }

  function onDblClick() {
    if (state.mode === "polygon" && !state.samEnabled) { finishPolygon(); }
  }

  function onWheel(event) {
    event.preventDefault();
    const rect = rectOfCanvas(canvas);
    zoomAt(event.clientX - rect.left, event.clientY - rect.top, Math.exp(-event.deltaY * 0.0015));
  }

  function zoomAt(cssX, cssY, factor) {
    const next = Math.min(64, Math.max(0.02, state.scale * factor));
    const worldX = (cssX - state.ox) / state.scale;
    const worldY = (cssY - state.oy) / state.scale;
    state.scale = next;
    state.ox = cssX - worldX * next;
    state.oy = cssY - worldY * next;
    ensureRenderBoost();
    render();
  }

  function fitView() {
    if (!state.imageReady) { return; }
    syncCssSize();
    applyBackingSize();
    state.scale = Math.min((state.cssWidth - 24) / state.naturalWidth, (state.cssHeight - 24) / state.naturalHeight);
    if (!isFinite(state.scale) || state.scale <= 0) { state.scale = 1; }
    state.fitScale = state.scale;   // 渲染倍率以"适应窗口"为 1 倍基准
    ensureRenderBoost();
    state.ox = (state.cssWidth - state.naturalWidth * state.scale) / 2;
    state.oy = (state.cssHeight - state.naturalHeight * state.scale) / 2;
    render();
  }

  function actualSize() {
    if (!state.imageReady) { return; }
    state.scale = 1;
    state.fitScale = 1;
    ensureRenderBoost();
    state.ox = Math.max(0, (state.cssWidth - state.naturalWidth) / 2);
    state.oy = Math.max(0, (state.cssHeight - state.naturalHeight) / 2);
    render();
  }

  function onKeyDown(event) {
    if (state.polygonPending) { return; }
    if (event.key === "Escape" && state.drag?.before) { cancelDrag(); event.preventDefault(); return; }
    if (state.drag?.before && (event.ctrlKey || event.metaKey)) { return; }
    const target = event.target;
    const editing = target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.tagName === "SELECT" || target.isContentEditable);
    const ctrl = event.ctrlKey || event.metaKey;
    const shift = event.shiftKey;
    const alt = event.altKey;
    const key = event.key;
    if (!editing && state.samEnabled && (state.samPreview || state.samBusy || state.samPending) && (key === "Enter" || key === "Escape")) {
      event.preventDefault(); notify("OnKey", key === "Enter" ? "sam-confirm" : "sam-cancel", ctrl, shift, alt); return;
    }
    let action = null;
    if (ctrl && key.toLowerCase() === "z" && shift) { action = "redo"; }
    else if (ctrl && key.toLowerCase() === "z") { action = editing ? null : "undo"; }
    else if (ctrl && key.toLowerCase() === "y") { action = "redo"; }
    else if (!ctrl && !alt && !editing) {
      if (key >= "1" && key <= "9") { action = "label:" + key; }
      else if (key === "v" || key === "V") { action = "tool:select"; }
      else if (key === "r" || key === "R") { action = "tool:rect"; }
      else if (key === "p" || key === "P") { action = "tool:polygon"; }
      else if (key === "k" || key === "K") { action = "tool:keypoint"; }
      else if (key === "o" || key === "O") { action = "tool:ellipse"; }
      else if (key === "b" || key === "B") { action = "tool:brush"; }
      else if (key === "h" || key === "H") { action = "tool:pan"; }
      else if (key === "ArrowLeft") { action = "prev"; }
      else if (key === "ArrowRight") { action = "next"; }
      else if (key === "Delete" || key === "Backspace") { action = "delete"; }
      else if (key === "Escape") { action = "escape"; }
      else if (key === "Enter" && state.mode === "polygon") { finishPolygon(); return; }
      else if (key === " ") { action = "pan-start"; }
    }
    if (key === " ") { state.spaceKey = true; canvas.style.cursor = "grab"; }
    if (action) {
      if (!(action === "undo" || action === "redo" || action === "submit" || action === "skip" || action === "pan-start")) { event.preventDefault(); }
      notify("OnKey", action, ctrl, shift, alt);
    }
  }

  function onKeyUp(event) {
    if (event.key === " ") { state.spaceKey = false; updateCursor(); }
  }

  function updateCursor() {
    if (state.mode === "pan" || state.spaceKey) { canvas.style.cursor = "grab"; }
    else if (state.mode === "rect" || state.mode === "polygon" || state.mode === "ellipse" || state.mode === "keypoint" || state.mode === "brush") { canvas.style.cursor = "crosshair"; }
    else { canvas.style.cursor = "default"; }
  }

  let lastSamBusyNotice = -Infinity;
  function warnSamBusy() {
    const now = performance.now();
    if (now - lastSamBusyNotice < 1500) { return; }
    lastSamBusyNotice = now; notify("OnSamBusy");
  }
  // 捕获禁用按钮上的点击；保留取消和滚动，避免运算期间误切换或重复提交。
  function samBusyInteraction(event) {
    if (!(state.samBusy || state.samPending) || !(event.target instanceof Element) ||
        !canvas.closest(".ls-labeling")?.contains(event.target) || event.target.closest("[data-sam-cancel]")) { return; }
    warnSamBusy();
    if (event.target.closest("button,input,select,a,canvas")) { event.preventDefault(); event.stopImmediatePropagation(); }
  }
  state.samBusyListener = samBusyInteraction;
  document.addEventListener("pointerdown", samBusyInteraction, true);
  document.addEventListener("click", samBusyInteraction, true);
  canvas.addEventListener("pointerdown", pointerDown);
  canvas.addEventListener("pointermove", pointerMove);
  canvas.addEventListener("pointerup", pointerUp);
  canvas.addEventListener("pointercancel", cancelDrag);
  canvas.addEventListener("dblclick", onDblClick);
  canvas.addEventListener("wheel", onWheel, { passive: false });
  state.canvasListeners = [
    ["pointerdown", pointerDown],
    ["pointermove", pointerMove],
    ["pointerup", pointerUp],
    ["pointercancel", cancelDrag],
    ["dblclick", onDblClick],
    ["wheel", onWheel],
  ];
  state.keyListener = { down: onKeyDown, up: onKeyUp };
  window.addEventListener("keydown", state.keyListener.down);
  window.addEventListener("keyup", state.keyListener.up);
  state.resizeObserver = new ResizeObserver(resize);
  state.resizeObserver.observe(canvas.parentElement || canvas);
  resize();

  function finishImage(image) {
    if (state.destroyed) { return; }
    canvas.dataset.imgLoaded = "1";
    canvas.dataset.imgSize = image.naturalWidth + "x" + image.naturalHeight;
    state.image = image;
    state.naturalWidth = image.naturalWidth;
    state.naturalHeight = image.naturalHeight;
    state.imageReady = true;
    fitView();
    notify("OnImageLoaded", image.naturalWidth, image.naturalHeight);
  }
  canvas.dataset.imgLoaded = "0";
  delete canvas.dataset.imgSize;
  const cachedImage = preloadCache.get(imageUrl);
  const image = cachedImage || new Image();
  image.decoding = "async";
  const onLoad = function () { finishImage(image); };
  const onError = function () {
    if (state.destroyed) { return; }
    canvas.dataset.imgLoaded = "error";
    notify("OnImageError");
  };
  state.pendingImage = { image, onLoad, onError };
  image.addEventListener("load", onLoad, { once: true });
  image.addEventListener("error", onError, { once: true });
  state.render = render;
  state.updateCursor = updateCursor;
  state.fitView = fitView;
  state.actualSize = actualSize;
  state.zoomAt = zoomAt;
  if (image.complete && image.naturalWidth > 0) { queueMicrotask(() => finishImage(image)); }
  else if (!cachedImage) { image.src = imageUrl; }
  return state;
}

function instanceOf(id) {
  const instance = instances.get(id);
  if (!instance) { throw new Error("engine not initialized: " + id); }
  return instance;
}

// 返回 Promise：图片解码结束（成功或失败）后 resolve —— 调用方据此关闭"加载中"提示，
// 大图（现场 75 MB）切换时才不会让人以为平台卡死。
export function init(canvasId, imageUrl, dotnetRef) {
  if (instances.has(canvasId)) { destroy(canvasId); }
  const instance = createInstance(canvasId, imageUrl, dotnetRef);
  instances.set(canvasId, instance);
  if (!instance || !instance.pendingImage) { return Promise.resolve(); }
  const { image, onLoad, onError } = instance.pendingImage;
  return new Promise((resolve) => {
    const done = () => { image.removeEventListener("load", done); image.removeEventListener("error", done); resolve(); };
    if (image.complete && image.naturalWidth > 0) { resolve(); return; }
    image.addEventListener("load", done, { once: true });
    image.addEventListener("error", done, { once: true });
    void onLoad; void onError;
  });
}

export function destroy(canvasId) {
  const instance = instances.get(canvasId);
  if (!instance) { return; }
  instance.destroyed = true;
  for (const image of instance.maskImages.values()) { image.onload = null; }
  instance.maskImages.clear();
  if (instance.renderFrame) { cancelAnimationFrame(instance.renderFrame); }
  if (instance.pendingImage) {
    instance.pendingImage.image.removeEventListener("load", instance.pendingImage.onLoad);
    instance.pendingImage.image.removeEventListener("error", instance.pendingImage.onError);
  }
  if (instance.samBusyListener) {
    document.removeEventListener("pointerdown", instance.samBusyListener, true);
    document.removeEventListener("click", instance.samBusyListener, true);
  }
  if (instance.keyListener) { window.removeEventListener("keydown", instance.keyListener.down); window.removeEventListener("keyup", instance.keyListener.up); }
  if (instance.resizeObserver) { instance.resizeObserver.disconnect(); }
  if (instance.canvasListeners) {
    for (const [type, fn] of instance.canvasListeners) {
      try { instance.canvas.removeEventListener(type, fn); } catch (err) { /* ignore */ }
    }
  }
  instances.delete(canvasId);
}

export function preloadImages(urls) {
  if (!Array.isArray(urls)) { return; }
  for (const url of urls) {
    if (!url || preloadCache.has(url)) { continue; }
    const image = new Image();
    image.decoding = "async";
    image.onerror = function () { preloadCache.delete(url); };
    image.src = url;
    preloadCache.set(url, image);
    while (preloadCache.size > maxPreloadEntries) {
      const oldest = preloadCache.keys().next().value;
      preloadCache.delete(oldest);
    }
  }
}

export function setMode(canvasId, mode) {
  const instance = instanceOf(canvasId);
  if (instance.drag?.before) { instance.cancelDrag(); }
  instance.mode = mode;
  instance.polygonEditMode = "reshape";
  instance.spaceKey = false;
  if (mode !== "polygon") { instance.drag = null; }
  instance.updateCursor();
  instance.render();
}

export function setPolygonEditMode(canvasId, mode) {
  const instance = instanceOf(canvasId);
  instance.cancelDrag(); instance.mode = "select";
  instance.polygonEditMode = ["reshape", "insert", "delete", "curve"].includes(mode) ? mode : "reshape";
  instance.updateCursor(); instance.render();
}

export function pushState(canvasId, payload) {
  const instance = instanceOf(canvasId);
  if (instance.polygonPending) { instance.deferredPayload = payload; return; }
  if (payload && Array.isArray(payload.regions)) {
    // 选中通知可能在拖动期间返回；不能用旧服务端几何覆盖尚未提交的编辑。
    const id = instance.drag?.before ? instance.drag.id : null;
    const editing = id && instance.regions.find(r => r.id === id);
    instance.regions = payload.regions.map(r => editing && r.id === id ? Object.assign(editing, { selected: r.selected }) : r);
  }
  if (payload && payload.overlayOpacity != null) { instance.overlayOpacity = payload.overlayOpacity; }
  if (payload && payload.samEnabled != null) {
    if (instance.samEnabled !== payload.samEnabled) { instance.cancelDrag(); }
    instance.samEnabled = payload.samEnabled; instance.samBusy = !!payload.samBusy; instance.samPreview = payload.samPreview || null;
  }
  const activeMasks = new Set(instance.regions.map(r => r.maskDataUrl).filter(Boolean));
  if (instance.samPreview) { activeMasks.add(instance.samPreview.dataUrl); }
  for (const [url, image] of instance.maskImages) { if (!activeMasks.has(url)) { image.onload = null; instance.maskImages.delete(url); } }
  instance.render();
}

export function viewportAction(canvasId, action) {
  const instance = instanceOf(canvasId);
  if (action === "fit") { instance.fitView(); }
  else if (action === "actual") { instance.actualSize(); }
  else if (action === "zoomIn") { instance.zoomAt(instance.cssWidth / 2, instance.cssHeight / 2, 1.25); }
  else if (action === "zoomOut") { instance.zoomAt(instance.cssWidth / 2, instance.cssHeight / 2, 0.8); }
}
