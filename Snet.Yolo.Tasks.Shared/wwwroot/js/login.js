/**
 * 使登录表单在 Blazor 连接建立前后都能使用回车原生提交。
 * 委托到 document 可兼容组件重新渲染后替换表单节点的情况。
 */
function submitLoginOnEnter(event) {
  const isEnter = event.key === "Enter" || event.code === "Enter" || event.code === "NumpadEnter" || event.keyCode === 13;
  if (!isEnter || event.repeat || event.isComposing || event.keyCode === 229) { return; }

  const target = event.target;
  if (!(target instanceof HTMLInputElement)) { return; }

  const form = target.closest("form[data-enter-submit='true']");
  if (!(form instanceof HTMLFormElement)) { return; }

  event.preventDefault();
  if (!form.checkValidity()) {
    form.reportValidity();
    return;
  }

  HTMLFormElement.prototype.submit.call(form);
}

if (!globalThis.__snetLoginEnterBound) {
  document.addEventListener("keydown", submitLoginOnEnter, true);
  globalThis.__snetLoginEnterBound = true;
}
