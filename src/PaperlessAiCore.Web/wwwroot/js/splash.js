(function () {
    var ICONS = {
        folder: '<path d="M2 5a1 1 0 0 1 1-1h5l2 2h9a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V5z"/>',
        tag: '<path d="M12 2h7a1 1 0 0 1 1 1v7l-9 9-8-8 9-9z"/>',
        gear: '<circle cx="12" cy="12" r="3.2"/><path d="M12 2v3M12 19v3M4.2 4.2l2.1 2.1M17.7 17.7l2.1 2.1M2 12h3M19 12h3M4.2 19.8l2.1-2.1M17.7 6.3l2.1-2.1"/>',
        search: '<circle cx="10.5" cy="10.5" r="6.5"/><path d="M20 20l-5-5"/>',
        document: '<path d="M6 2h9l5 5v15a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1z"/><path d="M15 2v5h5"/>'
    };

    function buildPattern() {
        var container = document.getElementById('splash-pattern');
        if (!container) return;
        var names = Object.keys(ICONS);
        var html = '';
        var positions = [
            [8, 8, 28], [82, 5, 24], [5, 75, 22], [88, 80, 26], [45, 6, 20],
            [50, 88, 22], [15, 42, 24], [80, 45, 28], [30, 25, 18], [65, 70, 20],
            [92, 25, 18], [3, 55, 20], [70, 15, 22], [25, 85, 24]
        ];
        for (var i = 0; i < positions.length; i++) {
            var p = positions[i];
            var icon = names[i % names.length];
            html += '<svg width="' + p[2] + '" height="' + p[2] + '" viewBox="0 0 24 24" ' +
                'style="position:absolute;top:' + p[1] + '%;left:' + p[0] +
                '%;stroke:#ffd700;fill:none;stroke-width:1.2;" aria-hidden="true">' + ICONS[icon] + '</svg>';
        }
        container.innerHTML = html;
    }

    function setProgress(pct, text) {
        var fill = document.getElementById('splash-progress-fill');
        var txt = document.getElementById('splash-status-text');
        var track = document.getElementById('splash-progress-track');
        if (fill) fill.style.width = Math.min(100, pct) + '%';
        if (txt && text) txt.textContent = text;
        if (track) track.setAttribute('aria-valuenow', String(Math.round(pct)));
    }

    function dismiss() {
        var splash = document.getElementById('app-splash');
        if (!splash) return;
        splash.classList.add('fade-out');
        setTimeout(function () {
            if (splash.parentNode) splash.parentNode.removeChild(splash);
        }, 600);
    }

    function loadDashboardData() {
        setProgress(10, 'Verbindung wird hergestellt…');

        var settled = 0;
        var total = 3;
        window.__paperleo_preloaded = {};

        function step(key, data) {
            settled++;
            if (key && data) window.__paperleo_preloaded[key] = data;
            var pct = 10 + (settled / total) * 85;
            var msgs = ['Dashboard-Daten werden geladen…', 'Paperless-Status wird geprüft…', 'Fast fertig…'];
            setProgress(pct, msgs[Math.min(settled - 1, msgs.length - 1)]);
            if (settled >= total) {
                setProgress(100, 'Bereit.');
                setTimeout(dismiss, 300);
            }
        }

        fetch('/api/dashboard')
            .then(function(r) { return r.json(); })
            .then(function(d) { step('dashboard', d); })
            .catch(function() { step(null, null); });

        fetch('/api/settings')
            .then(function(r) { return r.json(); })
            .then(function(d) { step('settings', d); })
            .catch(function() { step(null, null); });

        fetch('/api/dashboard/logs')
            .then(function(r) { return r.json(); })
            .then(function() { step(null, null); })
            .catch(function() { step(null, null); });

        // Failsafe: nach 8s trotzdem fortfahren
        setTimeout(function() {
            if (settled < total) {
                setProgress(100, 'Timeout – starte trotzdem…');
                dismiss();
            }
        }, 8000);
    }

    document.addEventListener('DOMContentLoaded', function () {
        buildPattern();
        loadDashboardData();
    });
})();
