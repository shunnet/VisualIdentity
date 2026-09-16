// 验证页：原图上绘制识别结果（兼容 Snet.Yolo.Server 返回的结构与五种任务类型的绘制）
//  - ObjectDetection: 矩形框 + 标签
//  - Segmentation   : 掩码(bit-packed) + 框 + 标签（掩码格式不符时回退半透明框）
//  - ObbDetection   : 旋转矩形（OrientationAngle）+ 标签
//  - PoseEstimation : 框 + 关键点（圆点 + 序号）
//  - Classification : 无框，顶部横幅显示 标签 + 置信度
function num(v, dflt) { const n = Number(v); return Number.isFinite(n) ? n : dflt; }
function pick(o, keys, dflt) { for (const k of keys) { if (o != null && o[k] != null) return o[k]; } return dflt; }
function bboxOf(b) {
  if (typeof b.x === "number" && b.width != null) return { x: b.x, y: b.y, w: b.width, h: b.height };
  if (typeof b.Y !== "undefined" && typeof b.X !== "undefined" && typeof b.Width !== "undefined") return { x: b.X, y: b.Y, w: b.Width, h: b.Height };
  if (typeof b.Position === "string") {
    const m = b.Position.match(/Left=([-.\d]+),Top=([-.\d]+),Width=([-.\d]+),Height=([-.\d]+)/);
    if (m) return { x: +m[1], y: +m[2], w: +m[3], h: +m[4] };
    if (typeof b.BoundingBox === "string") {
      const mm = b.BoundingBox.match(/Left=([-.\d]+),Top=([-.\d]+),Width=([-.\d]+),Height=([-.\d]+)/);
      if (mm) return { x: +mm[1], y: +mm[2], w: +mm[3], h: +mm[4] };
    }
  }
  if (typeof b.BoundingBox === "object" && b.BoundingBox) {
    const bb = b.BoundingBox, attrs = ["X", "Left", "XCoordinate"];
    return { x: num(pick(bb, ["X", "Left", "XCoordinate"]), NaN), y: num(pick(bb, ["Y", "Top"]), NaN), w: num(pick(bb, ["Width", "W"]), NaN), h: num(pick(bb, ["Height", "H"]), NaN) };
  }
  return null;
}
function coordOf(kp) {
  const co = kp.Coordinates || kp.coordinates || kp;
  return { x: num(pick(co, ["X", "x", "Left"], NaN), NaN), y: num(pick(co, ["Y", "y", "Top"], NaN), NaN) };
}
function labelOf(b) {
  const lab = pick(b, ["Label", "label", "Info", "InfoText"], null);
  if (lab != null) { return typeof lab === "string" ? lab : (lab.Name ?? lab.name ?? "?"); }
  return "?";
}
function drawRotated(ctx, cx, cy, w, h, deg) {
  ctx.save(); ctx.translate(cx, cy); ctx.rotate(deg * Math.PI / 180);
  ctx.strokeRect(-w / 2, -h / 2, w, h); ctx.restore();
}
// bit-packed 掩码（1 bit/像素, MSB first, 每行按字节补齐）→ 画到整图
function drawMask(ctx, maskBytes, bb) {
  const bw = Math.round(bb.w), bh = Math.round(bb.h);
  if (!maskBytes || !bw || !bh) return false;
  const rowBytes = Math.ceil(bw / 8);
  if (maskBytes.length < bh * rowBytes) return false;
  try {
    const img = ctx.getImageData(Math.round(bb.x), Math.round(bb.y), bw, bh);
    const d = img.data;
    for (let yy = 0; yy < bh; yy++) {
      for (let xx = 0; xx < bw; xx++) {
        const byte = maskBytes[yy * rowBytes + (xx >> 3)];
        if (byte & (0x80 >> (xx & 7))) {
          const i4 = (yy * bw + xx) * 4;
          d[i4] = 255; d[i4 + 1] = 45; d[i4 + 2] = 85; d[i4 + 3] = 120;
        }
      }
    }
    ctx.putImageData(img, Math.round(bb.x), Math.round(bb.y));
    return true;
  } catch { return false; }
}

export async function draw(canvasId, imageUrl, resultJson, type, showAnnotations = true) {
  const c = document.getElementById(canvasId);
  if (!c) { return; }
  const wrap = c.parentElement;
  wrap?.classList.remove("drawn");
  const ctx = c.getContext("2d");
  ctx.clearRect(0, 0, c.width, c.height);
  let boxes = [];
  // showAnnotations=false 时只画原图（大图查看器的"原图"勾选）
  if (showAnnotations !== false) {
    try { const r = JSON.parse(resultJson); boxes = r.ResultData || r.resultData || []; } catch { return; }
  }
  const t = String(type || "ObjectDetection").toLowerCase();
  const img = new Image();
  // 图片解码结束（无论成功失败）后 resolve：调用方 await draw(...) 返回时画布内容已经就绪
  const painted = new Promise((resolve) => {
    img.addEventListener("load", resolve, { once: true });
    img.addEventListener("error", resolve, { once: true });
  });
  img.onload = () => {
    c.width = img.naturalWidth; c.height = img.naturalHeight;
    ctx.drawImage(img, 0, 0);
    ctx.font = "15px system-ui, sans-serif"; ctx.lineWidth = 3;
    for (const b of boxes) {
      const bb = bboxOf(b);
      const base = labelOf(b);
      const conf = b.Confidence != null ? " " + Math.round(num(b.Confidence, 0) * 100) + "%" : "";
      const txt = String(base) + conf;
      ctx.strokeStyle = "#ff3b30"; ctx.fillStyle = "#ff3b30";
      // 分类：无框 → 顶部横幅
      if (t.includes("class")) {
        ctx.font = "600 18px system-ui, sans-serif";
        const w2 = ctx.measureText(txt).width + 28;
        const x0 = Math.max(4, c.width / 2 - w2 / 2);
        ctx.fillStyle = "rgba(0,0,0,.62)";
        ctx.beginPath(); ctx.roundRect(x0, 8, w2, 34, 8); ctx.fill();
        ctx.fillStyle = "#fff"; ctx.fillText(txt, x0 + 14, 31);
        continue;
      }
      if (!bb) { continue; }
      // 分割：先掩码，再框
      if (t.includes("segment")) {
        const mask = pick(b, ["BitPackedPixelMask", "bitPackedPixelMask"], null);
        if (!drawMask(ctx, mask, bb)) {
          ctx.fillStyle = "rgba(255,59,48,.18)"; ctx.fillRect(bb.x, bb.y, bb.w, bb.h);
        }
        ctx.strokeStyle = "#ff3b30";
      }
      // OBB：旋转框（默认正框也可用）
      if (t.includes("obb") && b.OrientationAngle != null) {
        drawRotated(ctx, bb.x + bb.w / 2, bb.y + bb.h / 2, bb.w, bb.h, num(b.OrientationAngle, 0));
      } else {
        ctx.strokeStyle = "#ff3b30"; ctx.strokeRect(bb.x, bb.y, bb.w, bb.h);
      }
      const tw = ctx.measureText(txt).width;
      ctx.fillStyle = "#ff3b30";
      ctx.fillRect(bb.x, Math.max(0, bb.y - 22), tw + 12, 22);
      ctx.fillStyle = "#fff"; ctx.fillText(txt, bb.x + 5, Math.max(15, bb.y - 6));
      // 姿态：关键点
      if (t.includes("pose") && Array.isArray(b.KeyPoints)) {
        let pi = 0;
        for (const kp of b.KeyPoints) {
          const co = coordOf(kp);
          const kx = co.x, ky = co.y;
          const px = Number.isFinite(kx) ? (kx < 2 ? kx * c.width : kx) : NaN;
          const py = Number.isFinite(ky) ? (ky < 2 ? ky * c.height : ky) : NaN;
          if (Number.isFinite(px) && Number.isFinite(py) && px >= 0 && py >= 0) {
            ctx.beginPath(); ctx.arc(px, py, 5, 0, Math.PI * 2);
            ctx.fillStyle = "#22c55e"; ctx.fill();
            ctx.strokeStyle = "#fff"; ctx.lineWidth = 1.5; ctx.stroke();
            ctx.fillStyle = "#fff"; ctx.font = "11px system-ui, sans-serif";
            ctx.fillText(String(++pi), px + 7, py - 5);
            ctx.lineWidth = 3;
          }
        }
      }
    }
    wrap?.classList.add("drawn");
  };
  img.onerror = () => {};
  img.src = imageUrl;
  await painted;
}

const videoOverlays = new Map();

/** Displays time-synchronized detection boxes over a playable video. */
export function showVideoAnnotations(videoId, canvasId, frameResults, type) {
  const video = document.getElementById(videoId);
  const canvas = document.getElementById(canvasId);
  if (!(video instanceof HTMLVideoElement) || !(canvas instanceof HTMLCanvasElement)) { return; }
  videoOverlays.get(canvasId)?.dispose();

  const frames = (frameResults || []).map(frame => {
    let result = {};
    try { result = JSON.parse(frame.resultJson ?? frame.ResultJson ?? "{}"); } catch { }
    return {
      time: Number(frame.timeSeconds ?? frame.TimeSeconds ?? 0),
      boxes: result.ResultData || result.resultData || [],
    };
  }).sort((left, right) => left.time - right.time);
  const context = canvas.getContext("2d");
  let animationFrame = 0;

  const resize = () => {
    if (!video.videoWidth || !video.videoHeight) { return; }
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    const videoRect = video.getBoundingClientRect();
    const parentRect = canvas.parentElement.getBoundingClientRect();
    canvas.style.left = `${videoRect.left - parentRect.left}px`;
    canvas.style.top = `${videoRect.top - parentRect.top}px`;
    canvas.style.width = `${videoRect.width}px`;
    canvas.style.height = `${videoRect.height}px`;
  };

  const render = () => {
    resize();
    context.clearRect(0, 0, canvas.width, canvas.height);
    if (frames.length > 0) {
      let nearest = frames[0];
      for (const frame of frames) {
        if (Math.abs(frame.time - video.currentTime) <= Math.abs(nearest.time - video.currentTime)) { nearest = frame; }
      }
      context.font = "15px system-ui, sans-serif";
      context.lineWidth = 3;
      for (const detection of nearest.boxes) {
        const box = bboxOf(detection);
        if (!box) { continue; }
        const confidence = detection.Confidence ?? detection.confidence;
        const text = `${labelOf(detection)}${confidence == null ? "" : ` ${Math.round(Number(confidence) * 100)}%`}`;
        context.strokeStyle = "#ff3b30";
        context.fillStyle = "#ff3b30";
        context.strokeRect(box.x, box.y, box.w, box.h);
        const textWidth = context.measureText(text).width;
        context.fillRect(box.x, Math.max(0, box.y - 22), textWidth + 12, 22);
        context.fillStyle = "#fff";
        context.fillText(text, box.x + 5, Math.max(15, box.y - 6));
      }
    }
    if (!video.paused && !video.ended) { animationFrame = requestAnimationFrame(render); }
  };
  const onPlay = () => { cancelAnimationFrame(animationFrame); render(); };
  const onTimeChange = () => render();
  const resizeObserver = new ResizeObserver(resize);
  resizeObserver.observe(video);
  video.addEventListener("play", onPlay);
  video.addEventListener("seeked", onTimeChange);
  video.addEventListener("timeupdate", onTimeChange);
  video.addEventListener("loadedmetadata", onTimeChange);
  const dispose = () => {
    cancelAnimationFrame(animationFrame);
    resizeObserver.disconnect();
    video.removeEventListener("play", onPlay);
    video.removeEventListener("seeked", onTimeChange);
    video.removeEventListener("timeupdate", onTimeChange);
    video.removeEventListener("loadedmetadata", onTimeChange);
  };
  videoOverlays.set(canvasId, { dispose });
  render();
}

/** Keeps the validation log viewport pinned to the newest entry. */
export function scrollToBottom(elementId) {
  const element = document.getElementById(elementId);
  if (element) { element.scrollTop = element.scrollHeight; }
}

/* ------------------------------------------------------------------------- *
 * 验证页大图查看器（只用于图片，视频不走这里）：
 *   滚轮 = 以光标为中心缩放；按住拖动 = 平移；双击图片或"还原"按钮 = 恢复初始大小；
 *   Esc 或关闭按钮 = 关闭。
 * 缩放/平移用 CSS transform 实现（origin: 0 0），画布本身保持原图分辨率，所以放大后依然清晰。
 * ------------------------------------------------------------------------- */
const imageViewers = new Map();
const MIN_VIEWER_SCALE = 0.2;
const MAX_VIEWER_SCALE = 20;
/** 查看器"代次"：关闭时是后台异步卸载的，代次用于忽略迟到的卸载请求，避免拆掉刚重开的查看器。 */
let viewerGeneration = 0;

/**
 * 给弹窗里的画布装上缩放/拖动交互。
 * @param {string} stageId 视口容器（overflow:hidden、relative）
 * @param {string} canvasId 显示图片/标注的画布
 * @param {string} closeButtonId 关闭按钮 id（Esc 触发它的 click）
 * @returns {number} 本次安装的代次，卸载时原样传回 detachImageViewer
 */
export function attachImageViewer(stageId, canvasId, closeButtonId) {
  detachImageViewer();
  const stage = document.getElementById(stageId);
  const canvas = document.getElementById(canvasId);
  if (!stage || !canvas) { return 0; }

  const state = { scale: 1, x: 0, y: 0, dragging: false, pointerId: -1, lastX: 0, lastY: 0, moved: false };

  const apply = () => {
    canvas.style.transformOrigin = "0 0";
    canvas.style.transform = `translate(${state.x}px, ${state.y}px) scale(${state.scale})`;
    canvas.style.cursor = state.dragging ? "grabbing" : "grab";
  };

  const reset = () => {
    state.scale = 1; state.x = 0; state.y = 0;
    apply();
  };

  // 以光标（clientX/clientY）为锚点缩放：光标下的像素在缩放前后保持不动
  const zoomAt = (clientX, clientY, factor) => {
    const next = Math.min(MAX_VIEWER_SCALE, Math.max(MIN_VIEWER_SCALE, state.scale * factor));
    if (next === state.scale) { return; }
    const rect = stage.getBoundingClientRect();
    const cx = clientX - rect.left;
    const cy = clientY - rect.top;
    const k = next / state.scale;
    const left = canvas.offsetLeft;
    const top = canvas.offsetTop;
    state.x = cx - left - (cx - left - state.x) * k;
    state.y = cy - top - (cy - top - state.y) * k;
    state.scale = next;
    apply();
  };

  const onWheel = (event) => {
    event.preventDefault();
    let delta = event.deltaY;
    if (event.deltaMode === 1) { delta *= 16; }      // 行
    else if (event.deltaMode === 2) { delta *= 100; } // 页
    zoomAt(event.clientX, event.clientY, Math.exp(-delta * 0.0015));
  };

  const onPointerDown = (event) => {
    if (event.button !== 0 && event.pointerType === "mouse") { return; }
    state.dragging = true;
    state.moved = false;
    state.pointerId = event.pointerId;
    state.lastX = event.clientX;
    state.lastY = event.clientY;
    try { stage.setPointerCapture(event.pointerId); } catch { }
    apply();
  };

  const onPointerMove = (event) => {
    if (!state.dragging || event.pointerId !== state.pointerId) { return; }
    const dx = event.clientX - state.lastX;
    const dy = event.clientY - state.lastY;
    if (dx !== 0 || dy !== 0) { state.moved = true; }
    state.lastX = event.clientX;
    state.lastY = event.clientY;
    state.x += dx;
    state.y += dy;
    apply();
  };

  const onPointerUp = (event) => {
    if (event.pointerId !== state.pointerId) { return; }
    state.dragging = false;
    state.pointerId = -1;
    try { stage.releasePointerCapture(event.pointerId); } catch { }
    apply();
  };

  // 双击画布/视口还原（拖动结束后的 dblclick 不还原，避免误触）
  const onDoubleClick = () => { if (!state.moved) { reset(); } };

  const onKeyDown = (event) => {
    if (event.key !== "Escape") { return; }
    const close = document.getElementById(closeButtonId);
    if (!close) { dispose(); return; }   // 弹窗已消失：自行卸掉 Esc 监听
    close.click();
  };

  stage.addEventListener("wheel", onWheel, { passive: false });
  stage.addEventListener("pointerdown", onPointerDown);
  stage.addEventListener("pointermove", onPointerMove);
  stage.addEventListener("pointerup", onPointerUp);
  stage.addEventListener("pointercancel", onPointerUp);
  stage.addEventListener("dblclick", onDoubleClick);
  stage.addEventListener("dragstart", (event) => event.preventDefault());
  document.addEventListener("keydown", onKeyDown);

  let disposed = false;
  function dispose() {
    if (disposed) { return; }
    disposed = true;
    stage.removeEventListener("wheel", onWheel);
    stage.removeEventListener("pointerdown", onPointerDown);
    stage.removeEventListener("pointermove", onPointerMove);
    stage.removeEventListener("pointerup", onPointerUp);
    stage.removeEventListener("pointercancel", onPointerUp);
    stage.removeEventListener("dblclick", onDoubleClick);
    document.removeEventListener("keydown", onKeyDown);
    // 只在仍是当前这一代时移除登记（否则会把后来新装的查看器从表里删掉）
    if (imageViewers.get(stageId)?.dispose === dispose) { imageViewers.delete(stageId); }
  }

  const generation = ++viewerGeneration;
  imageViewers.set(stageId, { generation, reset, dispose });
  reset();
  return generation;
}

/** 还原到初始大小（"还原"按钮）。 */
export function resetImageViewer(stageId) {
  imageViewers.get(stageId)?.reset();
}

/**
 * 关闭弹窗时卸载交互与 Esc 监听。
 * @param {string} [stageId] 省略表示全部卸载
 * @param {number} [generation] attachImageViewer 返回的代次；传入时若已被新查看器顶替则忽略本次卸载
 */
export function detachImageViewer(stageId, generation) {
  if (stageId) {
    const viewer = imageViewers.get(stageId);
    if (!viewer) { return; }
    if (generation != null && viewer.generation !== generation) { return; }
    viewer.dispose();
    return;
  }
  for (const viewer of [...imageViewers.values()]) { viewer.dispose(); }
}
