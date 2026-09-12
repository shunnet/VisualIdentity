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

export async function draw(canvasId, imageUrl, resultJson, type) {
  const c = document.getElementById(canvasId);
  if (!c) { return; }
  const wrap = c.parentElement;
  wrap?.classList.remove("drawn");
  const ctx = c.getContext("2d");
  ctx.clearRect(0, 0, c.width, c.height);
  let boxes = [];
  try { const r = JSON.parse(resultJson); boxes = r.ResultData || r.resultData || []; } catch { return; }
  const t = String(type || "ObjectDetection").toLowerCase();
  const img = new Image();
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
