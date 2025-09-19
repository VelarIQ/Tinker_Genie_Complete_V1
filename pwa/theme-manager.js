// Theme Manager for TinkerGenie
const ThemeManager = {
    // Get current theme from localStorage or default to dark
    getCurrentTheme: function() {
        return localStorage.getItem('theme') || 'dark';
    },
    
    // Set theme and save to localStorage
    setTheme: function(theme) {
        localStorage.setItem('theme', theme);
        document.documentElement.setAttribute('data-theme', theme);
        this.updateThemeStyles(theme);
    },
    
    // Toggle between dark and light
    toggleTheme: function() {
        const currentTheme = this.getCurrentTheme();
        const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
        this.setTheme(newTheme);
        
        // Reload page to apply new theme files
        window.location.reload();
    },
    
    // Update CSS variables based on theme
    updateThemeStyles: function(theme) {
        if (theme === 'light') {
            document.documentElement.style.setProperty('--bg-primary', '#ffffff');
            document.documentElement.style.setProperty('--bg-secondary', '#f6f8fa');
            document.documentElement.style.setProperty('--text-primary', '#24292f');
            document.documentElement.style.setProperty('--text-secondary', '#57606a');
            document.documentElement.style.setProperty('--border-color', '#d0d7de');
        } else {
            document.documentElement.style.setProperty('--bg-primary', '#0d1117');
            document.documentElement.style.setProperty('--bg-secondary', '#161b22');
            document.documentElement.style.setProperty('--text-primary', '#e6edf3');
            document.documentElement.style.setProperty('--text-secondary', '#8b949e');
            document.documentElement.style.setProperty('--border-color', '#30363d');
        }
    },
    
    // Initialize theme on page load
    init: function() {
        const theme = this.getCurrentTheme();
        this.setTheme(theme);
    }
};

// Initialize theme when script loads
ThemeManager.init();
