// Skanyxx JavaScript
console.log('site.js loaded');

// Theme definitions (like Visual Studio)
var themes = {
    'dark': {
        '--bg-primary': '#0a0a0a',
        '--bg-secondary': '#1a1a1a',
        '--bg-tertiary': '#2a2a2a',
        '--border-color': '#404040',
        '--text-primary': 'rgba(255, 255, 255, 0.95)',
        '--text-secondary': 'rgba(255, 255, 255, 0.7)',
        '--text-muted': 'rgba(255, 255, 255, 0.5)',
        '--accent-color': '#646cff',
        '--accent-hover': '#535bf2',
        '--success-color': '#4ade80',
        '--warning-color': '#fbbf24',
        '--error-color': '#f87171'
    },
    'light': {
        '--bg-primary': '#ffffff',
        '--bg-secondary': '#f3f3f3',
        '--bg-tertiary': '#e8e8e8',
        '--border-color': '#d4d4d4',
        '--text-primary': 'rgba(0, 0, 0, 0.9)',
        '--text-secondary': 'rgba(0, 0, 0, 0.7)',
        '--text-muted': 'rgba(0, 0, 0, 0.5)',
        '--accent-color': '#0066cc',
        '--accent-hover': '#0052a3',
        '--success-color': '#16a34a',
        '--warning-color': '#ca8a04',
        '--error-color': '#dc2626'
    },
    'blue': {
        '--bg-primary': '#1e3a5f',
        '--bg-secondary': '#264a73',
        '--bg-tertiary': '#2d5a87',
        '--border-color': '#3d6a97',
        '--text-primary': 'rgba(255, 255, 255, 0.95)',
        '--text-secondary': 'rgba(255, 255, 255, 0.8)',
        '--text-muted': 'rgba(255, 255, 255, 0.6)',
        '--accent-color': '#68b5ff',
        '--accent-hover': '#4da3ff',
        '--success-color': '#4ade80',
        '--warning-color': '#fbbf24',
        '--error-color': '#f87171'
    },
    'high-contrast': {
        '--bg-primary': '#000000',
        '--bg-secondary': '#000000',
        '--bg-tertiary': '#1a1a1a',
        '--border-color': '#ffffff',
        '--text-primary': '#ffffff',
        '--text-secondary': '#ffffff',
        '--text-muted': '#ffff00',
        '--accent-color': '#00ffff',
        '--accent-hover': '#00cccc',
        '--success-color': '#00ff00',
        '--warning-color': '#ffff00',
        '--error-color': '#ff0000'
    }
};

// Theme management
function applyTheme(theme) {
    var root = document.documentElement;
    var themeVars = themes[theme] || themes['dark'];

    // Apply all CSS variables
    for (var key in themeVars) {
        root.style.setProperty(key, themeVars[key]);
    }

    // Update body class
    document.body.className = document.body.className.replace(/\b\w+-theme\b/g, '');
    document.body.classList.add(theme + '-theme');

    console.log('Theme applied:', theme);
}

// Load and apply saved theme on page load
document.addEventListener('DOMContentLoaded', function() {
    console.log('DOM ready - site.js');

    // Load theme from settings
    fetch('/api/settings/theme')
        .then(function(r) { return r.ok ? r.text() : 'dark'; })
        .then(function(theme) {
            applyTheme(theme.replace(/"/g, '') || 'dark');
        })
        .catch(function() { applyTheme('dark'); });
});

// Make applyTheme available globally
window.applyTheme = applyTheme;
