
// 音频波形：解码音频 → 绘制波形 → 拖拽选择区间（时间秒）回传服务端。
const instances = new Map();

function create(canvasId, url, dotnet) {
  const canvas = document.getElementById(canvasId);
  if (!canvas) { throw new Error("wave canvas not found: " + canvasId); }
  const ctx = canvas.getContext("2d");
  const state = { canvas, url, dotnet, samples: null, duration: 0, drag: null, dpr: window.devicePixelRatio || 1 };

  function draw(selection) {
    const w = canvas.clientWidth || 600;
    const h = canvas.clientHeight || 120;
    canvas.width = Math.round(w * state.dpr);
    canvas.height = Math.round(h * state.dpr);
    ctx.setTransform(state.dpr, 0, 0, state.dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    ctx.fillStyle = "#22282f";
    ctx.fillRect(0, 0, w, h);
    ctx.strokeStyle = "#40a0ff";
    ctx.lineWidth = 1;
    if (state.samples) {
      const mid = h / 2;
      const step = Math.max(1, Math.floor(state.samples.length / w));
      ctx.beginPath();
      for (let x = 0; x < w; x++) {
        const amp = maxAmp(x * step, step);
        ctx.moveTo(x, mid - amp * mid);
        ctx.lineTo(x, mid + amp * mid);
      }
      ctx.stroke();
    }
    if (selection && selection.end > selection.start) {
      const x1 = (selection.start / state.duration) * w;
      const x2 = (selection.end / state.duration) * w;
      ctx.fillStyle = "rgba(64,160,255,0.30)";
      ctx.fillRect(x1, 0, x2 - x1, h);
    }
  }

  function maxAmp(start, step) {
    let max = 0;
    const s = state.samples;
    for (let i = start; i < start + step && i < s.length; i++) {
      const v = Math.abs(s[i]);
      if (v > max) { max = v; }
    }
    return max;
  }

  function timeAt(clientX) {
    const r = canvas.getBoundingClientRect();
    const frac = Math.max(0, Math.min(1, (clientX - r.left) / r.width));
    return frac * state.duration;
  }

  canvas.addEventListener("pointerdown", (e) => {
    state.drag = { start: timeAt(e.clientX), end: timeAt(e.clientX) };
    canvas.setPointerCapture(e.pointerId);
    draw(state.drag);
  });
  canvas.addEventListener("pointermove", (e) => {
    if (!state.drag) { return; }
    state.drag.end = Math.max(state.drag.start, timeAt(e.clientX));
    draw(state.drag);
  });
  canvas.addEventListener("pointerup", (e) => {
    if (!state.drag) { return; }
    const sel = state.drag;
    state.drag = null;
    if (sel.end - sel.start > 0.05) {
      state.dotnet.invokeMethodAsync("OnWaveSelect", sel.start, sel.end).catch(() => {});
    }
    draw(null);
  });

  fetch(url)
    .then((res) => res.arrayBuffer())
    .then((buf) => new AudioContext().decodeAudioData(buf))
    .then((audioBuffer) => {
      state.duration = audioBuffer.duration;
      const channel = audioBuffer.getChannelData(0);
      const downsample = Math.max(1, Math.floor(channel.length / 4000));
      const samples = new Float32Array(Math.ceil(channel.length / downsample));
      for (let i = 0; i < samples.length; i++) {
        let sum = 0;
        for (let j = 0; j < downsample; j++) { sum += channel[i * downsample + j]; }
        samples[i] = sum / downsample;
      }
      state.samples = samples;
      draw(null);
    })
    .catch(() => { /* 解码失败忽略 */ });
  return state;
}

export function initAudioWave(canvasId, url, dotnet) {
  if (instances.has(canvasId)) { instances.delete(canvasId); }
  instances.set(canvasId, create(canvasId, url, dotnet));
}
export function destroyAudioWave(canvasId) { instances.delete(canvasId); }
