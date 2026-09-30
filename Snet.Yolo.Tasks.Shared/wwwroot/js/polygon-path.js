// 独立实现的闭合路径几何。curves[i] 是顶点 i 到下一顶点的两控制点。
export function copyPath(r) {
  return { pointsX: r.pointsX.slice(), pointsY: r.pointsY.slice(), curves: r.pointsX.map((_, i) => r.curves?.[i]?.slice() ?? null) };
}
const mix = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t];
export function splitCubic(a, b, c, d, t) {
  const ab = mix(a, b, t), bc = mix(b, c, t), cd = mix(c, d, t);
  const abc = mix(ab, bc, t), bcd = mix(bc, cd, t), p = mix(abc, bcd, t);
  return { point: p, left: [...ab, ...abc], right: [...bcd, ...cd] };
}
export function toggleCurve(r, edge) {
  r.curves ??= r.pointsX.map(() => null);
  const next = (edge + 1) % r.pointsX.length;
  const a = [r.pointsX[edge], r.pointsY[edge]], b = [r.pointsX[next], r.pointsY[next]];
  r.curves[edge] = r.curves[edge] ? null : [...mix(a, b, 1 / 3), ...mix(a, b, 2 / 3)];
}
export function insertVertex(r, edge, t) {
  if (r.pointsX.length >= 4096 || t <= 0.001 || t >= 0.999) { return -1; }
  r.curves ??= r.pointsX.map(() => null);
  const next = (edge + 1) % r.pointsX.length;
  const a = [r.pointsX[edge], r.pointsY[edge]], b = [r.pointsX[next], r.pointsY[next]], c = r.curves[edge];
  let p;
  if (c) {
    const split = splitCubic(a, c.slice(0, 2), c.slice(2), b, t);
    p = split.point; r.curves[edge] = split.left; r.curves.splice(edge + 1, 0, split.right);
  } else { p = mix(a, b, t); r.curves.splice(edge + 1, 0, null); }
  r.pointsX.splice(edge + 1, 0, p[0]); r.pointsY.splice(edge + 1, 0, p[1]);
  return edge + 1;
}
export function deleteVertex(r, vertex) {
  if (r.pointsX.length <= 3) { return false; }
  r.curves ??= r.pointsX.map(() => null);
  // 删除后两邻点以直线连接，不猜测用户想要的新曲线。
  r.curves[(vertex + r.pointsX.length - 1) % r.pointsX.length] = null;
  r.pointsX.splice(vertex, 1); r.pointsY.splice(vertex, 1); r.curves.splice(vertex, 1);
  return true;
}
export function moveVertex(r, original, vertex, x, y, width, height) {
  const dx = x - original.pointsX[vertex], dy = y - original.pointsY[vertex];
  r.pointsX[vertex] = x; r.pointsY[vertex] = y;
  const prev = (vertex + r.pointsX.length - 1) % r.pointsX.length;
  for (const [edge, offset] of [[vertex, 0], [prev, 2]]) {
    const c = original.curves[edge];
    if (c) {
      r.curves[edge] = c.slice();
      r.curves[edge][offset] = Math.max(0, Math.min(width, c[offset] + dx));
      r.curves[edge][offset + 1] = Math.max(0, Math.min(height, c[offset + 1] + dy));
    }
  }
}
function project(x, y, a, b) {
  const dx = b[0] - a[0], dy = b[1] - a[1], length = dx * dx + dy * dy;
  const t = length ? Math.max(0, Math.min(1, ((x - a[0]) * dx + (y - a[1]) * dy) / length)) : 0;
  return { t, distance: Math.hypot(x - a[0] - t * dx, y - a[1] - t * dy) };
}
// 仅命中测试时细分，误差不超过屏幕 0.5px，保留参数以精确分割曲线。
export function nearestEdge(r, x, y, tolerance) {
  let best = { distance: tolerance, edge: -1, t: 0 };
  function segment(a, b, edge, start, end) {
    const hit = project(x, y, a, b);
    if (hit.distance < best.distance) { best = { distance: hit.distance, edge, t: start + hit.t * (end - start) }; }
  }
  function cubic(a, b, c, d, edge, start, end, depth) {
    // 贝塞尔曲线位于控制点凸包内；远离点击位置的分支不必细分。
    const margin = best.distance;
    if (x < Math.min(a[0], b[0], c[0], d[0]) - margin || x > Math.max(a[0], b[0], c[0], d[0]) + margin
      || y < Math.min(a[1], b[1], c[1], d[1]) - margin || y > Math.max(a[1], b[1], c[1], d[1]) + margin) { return; }
    if (depth >= 12 || Math.max(project(...b, a, d).distance, project(...c, a, d).distance) <= tolerance / 16) {
      segment(a, d, edge, start, end); return;
    }
    const s = splitCubic(a, b, c, d, 0.5), middle = (start + end) / 2;
    cubic(a, s.left.slice(0, 2), s.left.slice(2), s.point, edge, start, middle, depth + 1);
    cubic(s.point, s.right.slice(0, 2), s.right.slice(2), d, edge, middle, end, depth + 1);
  }
  for (let i = 0; i < r.pointsX.length; i++) {
    const j = (i + 1) % r.pointsX.length, a = [r.pointsX[i], r.pointsY[i]], b = [r.pointsX[j], r.pointsY[j]], c = r.curves?.[i];
    if (c) { cubic(a, c.slice(0, 2), c.slice(2), b, i, 0, 1, 0); }
    else { segment(a, b, i, 0, 1); }
  }
  return best.edge < 0 ? null : best;
}
export function makePath(r) {
  const path = new Path2D();
  if (!r.pointsX?.length) { return path; }
  path.moveTo(r.pointsX[0], r.pointsY[0]);
  for (let i = 0; i < r.pointsX.length; i++) {
    const j = (i + 1) % r.pointsX.length, c = r.curves?.[i];
    if (c) { path.bezierCurveTo(...c, r.pointsX[j], r.pointsY[j]); }
    else { path.lineTo(r.pointsX[j], r.pointsY[j]); }
  }
  path.closePath(); return path;
}
