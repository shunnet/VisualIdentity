// Audio waveform: decode audio, draw the waveform, and report selected ranges to Blazor.
const instances = new Map();

function create(canvasId, url, dotnet) {
  const canvas = document.getElementById(canvasId);
  if (!canvas) { throw new Error(`wave canvas not found: ${canvasId}`); }

  const context = canvas.getContext("2d");
  const controller = new AbortController();
  const state = {
    canvas,
    dotnet,
    samples: null,
    duration: 0,
    drag: null,
    dpr: window.devicePixelRatio || 1,
    controller,
    audioContext: new AudioContext(),
  };

  function draw(selection) {
    const width = canvas.clientWidth || 600;
    const height = canvas.clientHeight || 120;
    canvas.width = Math.round(width * state.dpr);
    canvas.height = Math.round(height * state.dpr);
    context.setTransform(state.dpr, 0, 0, state.dpr, 0, 0);
    context.clearRect(0, 0, width, height);

    const style = getComputedStyle(canvas);
    context.fillStyle = style.getPropertyValue("--ls-surface-1").trim() || "#171f2a";
    context.fillRect(0, 0, width, height);
    context.strokeStyle = style.getPropertyValue("--ls-accent").trim() || "#79a4f6";
    context.lineWidth = 1;

    if (state.samples) {
      const middle = height / 2;
      const step = Math.max(1, Math.floor(state.samples.length / width));
      context.beginPath();
      for (let x = 0; x < width; x++) {
        let amplitude = 0;
        const start = x * step;
        for (let index = start; index < start + step && index < state.samples.length; index++) {
          amplitude = Math.max(amplitude, Math.abs(state.samples[index]));
        }
        context.moveTo(x, middle - amplitude * middle);
        context.lineTo(x, middle + amplitude * middle);
      }
      context.stroke();
    }

    if (selection && selection.end > selection.start && state.duration > 0) {
      const start = (selection.start / state.duration) * width;
      const end = (selection.end / state.duration) * width;
      context.fillStyle = style.getPropertyValue("--ls-accent-soft").trim() || "rgba(121,164,246,.24)";
      context.fillRect(start, 0, end - start, height);
    }
  }

  function timeAt(clientX) {
    const bounds = canvas.getBoundingClientRect();
    const fraction = Math.max(0, Math.min(1, (clientX - bounds.left) / bounds.width));
    return fraction * state.duration;
  }

  canvas.addEventListener("pointerdown", (event) => {
    state.drag = { start: timeAt(event.clientX), end: timeAt(event.clientX) };
    canvas.setPointerCapture(event.pointerId);
    draw(state.drag);
  }, { signal: controller.signal });

  canvas.addEventListener("pointermove", (event) => {
    if (!state.drag) { return; }
    state.drag.end = Math.max(state.drag.start, timeAt(event.clientX));
    draw(state.drag);
  }, { signal: controller.signal });

  canvas.addEventListener("pointerup", () => {
    if (!state.drag) { return; }
    const selection = state.drag;
    state.drag = null;
    if (selection.end - selection.start > 0.05) {
      state.dotnet.invokeMethodAsync("OnWaveSelect", selection.start, selection.end).catch(() => {});
    }
    draw(null);
  }, { signal: controller.signal });

  fetch(url, { signal: controller.signal })
    .then((response) => {
      if (!response.ok) { throw new Error(`audio request failed: ${response.status}`); }
      return response.arrayBuffer();
    })
    .then((buffer) => state.audioContext.decodeAudioData(buffer))
    .then((audioBuffer) => {
      if (controller.signal.aborted) { return; }
      state.duration = audioBuffer.duration;
      const channel = audioBuffer.getChannelData(0);
      const downsample = Math.max(1, Math.floor(channel.length / 4000));
      const samples = new Float32Array(Math.ceil(channel.length / downsample));
      for (let index = 0; index < samples.length; index++) {
        let sum = 0;
        const start = index * downsample;
        const end = Math.min(start + downsample, channel.length);
        for (let sourceIndex = start; sourceIndex < end; sourceIndex++) { sum += channel[sourceIndex]; }
        samples[index] = sum / Math.max(1, end - start);
      }
      state.samples = samples;
      draw(null);
    })
    .catch((error) => { if (error.name !== "AbortError") { /* Leave an empty waveform on decode failure. */ } })
    .finally(() => {
      if (state.audioContext) { state.audioContext.close().catch(() => {}); state.audioContext = null; }
    });

  return state;
}

export function initAudioWave(canvasId, url, dotnet) {
  destroyAudioWave(canvasId);
  instances.set(canvasId, create(canvasId, url, dotnet));
}

export function destroyAudioWave(canvasId) {
  const state = instances.get(canvasId);
  if (!state) { return; }
  state.controller.abort();
  if (state.audioContext) { state.audioContext.close().catch(() => {}); state.audioContext = null; }
  instances.delete(canvasId);
}
