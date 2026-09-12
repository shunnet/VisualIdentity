// 训练页辅助：终端滚动吸附 + 复制。
// 注意：必须 export 供 import() 模块互操作（TrainPage 以 IJSObjectReference 调用）。
function atBottom(el) { return el.scrollHeight - el.scrollTop - el.clientHeight < 60; }
function toBottom(el) { try { el.scrollTop = el.scrollHeight; } catch { } }
const terminalWatches = new WeakMap();

export function scrollElToBottom(el) { if (el) { toBottom(el); } }

// 自动吸附到底部；仅当用户手动上滚（离开底部）时暂停，回到底部后恢复。
export function watchTerminal(el) {
  if (!el || terminalWatches.has(el)) { return; }
  let userScrolled = false;
  const onScroll = () => { userScrolled = !atBottom(el); };
  const observer = new MutationObserver(() => { if (!userScrolled) { toBottom(el); } });
  el.addEventListener('scroll', onScroll, { passive: true });
  observer.observe(el, { childList: true, subtree: true, characterData: true });
  terminalWatches.set(el, { observer, onScroll });
  toBottom(el);
}

export function unwatchTerminal(el) {
  const watch = el ? terminalWatches.get(el) : null;
  if (!watch) { return; }
  watch.observer.disconnect();
  el.removeEventListener('scroll', watch.onScroll);
  terminalWatches.delete(el);
}

export async function copyText(text) {
  try { await navigator.clipboard.writeText(text); return true; }
  catch {
    try {
      const ta = document.createElement('textarea');
      ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
      document.body.appendChild(ta); ta.select(); document.execCommand('copy'); ta.remove();
      return true;
    } catch { return false; }
  }
}

// 兼容普通 script 引用
window.scrollElToBottom = scrollElToBottom;
