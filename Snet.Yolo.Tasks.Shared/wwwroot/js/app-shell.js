// Snet.Yolo.Tasks 外壳客户端辅助：主题与语言仅作为 <html> 属性 + localStorage；
// 状态真源在服务端（LanguageManager / 组件），本模块只做 DOM 与持久化。
const themeKey = "snet.theme";
const langKey = "snet.lang";

export function getTheme() {
  return document.documentElement.getAttribute("data-bs-theme") || "light";
}

// 已有浏览器选择优先；站点设置仅作为尚未选择时的默认值。
export function initializePreferences(defaultLanguage, defaultTheme) {
  let language = defaultLanguage;
  let theme = defaultTheme || getTheme();
  try {
    const savedLanguage = localStorage.getItem(langKey);
    const savedTheme = localStorage.getItem(themeKey);
    if (["zh-CN", "en-US"].includes(savedLanguage)) language = savedLanguage;
    if (["light", "dark"].includes(savedTheme)) theme = savedTheme;
  } catch { /* 浏览器禁用存储时仍应用当前电路的设置。 */ }
  document.documentElement.setAttribute("lang", language);
  document.documentElement.setAttribute("data-bs-theme", theme);
  return language;
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
