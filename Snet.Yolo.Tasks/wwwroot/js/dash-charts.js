// 首页看板图表（Chart.js 封装）
window.dashCharts = {
  _c: {},
  line(id, labels, datasets) {
    const el = document.getElementById(id);
    if (!el || !window.Chart) return;
    if (this._c[id]) { this._c[id].destroy(); }
    this._c[id] = new Chart(el, {
      type: 'line',
      data: { labels, datasets },
      options: { responsive: true, maintainAspectRatio: false, animation: false,
        scales: { y: { beginAtZero: true, max: 100 }, x: { ticks: { maxTicksLimit: 8 } } },
        plugins: { legend: { labels: { boxWidth: 12 } } } }
    });
  },
  bar(id, labels, data, label) {
    const el = document.getElementById(id);
    if (!el || !window.Chart) return;
    if (this._c[id]) { this._c[id].destroy(); }
    this._c[id] = new Chart(el, {
      type: 'bar',
      data: { labels, datasets: [{ label, data, backgroundColor: 'rgba(17,17,17,.55)', borderColor: '#111', borderWidth: 1 }] },
      options: { responsive: true, maintainAspectRatio: false, animation: false, plugins: { legend: { display: false } } }
    });
  }
};
