// 验证页：原图上绘制识别框（x/y/width/height + label/confidence）
export async function draw(canvasId, imageUrl, resultJson) {
  const c = document.getElementById(canvasId);
  if (!c) { return; }
  const ctx = c.getContext("2d");
  ctx.clearRect(0, 0, c.width, c.height);
  let boxes = [];
  try { const r = JSON.parse(resultJson); boxes = r.ResultData || r.resultData || []; } catch { return; }
  const img = new Image();
  img.onload = () => {
    c.width = img.naturalWidth; c.height = img.naturalHeight;
    ctx.drawImage(img, 0, 0);
    ctx.font = "14px system-ui, sans-serif"; ctx.lineWidth = 3;
    for (const b of boxes) {
      if (typeof b.x !== "number") { continue; }
      const x = b.x, y = b.y, w = b.width || 0, h = b.height || 0;
      ctx.strokeStyle = "#ff3b30"; ctx.strokeRect(x, y, w, h);
      const label = (b.label || b.className || b.Info || "?").toString();
      const conf = (b.confidence ?? b.Confidence) !== undefined ? " " + Math.round((b.confidence ?? b.Confidence) * 100) + "%" : "";
      const txt = label + conf;
      ctx.fillStyle = "#ff3b30";
      ctx.fillRect(x, Math.max(0, y - 20), ctx.measureText(txt).width + 12, 20);
      ctx.fillStyle = "#fff"; ctx.fillText(txt, x + 4, Math.max(12, y - 5));
    }
  };
  img.src = imageUrl;
}
