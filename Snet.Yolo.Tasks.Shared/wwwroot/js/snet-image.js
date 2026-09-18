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
})();
