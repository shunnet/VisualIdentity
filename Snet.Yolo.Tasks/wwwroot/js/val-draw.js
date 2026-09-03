// 验证页：原图上绘制识别框（兼容 Snet.Yolo.Server 返回：Position="{Left,Top,Width,Height}" + Label.Name + Confidence）
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
    ctx.font = "15px system-ui, sans-serif"; ctx.lineWidth = 3;
    for (const b of boxes) {
      let x, y, w, h;
      if (typeof b.x === "number") { x = b.x; y = b.y; w = b.width || 0; h = b.height || 0; }
      else if (typeof b.Position === "string") {
        const m = b.Position.match(/Left=([-.\d]+),Top=([-.\d]+),Width=([-.\d]+),Height=([-.\d]+)/);
        if (m) { x = +m[1]; y = +m[2]; w = +m[3]; h = +m[4]; }
      }
      if (x == null) { continue; }
      const label = (b.Label && b.Label.Name) || b.label || b.Info || "?";
      const conf = (b.Confidence != null) ? " " + Math.round(b.Confidence * 100) + "%" : "";
      const txt = String(label) + conf;
      ctx.strokeStyle = "#ff3b30"; ctx.strokeRect(x, y, w, h);
      ctx.fillStyle = "#ff3b30";
      const tw = ctx.measureText(txt).width;
      ctx.fillRect(x, Math.max(0, y - 22), tw + 12, 22);
      ctx.fillStyle = "#fff"; ctx.fillText(txt, x + 5, Math.max(15, y - 6));
    }
  };
  img.src = imageUrl;
}
