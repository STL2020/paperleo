// Minimalistisches Theme-Interop für Blazor - kein Framework nötig.
window.themeInterop = {
    init: function () {
        const saved = localStorage.getItem('paperlessai-theme');
        // Dark Mode ist der gestaltete Standard-Look dieses Produkts - nur wenn
        // der Nutzer aktiv Light Mode gewählt hat, wird das respektiert.
        const isDark = saved ? saved === 'dark' : true;
        document.documentElement.classList.toggle('dark', isDark);
        return isDark;
    },
    toggle: function () {
        const isDark = document.documentElement.classList.toggle('dark');
        localStorage.setItem('paperlessai-theme', isDark ? 'dark' : 'light');
        return isDark;
    },
    isDark: function () {
        return document.documentElement.classList.contains('dark');
    },
};

// Sofort beim Laden anwenden (verhindert "Flash of Light Mode" vor Blazor-Start).
window.themeInterop.init();
