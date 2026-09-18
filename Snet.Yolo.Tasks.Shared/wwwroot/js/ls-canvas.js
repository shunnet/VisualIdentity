
// Snet.Yolo.Tasks 标注画布引擎。几何均为图像像素空间；视口变换仅作用于绘制；服务端持状态真源。
// 工具：select / rect / polygon / keypoint / ellipse / pan。
const instances = new Map();
const preloadCache = new Map();
const maxPreloadEntries = 12;

function rectOfCanvas(canvas) { return canvas.getBoundingClientRect(); }

function createInstance(canvasId, imageUrl, dotnetRef) {
  const canvas = document.getElementById(canvasId);
  if (!canvas) { throw new Error("canvas not found: " + canvasId); }
  const ctx = canvas.getContext("2d");
  const state = {
    canvas, ctx, dotnet: dotnetRef, image: null, naturalWidth: 0, naturalHeight: 0, imageReady: false,
    scale: 1, ox: 0, oy: 0, cssWidth: 0, cssHeight: 0, mode: "select", regions: [], drag: null, overlayOpacity: 0.25,
    spaceKey: false, keyListener: null, resizeObserver: null,
  };

  function resize() {
    const rect = rectOfCanvas(canvas);
    const dpr = window.devicePixelRatio || 1;
    state.cssWidth = Math.max(1, rect.width);
    state.cssHeight = Math.max(1, rect.height);
    canvas.width = Math.round(state.cssWidth * dpr);
    canvas.height = Math.round(state.cssHeight * dpr);
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
    const dpr = window.devicePixelRatio || 1;
    ctx.setTransform(dpr * state.scale, 0, 0, dpr * state.scale, dpr * state.ox, dpr * state.oy);
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
      pathPolygon(region);
      ctx.fill();
      ctx.globalAlpha = 1;
      ctx.lineWidth = stroke;
      pathPolygon(region);
      ctx.stroke();
      if (region.selected) { drawVertexHandles(region.pointsX, region.pointsY); }
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
      if (region.pointsX && region.pointsX.length >= 2) {
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

  function pathPolygon(region) {
    ctx.beginPath();
    ctx.moveTo(region.pointsX[0], region.pointsY[0]);
    for (let i = 1; i < region.pointsX.length; i++) { ctx.lineTo(region.pointsX[i], region.pointsY[i]); }
    ctx.closePath();
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

  function pointInPolygon(x, y, xs, ys) {
    let inside = false;
    for (let i = 0, j = xs.length - 1; i < xs.length; j = i++) {
      const xi = xs[i], yi = ys[i], xj = xs[j], yj = ys[j];
      if (((yi > y) !== (yj > y)) && (x < ((xj - xi) * (y - yi)) / (yj - yi) + xi)) { inside = !inside; }
    }
    return inside;
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
        if (r.pointsX && r.pointsX.length >= 3 && pointInPolygon(x, y, r.pointsX, r.pointsY)) { return r; }
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
    if (event.button !== 0) { return; }
    const img = toImage(event.clientX, event.clientY);
    const rect = rectOfCanvas(canvas);
    const cssX = event.clientX - rect.left;
    const cssY = event.clientY - rect.top;
    if (state.spaceKey || state.mode === "pan") { startPan(event.clientX, event.clientY, cssX, cssY); canvas.setPointerCapture(event.pointerId); return; }
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
      const cornerHit = hitCorner(img.x, img.y);
      if (cornerHit && cornerHit.vertex !== undefined) {
        state.drag = { type: "vertexDrag", id: cornerHit.region.id, vertex: cornerHit.vertex };
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
      state.drag = { type: "shapeMove", id: hit.id, startImgX: img.x, startImgY: img.y, moved: false, startCssX: cssX, startCssY: cssY, origX: hit.x, origY: hit.y, origPtsX: hit.pointsX ? hit.pointsX.slice() : null, origPtsY: hit.pointsY ? hit.pointsY.slice() : null, origKx: hit.kx, origKy: hit.ky, origEx: hit.ex, origEy: hit.ey, dx: 0, dy: 0 };
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
      if (r && r.pointsX) { r.pointsX[d.vertex] = img.x; r.pointsY[d.vertex] = img.y; render(); }
    } else if (d.type === "cornerResize") {
      const r = state.regions.find((q) => q.id === d.id);
      if (r) { updateResize(r, d, img.x, img.y); render(); }
    } else if (d.type === "shapeMove" && d.moved) {
      const dx = img.x - d.startImgX;
      const dy = img.y - d.startImgY;
      d.dx = dx; d.dy = dy;
      const region = state.regions.find((r) => r.id === d.id);
      if (region) {
        if (region.type === "rectanglelabels") { region.x = d.origX + dx; region.y = d.origY + dy; }
        else if ((region.type === "polygonlabels" || region.type === "brushlabels") && d.origPtsX) {
          region.pointsX = d.origPtsX.map((v) => v + dx);
          region.pointsY = d.origPtsY.map((v) => v + dy);
        } else if (region.type === "keypointlabels") { region.kx = d.origKx + dx; region.ky = d.origKy + dy; }
        else if (region.type === "ellipselabels") { region.ex = d.origEx + dx; region.ey = d.origEy + dy; }
      }
    }
    render();
  }

  function pointerUp(event) {
    if (!state.drag) { return; }
    const d = state.drag;
    if (d.type === "vertexDrag") {
      const r = state.regions.find((q) => q.id === d.id);
      state.drag = null;
      try { canvas.releasePointerCapture(event.pointerId); } catch (err) { /* ignore */ }
      if (r && r.pointsX) { notify("OnPolygonVertexMoved", r.id, d.vertex, r.pointsX[d.vertex], r.pointsY[d.vertex]); }
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
      if (Math.abs(d.dx) > 0.5 || Math.abs(d.dy) > 0.5) { notify("OnShapeMoved", d.id, d.dx, d.dy); }
    }
    render();
  }

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
    if (state.mode === "polygon") { finishPolygon(); }
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
    render();
  }

  function fitView() {
    if (!state.imageReady) { return; }
    state.scale = Math.min((state.cssWidth - 24) / state.naturalWidth, (state.cssHeight - 24) / state.naturalHeight);
    if (!isFinite(state.scale) || state.scale <= 0) { state.scale = 1; }
    state.ox = (state.cssWidth - state.naturalWidth * state.scale) / 2;
    state.oy = (state.cssHeight - state.naturalHeight * state.scale) / 2;
    render();
  }

  function actualSize() {
    if (!state.imageReady) { return; }
    state.scale = 1;
    state.ox = Math.max(0, (state.cssWidth - state.naturalWidth) / 2);
    state.oy = Math.max(0, (state.cssHeight - state.naturalHeight) / 2);
    render();
  }

  function onKeyDown(event) {
    const target = event.target;
    const editing = target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.tagName === "SELECT" || target.isContentEditable);
    const ctrl = event.ctrlKey || event.metaKey;
    const shift = event.shiftKey;
    const alt = event.altKey;
    const key = event.key;
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

  canvas.addEventListener("pointerdown", pointerDown);
  canvas.addEventListener("pointermove", pointerMove);
  canvas.addEventListener("pointerup", pointerUp);
  canvas.addEventListener("pointercancel", pointerUp);
  canvas.addEventListener("dblclick", onDblClick);
  canvas.addEventListener("wheel", onWheel, { passive: false });
  state.canvasListeners = [
    ["pointerdown", pointerDown],
    ["pointermove", pointerMove],
    ["pointerup", pointerUp],
    ["pointercancel", pointerUp],
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
  if (instance.pendingImage) {
    instance.pendingImage.image.removeEventListener("load", instance.pendingImage.onLoad);
    instance.pendingImage.image.removeEventListener("error", instance.pendingImage.onError);
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
  instance.mode = mode;
  instance.spaceKey = false;
  if (mode !== "polygon") { instance.drag = null; }
  instance.updateCursor();
  instance.render();
}

export function pushState(canvasId, payload) {
  const instance = instanceOf(canvasId);
  if (payload && Array.isArray(payload.regions)) { instance.regions = payload.regions; }
  if (payload && payload.overlayOpacity != null) { instance.overlayOpacity = payload.overlayOpacity; }
  instance.render();
}

export function viewportAction(canvasId, action) {
  const instance = instanceOf(canvasId);
  if (action === "fit") { instance.fitView(); }
  else if (action === "actual") { instance.actualSize(); }
  else if (action === "zoomIn") { instance.zoomAt(instance.cssWidth / 2, instance.cssHeight / 2, 1.25); }
  else if (action === "zoomOut") { instance.zoomAt(instance.cssWidth / 2, instance.cssHeight / 2, 0.8); }
}
