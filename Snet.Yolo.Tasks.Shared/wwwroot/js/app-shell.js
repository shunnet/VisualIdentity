// Snet.Yolo.Tasks 外壳客户端辅助：主题与语言仅作为 <html> 属性 + localStorage；
// 状态真源在服务端（LanguageManager / 组件），本模块只做 DOM 与持久化。
const themeKey = "snet.theme";
const langKey = "snet.lang";

export function getTheme() {
  return document.documentElement.getAttribute("data-bs-theme") || "light";
}

export function setTheme(theme) {
  document.documentElement.setAttribute("data-bs-theme", theme);
  try { localStorage.setItem(themeKey, theme); } catch (err) { /* 隐私模式忽略 */ }
}

export function toggleTheme() {
  const next = getTheme() === "dark" ? "light" : "dark";
  setTheme(next);
  return next;
}

export function setLanguage(lang) {
  document.documentElement.setAttribute("lang", lang);
  try { localStorage.setItem(langKey, lang); } catch (err) { /* 忽略 */ }
}

function triggerDownload(blob, filename) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
}

export function downloadText(filename, text) {
  const blob = new Blob([text], { type: "text/plain;charset=utf-8" });
  triggerDownload(blob, filename);
}

export function downloadBinary(filename, base64) {
  const bin = atob(base64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) { bytes[i] = bin.charCodeAt(i); }
  const blob = new Blob([bytes], { type: "application/zip" });
  triggerDownload(blob, filename);
}