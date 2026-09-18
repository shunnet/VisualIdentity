// 图片加载兜底：预览图取不到时自动退回原图，绝不在页面上留下破图图标。
// 用法：<img src="<预览地址>" data-original="<原图地址>" onerror="snetImageFallback(this)" />
(function () {
  window.snetImageFallback = function (img) {
    try {
      if (!img || img.dataset.snetFallbackDone === "1") { return; }
      img.dataset.snetFallbackDone = "1";
      var original = img.dataset.original;
      if (original && img.getAttribute("src") !== original) {
        img.setAttribute("src", original);   // 回退到原图（同一张图，只是没有预览加速）
        return;
      }
      img.classList.add("snet-image-failed"); // 连原图都拿不到：留一个明确的占位而不是破图
    } catch (err) { /* 兜底逻辑本身不允许影响页面 */ }
  };
  // 图片加载结束（成功或失败）时给容器打个标记：CSS 据此收起加载动画。
  // 用于项目详情里点开原图的大图弹窗（原图可能几十 MB，加载期间必须有反馈）。
  window.snetImageReady = function (img) {
    try {
      var host = img && img.closest ? img.closest(".ls-viewer") : null;
      if (host) { host.classList.add("img-ready"); }
    } catch (err) { /* 钩子本身不影响页面 */ }
  };
})();
