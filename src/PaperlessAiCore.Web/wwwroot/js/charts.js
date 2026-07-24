// Minimalistisches Chart.js-Interop für Blazor - kein Framework/Build nötig.
window.chartInterop = {
    charts: {},

    _isDark: function () {
        return document.documentElement.classList.contains('dark');
    },

    _textColor: function () {
        return this._isDark() ? '#94a3b8' : '#64748b';
    },

    _gridColor: function () {
        return this._isDark() ? 'rgba(255,255,255,0.06)' : 'rgba(15,23,42,0.06)';
    },

    renderDonut: function (canvasId, labels, data, colors) {
        this.destroy(canvasId);
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;
        this.charts[canvasId] = new Chart(ctx, {
            type: 'doughnut',
            data: { labels: labels, datasets: [{ data: data, backgroundColor: colors, borderWidth: 0, hoverOffset: 6 }] },
            options: {
                cutout: '72%',
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: { titleColor: '#fff', bodyColor: '#fff' },
                },
            },
        });
    },

    renderBar: function (canvasId, labels, data, color) {
        this.destroy(canvasId);
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;
        this.charts[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: { labels: labels, datasets: [{ label: 'Anzahl Dokumente', data: data, backgroundColor: color, borderRadius: 6, maxBarThickness: 42 }] },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { display: false } },
                scales: {
                    x: { ticks: { color: this._textColor() }, grid: { display: false } },
                    y: { beginAtZero: true, ticks: { color: this._textColor() }, grid: { color: this._gridColor() } },
                },
            },
        });
    },

    renderDualLine: function (canvasId, labels, seriesA, seriesB, labelA, labelB) {
        this.destroy(canvasId);
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;
        this.charts[canvasId] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [
                    { label: labelA, data: seriesA, borderColor: '#6366f1', backgroundColor: 'rgba(99,102,241,0.1)', yAxisID: 'y', tension: 0.3, pointRadius: 0, fill: true },
                    { label: labelB, data: seriesB, borderColor: '#22c55e', backgroundColor: 'rgba(34,197,94,0.1)', yAxisID: 'y1', tension: 0.3, pointRadius: 0, fill: true },
                ],
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                plugins: { legend: { display: true, labels: { color: this._textColor(), boxWidth: 10, font: { size: 10 } } } },
                scales: {
                    x: { ticks: { color: this._textColor(), maxTicksLimit: 6 }, grid: { display: false } },
                    y: { position: 'left', beginAtZero: true, ticks: { color: this._textColor() }, grid: { color: this._gridColor() } },
                    y1: { position: 'right', beginAtZero: true, ticks: { color: this._textColor() }, grid: { display: false } },
                },
            },
        });
    },

    destroy: function (canvasId) {
        if (this.charts[canvasId]) {
            this.charts[canvasId].destroy();
            delete this.charts[canvasId];
        }
    },
};

// Download-Hilfsfunktion für Config-Export
window.downloadFile = function(filename, mimeType, base64Data) {
    const blob = new Blob([Uint8Array.from(atob(base64Data), c => c.charCodeAt(0))], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = filename; a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
};

// Vorgeladene Dashboard-Daten für Blazor abrufbar machen
window.getPaperLeoPreloaded = function(key) {
    if (!window.__paperleo_preloaded) return null;
    var val = window.__paperleo_preloaded[key];
    if (!val) return null;
    delete window.__paperleo_preloaded[key];
    return typeof val === 'string' ? val : JSON.stringify(val);
};
